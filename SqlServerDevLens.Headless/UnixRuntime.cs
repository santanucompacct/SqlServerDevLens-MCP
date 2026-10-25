using System.Net.Sockets;
using ModelContextProtocol.Server;

namespace SqlServerDevLens.Headless;

public static class UnixRuntime
{
    public static void ValidateSocket(string path)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(path) || path.Length > 100)
            throw new ArgumentException("An absolute Linux Unix socket path of at most 100 characters is required.");
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent) || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetUnixFileMode(parent) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new ArgumentException("The runtime socket directory must be private (0700).");
    }
    public static async Task BridgeAsync(string path, CancellationToken token)
    {
        ValidateSocket(path);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
        connect.CancelAfter(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), connect.Token);
        using var stream = new NetworkStream(socket, false);
        using var end = CancellationTokenSource.CreateLinkedTokenSource(token);
        var input = Console.OpenStandardInput().CopyToAsync(stream, end.Token);
        var output = stream.CopyToAsync(Console.OpenStandardOutput(), end.Token);
        await Task.WhenAny(input, output);
        end.Cancel(); socket.Dispose();
        // Console stdin may use an uncancellable blocking read. Do not keep this bridge
        // process alive after its socket closes; observe any eventual copy exception.
        _ = Task.WhenAll(input, output).ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    public static async Task ServeAsync(string path, McpServerTool[] tools, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Private runtime sockets require Linux.");
        ValidateSocket(path);
        // Refuse existing paths rather than unlinking an unrelated file/listener.
        if (File.Exists(path)) throw new ArgumentException("Runtime socket already exists.");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path)); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        listener.Listen(16);
        using var slots = new SemaphoreSlim(16);
        var sessions = new List<Task>();
        var clients = new System.Collections.Concurrent.ConcurrentDictionary<Socket, byte>();
        async Task Session(Socket client)
        {
            using (client)
            using (var stream = new NetworkStream(client, false))
            {
                try
                {
                    await using var server = McpServer.Create(new StreamServerTransport(stream, stream),
                        new McpServerOptions { ToolCollection = [..tools] });
                    await server.RunAsync(token);
                }
                catch (Exception) { /* Client failure is isolated; never log payload/provider errors. */ }
                finally { clients.TryRemove(client, out _); slots.Release(); }
            }
        }
        try
        {
            while (!token.IsCancellationRequested)
            {
                await slots.WaitAsync(token);
                Socket client;
                try { client = await listener.AcceptAsync(token); }
                catch { slots.Release(); throw; }
                sessions.RemoveAll(t => t.IsCompleted);
                clients.TryAdd(client, 0);
                sessions.Add(Session(client));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            listener.Dispose();
            foreach (var client in clients.Keys) client.Dispose();
            await Task.WhenAll(sessions);
            File.Delete(path);
        }
    }
}
