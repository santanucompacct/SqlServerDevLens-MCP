using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;

namespace SqlServerDevLens.Headless;

// Opt-in Linux-only capabilities. Default/missing config retains the original ten-tool read-only mode.
// Saved policies are root-managed; auto-authorization always expires and is cleared on runtime restart.
public sealed record PostmanInstance(string WorkspaceId, string CollectionId);
public sealed record OperatorSettings(string Mode, DateTimeOffset? AutomaticUntilUtc,
    string[]? AutomaticCategories, PostmanInstance? Postman)
{
    public static OperatorSettings Load(string instance)
    {
        var path = $"/etc/devlens/{instance}/operator.json";
        if (!File.Exists(path)) return new("read-only", null, null, null);
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 8192 ||
            (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherRead |
                UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("Operator configuration is not private.");
        var settings = JsonSerializer.Deserialize<OperatorSettings>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidOperationException("Missing operator configuration.");
        if (settings.Mode is not ("read-only" or "full" or "sql-only"))
            throw new InvalidOperationException("Unsupported operator mode.");
        var names = settings.AutomaticCategories ?? [];
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            names.Any(x => !SessionAuthorization.Categories.Contains(x, StringComparer.Ordinal)))
            throw new InvalidOperationException("Unknown authorization category.");
        if (names.Length > 0 && (settings.AutomaticUntilUtc is null ||
            settings.AutomaticUntilUtc > DateTimeOffset.UtcNow.AddHours(36)))
            throw new InvalidOperationException("Auto authorization needs an expiry within 36 hours.");
        if (settings.Postman is { } p && (!ValidId(p.WorkspaceId) || !ValidId(p.CollectionId)))
            throw new InvalidOperationException("Invalid Postman selection.");
        if (settings.Mode == "sql-only" && (settings.Postman is not null || names.Any(x => x.StartsWith("postman_", StringComparison.Ordinal))))
            throw new InvalidOperationException("SQL-only mode cannot enable Postman.");
        if (settings.Mode == "read-only" && (names.Length > 0 || settings.Postman is not null))
            throw new InvalidOperationException("Read-only mode cannot enable actions or Postman.");
        return settings;
    }
    private static bool ValidId(string? id) => id is { Length: > 0 and <= 160 } &&
        Regex.IsMatch(id, "^[A-Za-z0-9_-]+$");

    public SessionAuthorization NewAuthorization()
    {
        var policy = new SessionAuthorization();
        if (Mode is "full" or "sql-only" && AutomaticUntilUtc > DateTimeOffset.UtcNow)
            foreach (var category in AutomaticCategories ?? []) policy.Set(category, true);
        return policy;
    }
    public static async Task ExpireAsync(SessionAuthorization authorization, DateTimeOffset? expiry, CancellationToken token)
    {
        if (expiry is null) return;
        var remaining = expiry.Value - DateTimeOffset.UtcNow;
        try { if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        authorization.Reset();
    }

    public async Task<(PostmanSelection? Selection, PostmanWorkflow? Workflow)> SetupPostmanAsync(
        string instance, SessionAuthorization authorization, CapturedResults results, CancellationToken token)
    {
        if (Mode != "full" || Postman is null) return (null, null);
        var path = $"/etc/devlens/{instance}/postman-key";
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 4096 ||
            (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherRead |
                UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("Postman key is not available privately for this instance.");
        var key = File.ReadAllText(path).TrimEnd('\r','\n');
        using var client = new PostmanClient();
        var workspaces = await client.WorkspacesAsync(key, token);
        var workspace = workspaces.SingleOrDefault(x => x.Id == Postman.WorkspaceId)
            ?? throw new InvalidOperationException("Configured Postman workspace is not accessible.");
        var collections = await client.CollectionsAsync(key, workspace.Id, token);
        var collection = collections.SingleOrDefault(x => x.Id == Postman.CollectionId)
            ?? throw new InvalidOperationException("Configured Postman collection is not accessible.");
        var selection = await client.SelectAsync(key, workspace, collection, token);
        var backupDir = Path.Combine($"/var/lib/devlens-{instance}", "PostmanBackups");
        var workflow = new PostmanWorkflow(new PostmanActionBackend(key, selection,
            new PrivatePostmanBackupStore(backupDir)), review: null, authorization: authorization, results: results);
        return (selection, workflow);
    }

    public McpServerTool[] CreateTools(InstanceConfiguration config, SqlDatabase database,
        SessionAuthorization authorization, PostmanSelection? selection, PostmanWorkflow? workflow)
    {
        if (Mode != "full") return ReadOnlyCatalog.Create(config, database);
        var tools = new List<McpServerTool>
        {
            McpServerTool.Create(() => new {
                service = "SQL Server DevLens MCP", mode = Mode == "sql-only" ? "linux-headless-sql-only" : "linux-headless-full",
                status = "running", instanceId = config.InstanceId, database = config.Database,
                mutationsEnabled = true, postmanImplemented = selection is not null,
                automaticUntilUtc = AutomaticUntilUtc,
                authorizedCategories = authorization.Snapshot(),
                sqlConnected = false,
                message = "Configuration status only. Verify SQL with get_database_info. Auto approvals are operator-scoped and expire; each action still requires a separate exact prepare and single-use apply. No automatic retries."
            }, new() { Name = "get_server_status", Description = "Read instance identity, tool mode and operator authorization snapshot. Does not test SQL connectivity." })
        };
        tools.AddRange(new DatabaseTools(database).CreateTools());
        tools.AddRange(new ProcedureTools(database.Procedures).CreateTools());
        tools.AddRange(new CapturedResultTools(database.Results).CreateTools());
        if (selection is not null) tools.AddRange(new PostmanTools(selection, workflow).CreateTools());
        if (tools.GroupBy(t => t.ProtocolTool.Name, StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Duplicate Linux tool name.");
        return [..tools];
    }
}
