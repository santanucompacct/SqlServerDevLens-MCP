using System.IO.Pipes;

namespace SqlServerDevLens.Runtime;

public static class DesktopBridge
{
    // Pure protocol forwarding: only the desktop runs tools; Stop disconnects this adapter.
    public static async Task RunAsync(Stream input, Stream output, string? pipeName = null, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName ?? DesktopMcpServer.DefaultPipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, cancellationToken);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var incoming = pipe.CopyToAsync(output, shutdown.Token);
        var outgoing = input.CopyToAsync(pipe, shutdown.Token);
        try { await await Task.WhenAny(incoming, outgoing); }
        finally
        {
            shutdown.Cancel();
            await pipe.DisposeAsync();
            // stdin reads may not cancel on every OS; the owning adapter process exits on return.
            _ = outgoing.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            _ = incoming.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
