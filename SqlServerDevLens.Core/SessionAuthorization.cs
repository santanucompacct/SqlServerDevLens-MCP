namespace SqlServerDevLens.Core;

// Mutable only through the local desktop UI. No MCP setter or persisted blanket permission.
public sealed class SessionAuthorization
{
    public static readonly string[] Categories = ["procedure_execute", "procedure_create", "procedure_alter", "table_create", "table_alter",
        "table_insert", "table_update", "table_delete", "postman_run", "postman_change"];
    private readonly object _gate = new();
    private readonly Dictionary<string, (bool Enabled, long Revision)> _settings = Categories.ToDictionary(x => x, _ => (false, 0L));
    public event Action<string, bool>? Changed;
    public event Action<string>? Used;
    public void Set(string category, bool enabled)
    {
        lock (_gate)
        {
            if (!_settings.TryGetValue(category, out var current)) throw new ArgumentException("Unknown authorization category.");
            if (current.Enabled == enabled) return;
            _settings[category] = (enabled, current.Revision + 1);
        }
        Changed?.Invoke(category, enabled);
    }
    public Dictionary<string, bool> Snapshot() { lock (_gate) return _settings.ToDictionary(x => x.Key, x => x.Value.Enabled); }
    public long? Grant(string category) { lock (_gate) return _settings.TryGetValue(category, out var s) && s.Enabled ? s.Revision : null; }
    public void Check(string category, long revision)
    {
        if (Grant(category) != revision) throw new InvalidOperationException("Session authorization was disabled or changed. Prepare again for manual approval. Nothing was started.");
    }
    internal void RecordUse(string category) => Used?.Invoke(category);
    public void Reset() { foreach (var category in Categories) Set(category, false); }
    public static string Category(ProcedureReview review) => review.TargetKind switch
    {
        "procedure" => "procedure_" + review.Action,
        "table" => review.Action,
        "postman" => review.Action == "run" ? "postman_run" : "postman_change",
        _ => "unknown"
    };
}
