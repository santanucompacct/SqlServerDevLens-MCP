using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;

namespace SqlServerDevLens.Headless;

public static class ReadOnlyCatalog
{
    public static readonly IReadOnlySet<string> AllowedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "get_server_status", "get_database_info", "list_database_objects", "describe_table", "get_table_schema",
        "read_table_page", "query_read_only", "get_procedure_definition", "read_result_page", "release_result"
    };
    public static McpServerTool[] Create(InstanceConfiguration config, ISqlDatabase database)
    {
        var tools = new List<McpServerTool>
        {
            McpServerTool.Create(() => new {
                service = "SQL Server DevLens MCP", mode = "linux-headless-read-only", status = "running",
                instanceId = config.InstanceId, database = database.Database, sqlConfigured = true, sqlConnected = false,
                lastSqlTestSucceeded = (bool?)null, postmanImplemented = false, mutationsEnabled = false,
                capturedResultMaxCharacters = CapturedResults.MaxCaptureCharacters, capturedResultsExpireMinutes = 15,
                message = "Process status only; call get_database_info to verify SQL. No execution, mutation, Postman or approval tools. Captures belong to this process and clear on restart."
            }, new() { Name = "get_server_status", Description = "Read headless instance identity, selected database and read-only policy. Does not test SQL connectivity." })
        };
        tools.AddRange(new DatabaseTools(database).CreateTools());
        // Positive allowlist: future desktop tools cannot accidentally become headless capabilities.
        tools.AddRange(new ProcedureTools(database.Procedures).CreateTools().Where(t =>
            t.ProtocolTool.Name is "get_table_schema" or "get_procedure_definition"));
        tools.AddRange(new CapturedResultTools(database.Results).CreateTools());
        if (tools.Count != AllowedNames.Count || tools.Any(t => !AllowedNames.Contains(t.ProtocolTool.Name)))
            throw new InvalidOperationException("Read-only catalog mismatch.");
        return tools.ToArray();
    }
}
