using System.IO.Pipes;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SqlServerDevLens.Runtime;

// The client-facing stdio session lives independently of the desktop. Every tool
// request opens a fresh desktop session; no stale status or SQL credentials are cached.
public sealed class DesktopStatusClient(string? pipeName = null, TimeSpan? requestTimeout = null)
{
    public async Task<CallToolResult> GetStatusAsync(CancellationToken cancellationToken = default)
        => await CallAsync("get_server_status", null, cancellationToken);

    public async Task<CallToolResult> CallAsync(string name, IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(name == "get_server_status" ? 5 : 35));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName ?? DesktopMcpServer.DefaultPipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            handshake.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(5));
            await pipe.ConnectAsync(handshake.Token).ConfigureAwait(false);
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe),
                cancellationToken: handshake.Token).ConfigureAwait(false);
            return await client.CallToolAsync(name, arguments, cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is
            IOException or TimeoutException or OperationCanceledException or McpException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new CallToolResult
            {
                IsError = name != "get_server_status",
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new
                {
                    service = "SQL Server DevLens MCP", status = "desktop_unavailable", adapterStatus = "ready",
                    transport = "stdio", sqlConfigured = false, sqlConnected = false,
                    lastSqlTestSucceeded = (bool?)null, lastSqlTestUtc = (DateTimeOffset?)null,
                    postmanImplemented = false,
                    outcomeUnknown = name is "execute_prepared_procedure" or "apply_procedure_change",
                    message = name is "execute_prepared_procedure" or "apply_procedure_change"
                        ? "Connection lost or operation timed out. The approved action may already have committed. Verify database state; do not retry automatically. The request may have been consumed."
                        : "DevLens is stopped, restarting, busy or unreachable. Start DevLens and test SQL until Running, then call this tool again. The plugin remains connected and reconnects to DevLens on each call."
                }) }]
            };
        }
    }
}
