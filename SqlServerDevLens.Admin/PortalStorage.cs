using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SqlServerDevLens.Core;

namespace SqlServerDevLens.Admin;

public sealed record DbConfig(string InstanceId, string Host, int Port, string Database, string Username, bool EncryptConnection = true, bool TrustServerCertificate = false);
public sealed record PostmanConfig(string WorkspaceId, string CollectionId);
public sealed record OperatorConfig(string Mode, DateTimeOffset? AutomaticUntilUtc, string[] AutomaticCategories, PostmanConfig? Postman);
public static class PortalStorage
{
    private const string Root = "/etc/devlens";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static bool ValidName(string? name) => name is not null &&
        Regex.IsMatch(name, "^[a-z][a-z0-9-]{0,31}$");
    public static bool ValidPostmanId(string? id) => id is { Length: > 0 and <= 160 } &&
        Regex.IsMatch(id, "^[A-Za-z0-9_-]+$");
    public static string Folder(string name)
    {
        if (!ValidName(name)) throw new ArgumentException("Invalid instance name.");
        var folder = Path.Combine(Root, name);
        if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Unknown or linked instance directory.");
        return folder;
    }
    public static string[] Instances() => Directory.Exists(Root) ? Directory.GetDirectories(Root)
        .Select(Path.GetFileName).Where(ValidName).Select(x => x!)
        .OrderBy(x => x, StringComparer.Ordinal).ToArray() : [];
    public static DbConfig Database(string name) =>
        JsonSerializer.Deserialize<DbConfig>(File.ReadAllText(Path.Combine(Folder(name), "instance.json")), Json)
        ?? throw new InvalidOperationException("No database configuration.");
    public static OperatorConfig Operator(string name)
    {
        var file = Path.Combine(Folder(name), "operator.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<OperatorConfig>(File.ReadAllText(file), Json)
            ?? throw new InvalidOperationException("Invalid operator config.")
            : new("read-only", null, [], null);
    }
    public static bool HasSecret(string name, string kind) => File.Exists(SecretPath(name, kind));
    private static string SecretPath(string name, string kind)
    {
        if (kind is not ("sql-password" or "tunnel-key" or "postman-key"))
            throw new ArgumentException("Unknown credential.");
        return Path.Combine(Folder(name), kind);
    }
    public static string ReadPostmanKey(string name)
    {
        var path = SecretPath(name, "postman-key");
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length > 4096 || (fi.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Configure the Postman key first.");
        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }
    private static void Replace(string destination, string content, string owner, string mode)
    {
        var tmp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            // Set restrictive mode while still owned by root. The admin unit deliberately
            // lacks CAP_FOWNER, so chmod after chown would fail with EPERM.
            File.SetUnixFileMode(tmp, mode == "0400" ? UnixFileMode.UserRead :
                UnixFileMode.UserRead | UnixFileMode.GroupRead);
            Run("/usr/bin/chown", owner, tmp);
            File.Move(tmp, destination, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static void SaveDatabase(string name, DbConfig config)
    {
        _ = Folder(name);
        if (config.InstanceId != name || string.IsNullOrWhiteSpace(config.Host) ||
            config.Host.Length > 255 || config.Host.Any(char.IsControl) ||
            config.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(config.Database) ||
            config.Database.Length > 128 || config.Database.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(config.Username) || config.Username.Length > 128 ||
            config.Username.Any(char.IsControl))
            throw new ArgumentException("Invalid SQL connection fields.");
        Replace(Path.Combine(Folder(name), "instance.json"), JsonSerializer.Serialize(config, Json),
            "devlens-" + name + ":devlens-" + name, "0400");
    }
    public static void SaveOperator(string name, OperatorConfig policy)
    {
        _ = Folder(name);
        if (policy.Mode is not ("read-only" or "full") ||
            policy.AutomaticCategories.Distinct(StringComparer.Ordinal).Count() != policy.AutomaticCategories.Length ||
            policy.AutomaticCategories.Any(x => !SessionAuthorization.Categories.Contains(x, StringComparer.Ordinal)) ||
            policy.AutomaticCategories.Length > 0 && (policy.Mode != "full" ||
                policy.AutomaticUntilUtc is null ||
                policy.AutomaticUntilUtc <= DateTimeOffset.UtcNow ||
                policy.AutomaticUntilUtc > DateTimeOffset.UtcNow.AddHours(36)) ||
            policy.Postman is { } p && (!ValidPostmanId(p.WorkspaceId) || !ValidPostmanId(p.CollectionId)) ||
            policy.Mode == "read-only" && policy.Postman is not null)
            throw new ArgumentException("Invalid/expired operator policy.");
        Replace(Path.Combine(Folder(name), "operator.json"), JsonSerializer.Serialize(policy, Json),
            "root:devlens-" + name, "0440");
    }
    public static void SaveSecret(string name, string kind, string value)
    {
        var path = SecretPath(name, kind);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 ||
            value.Any(c => c is '\0' or '\r' or '\n'))
            throw new ArgumentException("Invalid or empty credential.");
        Replace(path, value, kind == "postman-key" ? "root:devlens-" + name : "root:root",
            kind == "postman-key" ? "0440" : "0400");
    }
    public static void Run(string command, params string[] args)
    {
        var psi = new ProcessStartInfo(command) { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in args) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Administrative command could not start.");
        if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Administrative command timed out."); }
        if (process.ExitCode != 0) throw new InvalidOperationException("Administrative command failed; see protected system journal.");
    }
    public static void CreateInstance(string name, string tunnel, int dashboardPort)
    {
        if (!ValidName(name) || !System.Text.RegularExpressions.Regex.IsMatch(tunnel, "^tunnel_[A-Za-z0-9]+$") ||
            dashboardPort is < 1024 or > 65535)
            throw new ArgumentException("Invalid instance identity, tunnel or port.");
        // Provisioning creates an OS user: ask PID 1 to run the fixed, validated root-owned installer.
        // The web process itself retains a read-only system filesystem sandbox.
        Run("/usr/bin/systemd-run", "--wait", "--collect", "--unit=devlens-provision-" + name,
            "/usr/bin/bash", "/opt/devlens/linux/configure-instance.sh", name, tunnel, dashboardPort.ToString());
    }
    public static void Service(string name, string action)
    {
        _ = Folder(name);
        if (action is not ("start" or "stop" or "restart" or "enable"))
            throw new ArgumentException("Unsupported service operation.");
        if (action == "enable")
            Run("/usr/bin/bash", "/opt/devlens/linux/enable-instance.sh", name);
        else if (action == "stop")
            Run("/usr/bin/systemctl", "stop", "devlens-tunnel@" + name + ".service", "devlens@" + name + ".service");
        else
            Run("/usr/bin/systemctl", action, "devlens@" + name + ".service", "devlens-tunnel@" + name + ".service");
    }
    public static string SqlCheck(string name)
    {
        _ = Folder(name);
        // Dedicated service name permits the runtime to verify named-user systemd credential ACLs.
        var unit = "devlens-sqlcheck-" + name;
        try
        {
            Run("/usr/bin/systemd-run", "--wait", "--pipe", "--collect", "--unit=" + unit,
                "--property=User=devlens-" + name,
                "--property=Group=devlens-" + name,
                "--property=LoadCredential=sql-password:/etc/devlens/" + name + "/sql-password",
                "/opt/devlens/app/SqlServerDevLens.Headless",
                "--check", "/etc/devlens/" + name + "/instance.json");
            return "SQL connection verified successfully.";
        }
        catch (Exception)
        {
            return "SQL test failed. Check hostname, database, login, TLS settings and SQL connectivity. See system journal for details.";
        }
    }
    public static string UnitState(string name, bool tunnel)
    {
        _ = Folder(name);
        var unit = (tunnel ? "devlens-tunnel@" : "devlens@") + name + ".service";
        var psi = new ProcessStartInfo("/usr/bin/systemctl") { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("is-active");
        psi.ArgumentList.Add(unit);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not check service");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5000);
        return output is "active" or "inactive" or "failed" or "activating" or "deactivating" ? output : "unavailable";
    }
    public static string ServiceStatus(string name)
    {
        _ = Folder(name);
        return File.Exists(Path.Combine("/run/systemd/system", "devlens@" + name + ".service"))
            ? "Systemd runtime" : "See health command";
    }
}
