using System.Runtime.InteropServices;
using ModelContextProtocol.Server;
using SqlServerDevLens.Core;
using SqlServerDevLens.Headless;

try
{
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    using var terminate = OperatingSystem.IsLinux() ? PosixSignalRegistration.Create(PosixSignal.SIGTERM,
        context => { context.Cancel = true; stop.Cancel(); }) : null;
    if (args.Length == 2 && args[0] == "--bridge")
    {
        await UnixRuntime.BridgeAsync(args[1], stop.Token);
        return 0;
    }
    if (!((args.Length == 2 && args[0] is "--config" or "--check") || (args.Length == 4 && args[0] == "--serve" && args[2] == "--socket")))
        throw new ArgumentException("Expected --config PATH, --check PATH, --serve PATH --socket PATH, or --bridge SOCKET.");
    var config = InstanceConfiguration.Load(args[1]);
    using var database = new SqlDatabase(config.Profile(), InstanceConfiguration.ReadPassword());
    if (args[0] == "--serve")
    {
        Console.Error.WriteLine($"Instance {config.InstanceId}: private read-only runtime starting.");
        await UnixRuntime.ServeAsync(args[3], ReadOnlyCatalog.Create(config, database), stop.Token);
        return 0;
    }
    if (args[0] == "--check")
    {
        await database.GetDatabaseInfoAsync(stop.Token);
        Console.Error.WriteLine($"Instance {config.InstanceId}: SQL metadata check succeeded.");
        return 0;
    }
    // stdout is exclusively MCP; the runtime has no desktop pipe, listener or approval endpoint.
    await using var server = McpServer.Create(new StreamServerTransport(Console.OpenStandardInput(), Console.OpenStandardOutput()),
        new McpServerOptions { ToolCollection = [..ReadOnlyCatalog.Create(config, database)] });
    Console.Error.WriteLine($"Instance {config.InstanceId}: read-only stdio runtime started.");
    try { await server.RunAsync(stop.Token); }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    return 0;
}
catch (Exception)
{
    // Never print provider exceptions, config contents, arguments or connection strings.
    Console.Error.WriteLine("Headless startup/check failed. Check private configuration, credential permissions, SQL access and TLS. No automatic SQL retry.");
    return 1;
}
