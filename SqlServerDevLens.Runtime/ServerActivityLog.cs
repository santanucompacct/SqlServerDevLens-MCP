namespace SqlServerDevLens.Runtime;

public sealed record ServerActivity(DateTimeOffset Time, string Operation, string State, string Detail)
{
    public string Timestamp => Time.ToLocalTime().ToString("HH:mm:ss");
}

// In-memory operational metadata only. Never pass arguments, results or exceptions here.
public sealed class ServerActivityLog
{
    public const int Capacity = 200;
    private readonly object _gate = new();
    private readonly List<ServerActivity> _entries = [];
    public event Action? Changed;
    public ServerActivity[] Snapshot() { lock (_gate) return _entries.ToArray(); }
    public void Add(string operation, string state, string detail = "")
    {
        lock (_gate)
        {
            _entries.Insert(0, new(DateTimeOffset.UtcNow, operation, state, detail));
            if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
        // A display failure must never change an MCP action's outcome.
        foreach (var listener in Changed?.GetInvocationList() ?? [])
            try { ((Action)listener)(); } catch { }
    }
}
