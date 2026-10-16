using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SqlServerDevLens.Runtime;

if (args.Contains("--desktop"))
{
    var pipeArgument = Array.IndexOf(args, "--pipe-name");
    var pipeName = pipeArgument >= 0 && pipeArgument + 1 < args.Length ? args[pipeArgument + 1] : null;
    try { await DesktopBridge.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), pipeName); }
    catch (Exception) { Console.Error.WriteLine("Desktop MCP server unavailable or disconnected. Open DevLens, test SQL and confirm the server is Running; reconnect the plugin."); Environment.ExitCode = 1; }
    return;
}

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
