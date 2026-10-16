using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<StatusTools>();
await builder.Build().RunAsync();

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
