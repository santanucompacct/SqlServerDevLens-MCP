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
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var testedAt = DateTimeOffset.UtcNow;
        await server.StartAsync(testedAt);
        Assert.True(server.IsRunning);
        await CheckStatus(name, testedAt, timeout.Token);
        await CheckStatus(name, testedAt, timeout.Token);
        var activity = server.Activity.Snapshot();
        Assert.Equal(2, activity.Count(e => e.State == "Serving"));
        Assert.Equal(2, activity.Count(e => e.State == "Completed"));
        Assert.Contains("get_server_status", activity[0].Operation);
        Assert.Equal("Completed", activity[0].State);
        var rejected = await new DesktopStatusClient(name).CallAsync("query_read_only",
            new Dictionary<string, object?> { ["sql"] = "SELECT 'PRIVATE-SENTINEL'", ["maxRows"] = 1 }, timeout.Token);
        Assert.True(rejected.IsError);
        activity = server.Activity.Snapshot();
        Assert.Equal("Failed", activity[0].State);
        Assert.DoesNotContain("PRIVATE-SENTINEL", string.Join("\n", activity.Select(e => e.ToString())));
        await server.StopAsync();
        Assert.False(server.IsRunning);
        Assert.Equal("Stopped", server.Activity.Snapshot()[0].State);
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
        Assert.Equal(25, tools.Count); Assert.Contains(tools, t => t.Name == "get_server_status");
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
    public async Task DesktopStatusClientRecoversWithoutCachingThePreviousResult()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        // Use the production request budget; initial SDK/JIT startup on hosted
        // Windows runners can exceed the shorter timeout used by timeout-only tests.
        var statusClient = new DesktopStatusClient(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var offline = await statusClient.GetStatusAsync(timeout.Token);
        Assert.Contains("desktop_unavailable", Assert.IsType<TextContentBlock>(offline.Content[0]).Text);
        await server.StartAsync(DateTimeOffset.UtcNow);
        var running = await statusClient.GetStatusAsync(timeout.Token);
        Assert.Equal("running", JsonDocument.Parse(Assert.IsType<TextContentBlock>(running.Content[0]).Text).RootElement.GetProperty("status").GetString());
        await server.StopAsync();
        var stopped = await statusClient.GetStatusAsync(timeout.Token);
        Assert.Contains("desktop_unavailable", Assert.IsType<TextContentBlock>(stopped.Content[0]).Text);
    }
    [Fact]
    public async Task CompiledAdapterProcessStaysAvailableWhenDesktopStops()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
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
        Assert.Equal(25, tools.Count);
        var result = await client.CallToolAsync("get_server_status", cancellationToken: timeout.Token);
        Assert.NotEmpty(result.Content);
        await server.StopAsync();
        var stopped = await client.CallToolAsync("get_server_status", cancellationToken: timeout.Token);
        Assert.Contains("desktop_unavailable", Assert.IsType<TextContentBlock>(stopped.Content[0]).Text);
    }

    [Fact]
    public async Task DuplicateListenerIsRejectedAndStopIsIdempotent()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var first = new DesktopMcpServer(name);
        await using var second = new DesktopMcpServer(name);
        await first.StartAsync(DateTimeOffset.UtcNow);
        var error = await Record.ExceptionAsync(() => second.StartAsync(DateTimeOffset.UtcNow));
        Assert.True(error is IOException or UnauthorizedAccessException, $"Expected duplicate endpoint rejection, got {error?.GetType().Name ?? "no error"}.");
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
        connection.Password = "fake";
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
        connection.Password = "fake";
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
        public ServerActivityLog Activity { get; } = new();
        public bool IsRunning { get; private set; }
        public event Action? StoppedUnexpectedly { add { } remove { } }
        public Task StartAsync(DateTimeOffset sqlTestedAt, SqlServerDevLens.Core.ISqlDatabase? database = null, SqlServerDevLens.Core.PostmanSelection? postman = null, SqlServerDevLens.Core.PostmanWorkflow? postmanActions = null)
        { if (FailStartup) throw new IOException(); IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
