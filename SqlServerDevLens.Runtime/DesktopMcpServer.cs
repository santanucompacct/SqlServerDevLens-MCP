using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Server;
using SqlServerDevLens.Core;

namespace SqlServerDevLens.Runtime;

public interface IDesktopMcpServer : IAsyncDisposable
{
    bool IsRunning { get; }
    event Action? StoppedUnexpectedly;
    Task StartAsync(DateTimeOffset sqlTestedAt, ISqlDatabase? database = null);
    Task StopAsync();
}

// No TCP listener or credential file. Only the desktop holds the active SQL
// context, releasing it on Stop; SQL connections are per operation.
public sealed class DesktopMcpServer(string? pipeName = null) : IDesktopMcpServer
{
    public const int MaxConcurrentSessions = 16;
    // Short names fit macOS Unix-domain socket limits in its per-user temp directory.
    public static string DefaultPipeName => "dvl-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..24];
    private readonly string _pipeName = pipeName ?? DefaultPipeName;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _slots = new(MaxConcurrentSessions, MaxConcurrentSessions);
    private readonly object _sessionGate = new();
    private readonly HashSet<Session> _sessions = [];
    private NamedPipeServerStream? _listener;
    private CancellationTokenSource? _shutdown;
    private Task? _acceptLoop;
    private ISqlDatabase? _database;
    private volatile bool _isRunning;
    public bool IsRunning => _isRunning;
    public event Action? StoppedUnexpectedly;

    private NamedPipeServerStream CreateListener(bool firstInstance = false) => new(
        _pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly |
        (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    public async Task StartAsync(DateTimeOffset sqlTestedAt, ISqlDatabase? database = null)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_shutdown is not null) throw new InvalidOperationException("Server is already started.");
            // Reserve the endpoint exclusively, allowing multiple instances owned by this server.
            _listener = CreateListener(firstInstance: true);
            _database = database;
            _shutdown = new CancellationTokenSource();
            _isRunning = true;
            _acceptLoop = AcceptAsync(_listener, sqlTestedAt, _shutdown.Token);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task AcceptAsync(NamedPipeServerStream listener, DateTimeOffset sqlTestedAt, CancellationToken cancellationToken)
    {
        var ownsSlot = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                ownsSlot = true;
                await listener.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var accepted = listener;
                // Keep an accepting pipe alive before handing off this connection.
                var next = CreateListener();
                lock (_sessionGate)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        next.Dispose();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    _listener = next;
                    listener = next;
                    var session = new Session(accepted);
                    _sessions.Add(session);
                    session.Completion = RunSessionAsync(session, sqlTestedAt, cancellationToken);
                    ownsSlot = false;
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception)
        {
            _isRunning = false;
            _shutdown!.Cancel();
            _database?.Dispose(); _database = null;
            Session[] active;
            lock (_sessionGate) active = _sessions.ToArray();
            foreach (var session in active) session.Pipe.Dispose();
            StoppedUnexpectedly?.Invoke();
        }
        finally
        {
            listener.Dispose();
            if (ownsSlot) _slots.Release();
        }
    }

    private async Task RunSessionAsync(Session session, DateTimeOffset sqlTestedAt, CancellationToken cancellationToken)
    {
        try
        {
            await using var server = McpServer.Create(new StreamServerTransport(session.Pipe, session.Pipe), new McpServerOptions
            {
                ToolCollection = [McpServerTool.Create(() => new
                {
                    service = "SQL Server DevLens MCP", status = "running", transport = "desktop-pipe",
                    sqlConfigured = _database is not null, sqlConnected = false, database = _database?.Database, lastSqlTestSucceeded = true, lastSqlTestUtc = sqlTestedAt,
                    postmanImplemented = false,
                    message = "SQL connections open per operation and are disposed. Read-only tools use the selected database; no persistent SQL session. Postman is not implemented."
                }, new() { Name = "get_server_status", Description = "Returns the desktop-controlled MCP server status and last SQL test time; does not query SQL." }), ..new DatabaseTools(_database).CreateTools()]
            });
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        { /* A disconnected or malformed client is isolated to its own session. */ }
        finally
        {
            session.Pipe.Dispose();
            _slots.Release();
            lock (_sessionGate) _sessions.Remove(session);
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_shutdown is null) return;
            _isRunning = false;
            _shutdown.Cancel();
            NamedPipeServerStream? listener;
            Session[] active;
            lock (_sessionGate)
            {
                listener = _listener;
                active = _sessions.ToArray();
            }
            listener?.Dispose();
            foreach (var session in active) session.Pipe.Dispose();
            if (_acceptLoop is not null) await _acceptLoop.ConfigureAwait(false);
            Task[] pending;
            lock (_sessionGate) pending = _sessions.Select(session => session.Completion).ToArray();
            await Task.WhenAll(pending).ConfigureAwait(false);
            _database?.Dispose(); _database = null;
            _shutdown.Dispose(); _shutdown = null; _listener = null; _acceptLoop = null;
        }
        finally { _lifecycle.Release(); }
    }
    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class Session(NamedPipeServerStream pipe)
    {
        public NamedPipeServerStream Pipe { get; } = pipe;
        public Task Completion { get; set; } = Task.CompletedTask;
    }
}
