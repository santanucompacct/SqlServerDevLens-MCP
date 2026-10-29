using System.Security.Cryptography;

namespace SqlServerDevLens.Core;

// Only masked display strings cross the review/MCP boundary. Payloads stay in the desktop.
public sealed class PostmanPlan(string action, string name, string revision, string before, string after, string scope)
{
    public string Action { get; } = action;
    public string Name { get; } = name;
    public string Revision { get; } = revision;
    public string Before { get; } = before;
    public string After { get; } = after;
    public string Scope { get; } = scope;
    internal string TargetId { get; init; } = "";
    internal string FolderId { get; init; } = "";
    internal string Payload { get; init; } = "";
    internal string Backup { get; init; } = "";
    internal ResolvedPostmanRequest? Request { get; init; }
    internal string[] Secrets { get; init; } = [];
    public override string ToString() => $"Postman {Action}: {Name}";
}
internal sealed record ResolvedPostmanRequest(string Method, string Url, Dictionary<string, string> Headers, string? Body);
public sealed record PostmanActionResult(string Status, string? ResourceId = null, int? HttpStatus = null,
    string? Body = null, bool Truncated = false, string Message = "", CapturedResultReference? Capture = null, bool ReadCapturedResult = false);
public sealed class PostmanReviewChangedException() : InvalidOperationException("Postman collection, variables or authentication changed after review. Nothing was sent. Prepare and approve again.");
    
// Safe, phase-specific diagnostics. Does not expose provider response bodies, SQL, keys or payloads.
public sealed class PostmanWriteStageException(string stage, string detail, bool submissionStarted) : InvalidOperationException(
    $"Postman collection change failed during {stage}: {detail}. " +
    (submissionStarted
        ? "The write may have been saved; inspect the collection before taking any further action. "
        : "No Postman write request was sent. ") +
    "Approval was consumed; never retry automatically.")
{
    public string Stage { get; } = stage;
    public bool SubmissionStarted { get; } = submissionStarted;
}
public sealed record PostmanItem(string Id, string Path, string Kind, string? Method);
public interface IPostmanActionBackend : IDisposable
{
    string CollectionName { get; }
    Task<PostmanItem[]> ListAsync(CancellationToken token);
    Task<string> ReadAsync(string requestId, CancellationToken token);
    Task<PostmanPlan> PrepareChangeAsync(string action, string name, string requestId, string templateRequestId,
        string folderId, string? headersJson, string? bodyJson, CancellationToken token);
    Task<PostmanPlan> PrepareRunAsync(string requestId, CancellationToken token);
    Task<PostmanActionResult> ApplyAsync(PostmanPlan plan, CancellationToken token);
}

public sealed class PostmanWorkflow(IPostmanActionBackend backend,
    Action<ProcedureReview, Action<bool>, CancellationToken>? review, TimeProvider? clock = null,
    SessionAuthorization? authorization = null, CapturedResults? results = null) : IDisposable
{
    public CapturedResults Results { get; } = results ?? new(clock);
    private readonly Dictionary<string, long> _automatic = [];
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, (PostmanPlan Plan, ProcedureReview Review, string Status)> _pending = [];
    public Task<PostmanItem[]> ListAsync(CancellationToken token) => ReadOperation(ct => backend.ListAsync(ct), token);
    public Task<string> ReadAsync(string id, CancellationToken token) => ReadOperation(ct => backend.ReadAsync(id, ct), token);
    private async Task<T> ReadOperation<T>(Func<CancellationToken,Task<T>> operation, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(25));
        return await operation(linked.Token);
    }
    public async Task<ProcedureRequestStatus> PrepareChangeAsync(string action, string name, string requestId, string templateRequestId,
        string folderId, string? headersJson, string? bodyJson, CancellationToken token) =>
        Stage(await ReadOperation(ct => backend.PrepareChangeAsync(action, name, requestId, templateRequestId, folderId, headersJson, bodyJson, ct), token));
    public async Task<ProcedureRequestStatus> PrepareRunAsync(string requestId, CancellationToken token) =>
        Stage(await ReadOperation(ct => backend.PrepareRunAsync(requestId, ct), token));
    private ProcedureRequestStatus Stage(PostmanPlan plan)
    {
        _stop.Token.ThrowIfCancellationRequested();
        if (review is null && authorization is null) throw new InvalidOperationException("Desktop Postman approval is unavailable. Nothing was executed.");
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); var expires = _clock.GetUtcNow().AddMinutes(5);
        var diff = plan.Scope + "\n\n" + (plan.Action == "run" ? plan.After : ProcedureSql.Diff(plan.Before, plan.After));
        var display = new ProcedureReview(id, backend.CollectionName, "Postman", plan.Name, plan.Action,
            plan.Before, plan.After, diff, "", expires, "postman");
        lock (_gate)
        {
            _stop.Token.ThrowIfCancellationRequested();
            foreach (var old in _pending.Where(x => x.Value.Review.ExpiresAt <= _clock.GetUtcNow()).Select(x=>x.Key).ToArray()) { _pending.Remove(old); _automatic.Remove(old); }
            if (_pending.Count >= 5) throw new InvalidOperationException("Too many Postman approvals. Complete them or wait for expiry.");
            _pending.Add(id, (plan, display, "pending"));
        }
        var category = SessionAuthorization.Category(display);
        if (authorization?.Grant(category) is long grant)
        {
            lock (_gate) { _automatic[id] = grant; _pending[id] = (plan, display, "approved"); }
            authorization.RecordUse(category);
            return Status(id);
        }
        try
        {
            if (review is null) throw new InvalidOperationException("Manual desktop review is unavailable.");
            review(display, approved => { lock (_gate)
                if (_pending.TryGetValue(id, out var current) && current.Status == "pending")
                    _pending[id] = (current.Plan, current.Review, approved && !_stop.IsCancellationRequested && _clock.GetUtcNow() < expires ? "approved" : "denied");
            }, _stop.Token);
        }
        catch { lock (_gate) _pending.Remove(id); throw new InvalidOperationException("Postman review could not be opened. Nothing was executed."); }
        return Status(id);
    }
    private (PostmanPlan Plan, ProcedureReview Review, string Status) Find(string id)
    {
        if (_stop.IsCancellationRequested || !_pending.TryGetValue(id, out var value)) throw new ArgumentException("Postman request is unknown, consumed or cleared by Stop.");
        if (_clock.GetUtcNow() >= value.Review.ExpiresAt) { _pending.Remove(id); _automatic.Remove(id); throw new ArgumentException("Postman request expired. Prepare again."); }
        return value;
    }
    public ProcedureRequestStatus Status(string id)
    { lock (_gate) { var value = Find(id); return new(id, value.Status, value.Review.ExpiresAt, value.Review.Diff, _automatic.ContainsKey(id) ? "session_policy" : "manual"); } }
    public async Task<PostmanActionResult> ApplyAsync(string id, bool run, CancellationToken token)
    {
        lock (_gate) _ = Find(id);
        using var capture = Results.Reserve();
        PostmanPlan plan;
        lock (_gate)
        {
            var value = Find(id);
            if ((value.Plan.Action == "run") != run) throw new ArgumentException("Postman approval belongs to a different action.");
            if (value.Status != "approved") throw new InvalidOperationException("Explicit approval in DevLens is required. Nothing was executed.");
            if (_automatic.TryGetValue(id, out var grant)) authorization!.Check(SessionAuthorization.Category(value.Review), grant);
            _automatic.Remove(id);
            plan = value.Plan; _pending.Remove(id); // Consume before any external request. Never replay.
        }
        try
        {
            var result = await ReadOperation(ct => backend.ApplyAsync(plan, ct), token);
            if (!run) return result;
            var reference = capture.Save(result);
            return reference.TotalCharacters <= CapturedResults.InlineCharacters ? result with { Capture = reference }
                : result with { Body = null, Capture = reference, ReadCapturedResult = true,
                    Message = result.Message + " Full masked HTTP result captured. Read read_result_page; do not run HTTP again to fetch pages." };
        }
        catch (PostmanReviewChangedException) { throw; }
        catch (PostmanWriteStageException) { throw; }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or IOException)
        { throw new InvalidOperationException("Postman action failed or timed out. It may already have been saved or executed. Verify state; never retry automatically. Approval was consumed."); }
    }
    public void Dispose() { _stop.Cancel(); lock (_gate) { _pending.Clear(); _automatic.Clear(); } if (results is null) Results.Dispose(); backend.Dispose(); }
}
