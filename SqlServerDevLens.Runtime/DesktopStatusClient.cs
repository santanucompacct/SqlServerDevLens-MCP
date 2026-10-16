using System.IO.Pipes;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SqlServerDevLens.Runtime;

// The client-facing stdio session lives independently of the desktop. Every status
// request opens a fresh desktop session; no stale status or SQL credentials are cached.
public sealed class DesktopStatusClient(string? pipeName = null, TimeSpan? requestTimeout = null)
{
    public async Task<CallToolResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(3));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName ?? DesktopMcpServer.DefaultPipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe),
                cancellationToken: timeout.Token).ConfigureAwait(false);
            return await client.CallToolAsync("get_server_status", cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is
            IOException or TimeoutException or OperationCanceledException or McpException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new
                {
                    service = "SQL Server DevLens MCP", status = "desktop_unavailable", adapterStatus = "ready",
                    transport = "stdio", sqlConfigured = false, sqlConnected = false,
                    lastSqlTestSucceeded = (bool?)null, lastSqlTestUtc = (DateTimeOffset?)null,
                    postmanImplemented = false,
                    message = "DevLens is stopped, restarting, busy or unreachable. Start DevLens and test SQL until Running, then call this tool again. The plugin remains connected and reconnects to DevLens on each call."
                }) }]
            };
        }
    }
}
