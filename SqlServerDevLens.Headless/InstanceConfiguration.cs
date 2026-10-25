using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SqlServerDevLens.Core;

namespace SqlServerDevLens.Headless;

// No secret, authentication override, connection string or authorization flags in this DTO.
public sealed record InstanceConfiguration(string InstanceId, string Host, int Port, string Database, string Username)
{
    public ConnectionProfile Profile()
    {
        if (!Regex.IsMatch(InstanceId ?? "", "^[a-z][a-z0-9-]{0,31}$") ||
            string.IsNullOrWhiteSpace(Database) || Database != Database.Trim() ||
            Database.Any(char.IsControl)) throw new ArgumentException("Invalid instance identity or database.");
        var profile = new ConnectionProfile(Host, Port, Database, AuthenticationMode.Sql, Username, true, false);
        profile.Validate();
        return profile;
    }
    public static InstanceConfiguration Load(string path)
    {
        CheckPrivateFile(path);
        if (new FileInfo(path).Length > 8192) throw new ArgumentException("Instance config exceeds 8 KiB.");
        var config = JsonSerializer.Deserialize<InstanceConfiguration>(File.ReadAllText(path), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Instance configuration is required.");
        config.Profile();
        if (new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!).Name != config.InstanceId)
            throw new ArgumentException("Config directory must match instanceId.");
        return config;
    }
    public static string ReadPassword(string? expectedInstance = null)
    {
        // systemd LoadCredential gives each service its own read-only credentials mount.
        var directory = Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("A protected systemd credentials directory is required.");
        var path = Path.Combine(directory, "sql-password");
        // systemd may expose group-readable credential files inside its private mount.
        // Require the mount directory itself to exclude all group/other traversal.
        var privateMount = OperatingSystem.IsLinux() && Directory.Exists(directory) &&
            (File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) == 0;
        try { CheckPrivateFile(path, privateMount); }
        catch (ArgumentException) when (OperatingSystem.IsLinux() && expectedInstance is not null &&
            directory == $"/run/credentials/devlens@{expectedInstance}.service" &&
            SystemdCredentialAcl.IsPrivate(directory, true) && SystemdCredentialAcl.IsPrivate(path, false))
        { /* systemd's named-user ACL mask is not a shared-group grant. */ }
        if (new FileInfo(path).Length > 4096) throw new ArgumentException("SQL credential exceeds 4 KiB.");
        var value = File.ReadAllText(path).TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(value) || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Invalid SQL credential file.");
        return value;
    }
    private static void CheckPrivateFile(string path, bool privateCredentialMount = false)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Expected a regular private configuration or credential file.");
        if (OperatingSystem.IsLinux() && (File.GetUnixFileMode(path) &
            (UnixFileMode.GroupWrite | (privateCredentialMount ? 0 : UnixFileMode.GroupRead) | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new ArgumentException("Configuration and credentials must not be accessible to group or other users.");
    }
}
