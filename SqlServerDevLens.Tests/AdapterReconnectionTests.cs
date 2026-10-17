using System.IO.Pipes;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class AdapterReconnectionTests
{
    private static JsonElement Status(CallToolResult result)
    {
        Assert.False(result.IsError ?? false);
        using var document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task SameStdioSessionDiscoversBeforeDesktopStartsAndRecoversAcrossRestarts()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SqlServerDevLens.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var dll = Path.Combine(root.FullName, "SqlServerDevLens.McpHost", "bin", "Release", "net10.0", "SqlServerDevLens.McpHost.dll");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await using var chat = await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = "dotnet", Arguments = [dll, "--desktop", "--pipe-name", name] }), cancellationToken: timeout.Token);
        var tools = await chat.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(14, tools.Count); Assert.Contains(tools, t => t.Name == "get_server_status");
        var offline = Status(await chat.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
        Assert.Equal("desktop_unavailable", offline.GetProperty("status").GetString());
        Assert.Equal("ready", offline.GetProperty("adapterStatus").GetString());
        Assert.Equal(JsonValueKind.Null, offline.GetProperty("lastSqlTestSucceeded").ValueKind);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var testedAt = DateTimeOffset.UtcNow.AddMinutes(cycle);
            await server.StartAsync(testedAt);
            var running = Status(await chat.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
            Assert.Equal("running", running.GetProperty("status").GetString());
            Assert.Equal(testedAt, running.GetProperty("lastSqlTestUtc").GetDateTimeOffset());
            await server.StopAsync();
            offline = Status(await chat.CallToolAsync("get_server_status", cancellationToken: timeout.Token));
            Assert.Equal("desktop_unavailable", offline.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, offline.GetProperty("lastSqlTestUtc").ValueKind);
            Assert.Equal(14, (await chat.ListToolsAsync(cancellationToken: timeout.Token)).Count);
        }
    }

    [Fact]
    public async Task CancelledCallerIsNotReportedAsAnUnavailableDesktop()
    {
        var client = new DesktopStatusClient("dvt-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStatusAsync(cancellation.Token));
    }

    [Fact]
    public async Task StalledDesktopHandshakeIsBoundedAndNextCallCanRecover()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        var client = new DesktopStatusClient(name, TimeSpan.FromMilliseconds(500));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using (var stalled = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            var accepting = stalled.WaitForConnectionAsync(timeout.Token);
            var request = client.GetStatusAsync(timeout.Token);
            await accepting;
            var unavailable = Status(await request.WaitAsync(timeout.Token));
            Assert.Equal("desktop_unavailable", unavailable.GetProperty("status").GetString());
        }
        await using var server = new DesktopMcpServer(name);
        await server.StartAsync(DateTimeOffset.UtcNow);
        Assert.Equal("running", Status(await client.GetStatusAsync(timeout.Token)).GetProperty("status").GetString());
    }
}
