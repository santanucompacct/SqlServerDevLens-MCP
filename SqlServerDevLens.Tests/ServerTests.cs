using System.IO.Pipelines;
using System.IO.Pipes;
using PipeOptions = System.IO.Pipes.PipeOptions;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Core;
using SqlServerDevLens.Desktop.ViewModels;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class ServerTests
{
    [Fact]
    public async Task RealMcpSessionReconnectStopAndRestart()
    {
        var name = "devlens-test-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var testedAt = DateTimeOffset.UtcNow;
        await server.StartAsync(testedAt);
        Assert.True(server.IsRunning);
        await CheckStatus(name, testedAt, timeout.Token);
        await CheckStatus(name, testedAt, timeout.Token);
        await server.StopAsync();
        Assert.False(server.IsRunning);
        await using (var absent = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            await Assert.ThrowsAsync<TimeoutException>(() => absent.ConnectAsync(150));
        await server.StartAsync(testedAt);
        await CheckStatus(name, testedAt, timeout.Token);
    }

    private static async Task CheckStatus(string name, DateTimeOffset testedAt, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(cancellationToken);
        await using var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe), cancellationToken: cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        Assert.Single(tools); Assert.Equal("get_server_status", tools[0].Name);
        var result = await client.CallToolAsync("get_server_status", cancellationToken: cancellationToken);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        var status = JsonDocument.Parse(text).RootElement;
        Assert.Equal("running", status.GetProperty("status").GetString());
        Assert.False(status.GetProperty("sqlConfigured").GetBoolean());
        Assert.True(status.GetProperty("lastSqlTestSucceeded").GetBoolean());
        Assert.False(status.GetProperty("sqlConnected").GetBoolean());
        Assert.Equal(testedAt, status.GetProperty("lastSqlTestUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task StdioBridgeForwardsRealProtocolAndEndsWhenStopped()
    {
        var name = "devlens-bridge-test-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        await server.StartAsync(DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var incoming = new Pipe(); var outgoing = new Pipe();
        var bridge = DesktopBridge.RunAsync(incoming.Reader.AsStream(), outgoing.Writer.AsStream(), name, timeout.Token);
        await using var client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: timeout.Token);
        var result = await client.CallToolAsync("get_server_status", cancellationToken: timeout.Token);
        Assert.NotEmpty(result.Content);
        await server.StopAsync();
        await bridge.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task CompiledAdapterProcessConnectsAndCallsFailOnStop()
    {
        var name = "devlens-process-test-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        await server.StartAsync(DateTimeOffset.UtcNow);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SqlServerDevLens.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var dll = Path.Combine(root.FullName, "SqlServerDevLens.McpHost", "bin", "Release", "net10.0", "SqlServerDevLens.McpHost.dll");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = "dotnet", Arguments = [dll, "--desktop", "--pipe-name", name]
        }), cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Single(tools);
        var result = await client.CallToolAsync("get_server_status", cancellationToken: timeout.Token);
        Assert.NotEmpty(result.Content);
        await server.StopAsync();
        await Assert.ThrowsAnyAsync<Exception>(async () => await client.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
    }

    [Fact]
    public async Task DuplicateListenerIsRejectedAndStopIsIdempotent()
    {
        var name = "devlens-exclusive-test-" + Guid.NewGuid().ToString("N");
        await using var first = new DesktopMcpServer(name);
        await using var second = new DesktopMcpServer(name);
        await first.StartAsync(DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<IOException>(() => second.StartAsync(DateTimeOffset.UtcNow));
        await Task.WhenAll(first.StopAsync(), first.StopAsync());
        Assert.False(first.IsRunning);
    }

    [Fact]
    public async Task SuccessfulTestNavigatesAndStopAllowsReturning()
    {
        var runtime = new FakeServer();
        var connection = new ConnectionViewModel(new Tester(true), new Profiles(), new UnavailableCredentialStore())
        { Host = "localhost", Database = "Example", Username = "fake", Password = "fake" };
        var model = new MainViewModel(connection, new ServerViewModel(runtime));
        await model.ContinueCommand.ExecuteAsync(null);
        await connection.TestConnectionCommand.ExecuteAsync(null);
        Assert.True(model.IsServerStepVisible); Assert.False(model.IsConnectionStepVisible);
        Assert.True(model.Server.IsRunning); Assert.Empty(connection.Password);
        await model.ReturnToConnectionCommand.ExecuteAsync(null);
        Assert.True(model.IsServerStepVisible);
        await model.Server.StopCommand.ExecuteAsync(null);
        Assert.False(model.Server.IsRunning);
        await model.ReturnToConnectionCommand.ExecuteAsync(null);
        Assert.True(model.IsConnectionStepVisible); Assert.False(model.IsServerStepVisible);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task FailureDoesNotClaimRunning(bool sqlSuccess, bool failStartup)
    {
        var runtime = new FakeServer { FailStartup = failStartup };
        var connection = new ConnectionViewModel(new Tester(sqlSuccess), new Profiles(), new UnavailableCredentialStore())
        { Host = "localhost", Database = "Example", Username = "fake", Password = "fake" };
        var model = new MainViewModel(connection, new ServerViewModel(runtime));
        await model.ContinueCommand.ExecuteAsync(null);
        await connection.TestConnectionCommand.ExecuteAsync(null);
        Assert.False(model.Server.IsRunning);
        Assert.Equal(sqlSuccess, model.IsServerStepVisible);
        if (failStartup) Assert.Contains("Could not start", model.Server.Status);
    }

    private sealed class Tester(bool success) : IConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(ConnectionProfile profile, string? password, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectionTestResult(success, success ? "Connected." : "Failed."));
    }
    private sealed class Profiles : IProfileStore
    {
        public Task<ConnectionProfile?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<ConnectionProfile?>(null);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FakeServer : IDesktopMcpServer
    {
        public bool FailStartup;
        public bool IsRunning { get; private set; }
        public event Action? StoppedUnexpectedly { add { } remove { } }
        public Task StartAsync(DateTimeOffset sqlTestedAt)
        { if (FailStartup) throw new IOException(); IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
