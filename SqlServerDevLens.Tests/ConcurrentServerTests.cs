using System.IO.Pipes;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class ConcurrentServerTests
{
    private sealed class ClientConnection(McpClient client, NamedPipeClientStream pipe) : IAsyncDisposable
    {
        public McpClient Client { get; } = client;
        public async ValueTask DisposeAsync()
        { try { await Client.DisposeAsync(); } finally { await pipe.DisposeAsync(); } }
    }
    private static async Task<ClientConnection> ConnectAsync(string name, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(cancellationToken);
            var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe), cancellationToken: cancellationToken);
            return new ClientConnection(client, pipe);
        }
        catch { await pipe.DisposeAsync(); throw; }
    }

    private static async Task AssertStatus(McpClient client, CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        Assert.Single(tools); Assert.Equal("get_server_status", tools[0].Name);
        var result = await client.CallToolAsync("get_server_status", cancellationToken: cancellationToken);
        Assert.False(result.IsError ?? false);
        using var status = JsonDocument.Parse(Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.Equal("running", status.RootElement.GetProperty("status").GetString());
    }
    private static Task AssertStatus(ClientConnection connection, CancellationToken cancellationToken) => AssertStatus(connection.Client, cancellationToken);

    [Fact]
    public async Task IdleClientDoesNotBlockConcurrentDiscoveryAndToolCalls()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        await server.StartAsync(DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        // Simulate a discovery client which connects but never sends initialize.
        await using var idle = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await idle.ConnectAsync(timeout.Token);
        await using var first = await ConnectAsync(name, timeout.Token);
        await using var second = await ConnectAsync(name, timeout.Token);
        await using var third = await ConnectAsync(name, timeout.Token);
        await Task.WhenAll(AssertStatus(first, timeout.Token), AssertStatus(second, timeout.Token), AssertStatus(third, timeout.Token));
        await first.DisposeAsync();
        await AssertStatus(second, timeout.Token);
        await using var replacement = await ConnectAsync(name, timeout.Token);
        await Task.WhenAll(AssertStatus(replacement, timeout.Token), AssertStatus(third, timeout.Token));
    }

    [Fact]
    public async Task StopDisconnectsEveryClientIncludingIdleAndAllowsRestart()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        await server.StartAsync(DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var first = await ConnectAsync(name, timeout.Token);
        await using var second = await ConnectAsync(name, timeout.Token);
        await using var idle = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await idle.ConnectAsync(timeout.Token);
        await Task.WhenAll(server.StopAsync(), server.StopAsync()).WaitAsync(timeout.Token);
        Assert.False(server.IsRunning);
        await Assert.ThrowsAnyAsync<Exception>(async () => await first.Client.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
        await Assert.ThrowsAnyAsync<Exception>(async () => await second.Client.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
        var buffer = new byte[1];
        try { Assert.Equal(0, await idle.ReadAsync(buffer, timeout.Token)); }
        catch (IOException error) when (error.InnerException is System.Net.Sockets.SocketException
            { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionReset })
        { /* Unix may report reset instead of EOF when Stop closes an idle session. */ }
        await server.StartAsync(DateTimeOffset.UtcNow);
        await using var restarted = await ConnectAsync(name, timeout.Token);
        await AssertStatus(restarted, timeout.Token);
    }

    [Fact]
    public async Task FullCapacityCanBeReusedAfterDisconnectAndAfterRestart()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await server.StartAsync(DateTimeOffset.UtcNow);
            var clients = new List<ClientConnection>();
            try
            {
                for (var i = 0; i < DesktopMcpServer.MaxConcurrentSessions; i++)
                    clients.Add(await ConnectAsync(name, timeout.Token));
                await Task.WhenAll(clients.Select(client => AssertStatus(client, timeout.Token)));
                await clients[0].DisposeAsync();
                await using var replacement = await ConnectAsync(name, timeout.Token);
                await AssertStatus(replacement, timeout.Token);
                await server.StopAsync().WaitAsync(timeout.Token);
            }
            finally { foreach (var client in clients) await client.DisposeAsync(); }
        }
    }

    [Fact]
    public async Task IndependentStdioAdaptersCanDiscoverAndCallTogether()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        await server.StartAsync(DateTimeOffset.UtcNow);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SqlServerDevLens.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var dll = Path.Combine(root.FullName, "SqlServerDevLens.McpHost", "bin", "Release", "net10.0", "SqlServerDevLens.McpHost.dll");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        async Task<McpClient> ConnectAdapter() => await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = "dotnet", Arguments = [dll, "--desktop", "--pipe-name", name] }), cancellationToken: timeout.Token);
        await using var discovery = await ConnectAdapter();
        await using var chat = await ConnectAdapter();
        await Task.WhenAll(AssertStatus(discovery, timeout.Token), AssertStatus(chat, timeout.Token));
        await server.StopAsync();
        await Assert.ThrowsAnyAsync<Exception>(async () => await discovery.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
        await Assert.ThrowsAnyAsync<Exception>(async () => await chat.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
    }
}
