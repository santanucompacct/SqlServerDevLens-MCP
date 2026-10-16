using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Server;

namespace SqlServerDevLens.Runtime;

public interface IDesktopMcpServer : IAsyncDisposable
{
    bool IsRunning { get; }
    event Action? StoppedUnexpectedly;
    Task StartAsync(DateTimeOffset sqlTestedAt);
    Task StopAsync();
}

// No TCP listener, credential file, SQL password, or persistent SQL connection.
public sealed class DesktopMcpServer(string? pipeName = null) : IDesktopMcpServer
{
    public static string DefaultPipeName => "SqlServerDevLens-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..24];
    private readonly string _pipeName = pipeName ?? DefaultPipeName;
    private NamedPipeServerStream? _pipe;
    private CancellationTokenSource? _shutdown;
    private Task? _loop;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    public bool IsRunning { get; private set; }
    public event Action? StoppedUnexpectedly;

    public async Task StartAsync(DateTimeOffset sqlTestedAt)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_pipe is not null) throw new InvalidOperationException("Server is already started.");
            // A single persistent instance prevents another desktop from claiming this endpoint.
            _pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _shutdown = new CancellationTokenSource();
            IsRunning = true;
            _loop = ServeAsync(_pipe, sqlTestedAt, _shutdown.Token);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, DateTimeOffset sqlTestedAt, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                try
                {
                    // Keep the listener alive when an MCP client disconnects; accept the next session.
                    using var stream = new LeaveOpenStream(pipe);
                    await using var server = McpServer.Create(new StreamServerTransport(stream, stream), new McpServerOptions
                    {
                        ToolCollection = [McpServerTool.Create(() => new
                        {
                            service = "SQL Server DevLens MCP", status = "running", transport = "desktop-pipe",
                            sqlConfigured = false, sqlConnected = false, lastSqlTestSucceeded = true, lastSqlTestUtc = sqlTestedAt,
                            postmanImplemented = false,
                            message = "SQL connection was tested at the recorded time and disposed. Status tool only; no live SQL session or inspection tools."
                        }, new() { Name = "get_server_status", Description = "Returns the desktop-controlled MCP server status and last SQL test time; does not query SQL." })]
                    });
                    await server.RunAsync(cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is IOException or System.Text.Json.JsonException or ModelContextProtocol.McpException)
                { /* A broken/malformed client session must not kill the listener. */ }
                finally { if (pipe.IsConnected) pipe.Disconnect(); }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            IsRunning = false;
            StoppedUnexpectedly?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_shutdown is null) return;
            _shutdown.Cancel();
            _pipe?.Dispose();
            if (_loop is not null) await _loop;
            _shutdown.Dispose(); _shutdown = null; _pipe = null; _loop = null;
            IsRunning = false;
        }
        finally { _lifecycle.Release(); }
    }
    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class LeaveOpenStream(Stream stream) : Stream
    {
        public override bool CanRead => stream.CanRead;
        public override bool CanWrite => stream.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => stream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => stream.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => stream.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
