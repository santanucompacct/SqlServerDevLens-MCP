using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Runtime;

var desktopMode = args.Contains("--desktop");
var pipeArgument = Array.IndexOf(args, "--pipe-name");
var pipeName = pipeArgument >= 0 && pipeArgument + 1 < args.Length ? args[pipeArgument + 1] : null;
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
var server = builder.Services.AddMcpServer().WithStdioServerTransport();
if (desktopMode)
{
    builder.Services.AddSingleton(new DesktopStatusClient(pipeName));
    server.WithTools<DesktopStatusTools>();
}
else server.WithTools<StatusTools>();
await builder.Build().RunAsync();

[McpServerToolType]
public sealed class DesktopStatusTools(DesktopStatusClient desktop)
{
    [McpServerTool(Name = "get_server_status"), Description("Returns desktop MCP server status or desktop_unavailable. Automatically reconnects to DevLens on each call; does not query SQL.")]
    public Task<CallToolResult> GetServerStatus(CancellationToken cancellationToken) => desktop.GetStatusAsync(cancellationToken);
}

[McpServerToolType]
public sealed class StatusTools
{
    [McpServerTool(Name = "get_server_status"), Description("Returns MCP host readiness. Does not connect to SQL Server or inspect desktop state.")]
    public static object GetServerStatus() => new
    {
        service = "SQL Server DevLens MCP", status = "ready", transport = "stdio",
        sqlConfigured = false, sqlConnected = false, postmanImplemented = false,
        message = "Transport smoke test only. Desktop profiles and SQL tools are not connected to this host."
    };
}
