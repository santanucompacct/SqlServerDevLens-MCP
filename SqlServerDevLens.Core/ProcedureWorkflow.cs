using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlServerDevLens.Core;

public sealed record ProcedureParameterInfo(string Name, string Type, int Size, byte Precision, byte Scale, bool Output, bool HasDefault,
    bool Structured = false, bool UserDefined = false);
public sealed record ProcedureDefinition(string Schema, string Name, string Definition, string Revision,
    bool AnsiNulls, bool QuotedIdentifier, IReadOnlyList<ProcedureParameterInfo> Parameters);
public sealed record ProcedureExecution(IReadOnlyList<SqlResult> ResultSets, IReadOnlyDictionary<string, object?> OutputParameters,
    int? ReturnCode, bool Completed, string Message, bool OutputValuesTruncated = false);
public sealed record ProcedureReview(string RequestId, string Database, string Schema, string Name, string Action,
    string Before, string Proposed, string Diff, string Parameters, DateTimeOffset ExpiresAt, string TargetKind = "procedure");
public sealed record ProcedureRequestStatus(string RequestId, string Status, DateTimeOffset ExpiresAt, string Diff);
public sealed record ProcedureApplyResult(string Status, bool BackupSaved);

public interface IProcedureBackend
{
    string Database { get; }
    Task<ProcedureDefinition?> ReadAsync(string schema, string name, CancellationToken token);
    void ValidateParameters(ProcedureDefinition definition, string parametersJson);
    Task<ProcedureExecution> ExecuteAsync(ProcedureDefinition definition, string parametersJson, CancellationToken token);
    Task<ProcedureApplyResult> ApplyAsync(string schema, string name, string sql, ProcedureDefinition? previous, CancellationToken token);
}

// Decisions come only from the desktop callback; no MCP tool can set approved=true.
public sealed class ProcedureWorkflow(IProcedureBackend backend,
    Action<ProcedureReview, Action<bool>, CancellationToken>? review = null, TimeProvider? clock = null) : IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly CancellationTokenSource _stopped = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _pending = new();
    private sealed class Pending(ProcedureReview review, ProcedureDefinition? previous, string sql, string parameters, TableSqlSnapshot? table = null)
    {
        public ProcedureReview Review { get; } = review;
        public ProcedureDefinition? Previous { get; } = previous;
        public string Sql { get; } = sql;
        public string Parameters { get; } = parameters;
        public string Status { get; set; } = "pending";
        public TableSqlSnapshot? Table { get; } = table;
    }
    public async Task<ProcedureDefinition> ReadAsync(string schema, string name, CancellationToken token)
    {
        ProcedureSql.ValidateName(schema, name);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
        return await backend.ReadAsync(schema, name, linked.Token) ?? throw new ArgumentException("Procedure not found or not visible in the selected database.");
    }
    public async Task<TableSchemaPage> ReadTableSchemaAsync(string schema, string name, int offset, int maxCharacters,
        string? expectedRevision, CancellationToken token)
    {
        ProcedureDefinitionPage.Validate(offset, maxCharacters, expectedRevision);
        if (backend is not ITableActionBackend tables) throw new InvalidOperationException("Table schema tools unavailable.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
        return TableSchemaPage.Create(await tables.ReadSchemaAsync(schema, name, linked.Token), offset, maxCharacters, expectedRevision);
    }
    public async Task<ProcedureRequestStatus> PrepareExecutionAsync(string schema, string name, string parametersJson, CancellationToken token)
    {
        var definition = await ReadAsync(schema, name, token);
        backend.ValidateParameters(definition, parametersJson);
        return Stage(schema, name, "execute", definition, "", parametersJson);
    }
    public async Task<ProcedureRequestStatus> PrepareChangeAsync(string schema, string name, string sql, bool create, CancellationToken token)
    {
        ProcedureSql.ValidateChange(schema, name, sql, create);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
        var previous = await backend.ReadAsync(schema, name, linked.Token);
        if (create && previous is not null) throw new ArgumentException("Procedure already exists. Prepare an ALTER instead.");
        if (!create && previous is null) throw new ArgumentException("Procedure not found or not visible. Cannot safely update without the previous definition.");
        return Stage(schema, name, create ? "create" : "alter", previous, sql, "");
    }
    public async Task<ProcedureRequestStatus> PrepareTableAsync(string schema, string name, string sql, int maxRows, CancellationToken token)
    {
        if (backend is not ITableActionBackend tables) throw new InvalidOperationException("Table actions are unavailable in this server.");
        var plan = TableSql.Validate(schema, name, sql, maxRows);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
        var snapshot = await tables.PrepareTableAsync(plan, linked.Token);
        return Stage(schema, name, plan.Action, null, sql, "", snapshot);
    }
    private ProcedureRequestStatus Stage(string schema, string name, string action, ProcedureDefinition? previous, string sql, string parameters, TableSqlSnapshot? table = null)
    {
        _stopped.Token.ThrowIfCancellationRequested();
        if (review is null) throw new InvalidOperationException("Desktop approval is unavailable. No procedure action was prepared or executed.");
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var expires = _clock.GetUtcNow().AddMinutes(5);
        var before = table?.Metadata ?? previous?.Definition ?? "";
        var diff = table is not null
            ? $"Action: {action}\nMatching/insert rows: {table.MatchingRows}. Maximum direct rows: {table.Plan.MaxRows}.\n\nExact SQL:\n{sql}\n\nCurrent schema metadata (not a table-data backup):\n{before}"
            : action == "execute" ? "" : ProcedureSql.Diff(before, sql);
        var request = new ProcedureReview(id, backend.Database, schema, name, action,
            before, sql, diff, parameters, expires, table is null ? "procedure" : "table");
        var pending = new Pending(request, previous, sql, parameters, table);
        lock (_gate)
        {
            _stopped.Token.ThrowIfCancellationRequested();
            foreach (var expired in _pending.Where(p => p.Value.Review.ExpiresAt <= _clock.GetUtcNow()).Select(p => p.Key).ToArray()) _pending.Remove(expired);
            if (_pending.Count >= 5) throw new InvalidOperationException("Too many pending requests. Complete them or wait for expiry.");
            _pending.Add(id, pending);
        }
        try
        {
            review(request, approved =>
            {
                lock (_gate)
                    if (_pending.TryGetValue(id, out var current) && current.Status == "pending")
                        current.Status = approved && !_stopped.IsCancellationRequested && _clock.GetUtcNow() < expires ? "approved" : "denied";
            }, _stopped.Token);
        }
        catch { lock (_gate) _pending.Remove(id); throw new InvalidOperationException("Desktop review could not be opened. Nothing was executed."); }
        return Status(id);
    }
    public ProcedureRequestStatus Status(string requestId)
    {
        lock (_gate)
        {
            var pending = Find(requestId);
            return new(requestId, pending.Status, pending.Review.ExpiresAt, pending.Review.Diff);
        }
    }
    private Pending Find(string id)
    {
        if (_stopped.IsCancellationRequested || !_pending.TryGetValue(id, out var pending)) throw new ArgumentException("Request is unknown, already consumed or cleared by Stop.");
        if (_clock.GetUtcNow() >= pending.Review.ExpiresAt) { _pending.Remove(id); throw new ArgumentException("Request expired. Prepare and review again."); }
        return pending;
    }
    private Pending Consume(string id, string kind)
    {
        lock (_gate)
        {
            var pending = Find(id);
            if (kind switch { "execute" => pending.Review.Action != "execute", "table" => pending.Table is null,
                _ => pending.Table is not null || pending.Review.Action is not ("create" or "alter") })
                throw new ArgumentException("Request belongs to a different action.");
            if (pending.Status != "approved") throw new InvalidOperationException("Explicit approval in DevLens is required. The action has not run.");
            _pending.Remove(id); // Consume before any SQL; a failure must never be replayed.
            return pending;
        }
    }
    public async Task<ProcedureExecution> ExecuteAsync(string id, CancellationToken token)
    {
        var pending = Consume(id, "execute");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
        return await backend.ExecuteAsync(pending.Previous!, pending.Parameters, linked.Token);
    }
    public async Task<ProcedureApplyResult> ApplyAsync(string id, CancellationToken token)
    {
        var pending = Consume(id, "procedure_change");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
        return await backend.ApplyAsync(pending.Review.Schema, pending.Review.Name, pending.Sql, pending.Previous, linked.Token);
    }
    public async Task<TableSqlResult> ApplyTableAsync(string id, CancellationToken token)
    {
        var pending = Consume(id, "table");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stopped.Token);
        return await ((ITableActionBackend)backend).ApplyTableAsync(pending.Table!, linked.Token);
    }
    public void Dispose() { _stopped.Cancel(); lock (_gate) _pending.Clear(); }
}

public static class ProcedureSql
{
    public static void ValidateName(string schema, string name)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name) || schema.Length > 128 || name.Length > 128 ||
            schema.Equals("sys", StringComparison.OrdinalIgnoreCase) || name.StartsWith('#') || schema.Any(char.IsControl) || name.Any(char.IsControl))
            throw new ArgumentException("Supply a non-system procedure schema and name, each 1–128 characters, in the selected database.");
    }
    public static string QuotedName(string schema, string name) => $"[{schema.Replace("]", "]]" )}].[{name.Replace("]", "]]" )}]";
    public static ProcedureStatementBody Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("Procedure SQL must be complete and nonempty.");
        var script = new TSql170Parser(true).Parse(new StringReader(sql), out var errors) as TSqlScript;
        if (errors.Count > 0 || script is null || script.Batches.Count != 1 || script.Batches[0].Statements.Count != 1 ||
            script.Batches[0].Statements[0] is not ProcedureStatementBody body || body.MethodSpecifier is not null)
            throw new ArgumentException("Supply exactly one complete T-SQL CREATE PROCEDURE or ALTER PROCEDURE, without GO or extra statements.");
        return body;
    }
    public static void ValidateChange(string schema, string name, string sql, bool create)
    {
        ValidateName(schema, name);
        var body = Parse(sql);
        if (create ? body is not CreateProcedureStatement : body is not AlterProcedureStatement)
            throw new ArgumentException(create ? "Creation requires CREATE PROCEDURE (not CREATE OR ALTER)." : "Updates require ALTER PROCEDURE (not CREATE OR ALTER).");
        var target = body.ProcedureReference.Name.Identifiers;
        if (target.Count != 2 || target[0].Value != schema || target[1].Value != name || body.ProcedureReference.Number is not null)
            throw new ArgumentException("The SQL target must exactly match the supplied schema and procedure name. Numbered procedures are unsupported.");
        if (body.Options.Any(option => option.OptionKind == ProcedureOptionKind.Encryption))
            throw new ArgumentException("Encrypted procedure definitions are unsupported because future review and backup would be unavailable.");
    }
    public static string Revision(string definition, int objectId, DateTime modified, bool ansi, bool quoted) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{objectId}|{modified:O}|{ansi}|{quoted}|{definition}")));
    public static string Diff(string before, string after)
    {
        // Exact line diff, using shared prefix/suffix and a replacement hunk.
        // A large middle hunk is deliberately preserved instead of hiding SQL.
        var a = before.Replace("\r\n", "\n").Split('\n'); var b = after.Replace("\r\n", "\n").Split('\n');
        var start = 0; while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        var end = 0; while (end < a.Length - start && end < b.Length - start && a[^(end + 1)] == b[^(end + 1)]) end++;
        return $"--- current\n+++ proposed\n@@ -{start + 1},{a.Length - start - end} +{start + 1},{b.Length - start - end} @@\n" + string.Join("\n", a.Skip(start).Take(a.Length - start - end).Select(l => "-" + l)
            .Concat(b.Skip(start).Take(b.Length - start - end).Select(l => "+" + l)));
    }
    public static Dictionary<string, JsonElement> Parameters(string json)
    {
        if (json.Length > 64000) throw new ArgumentException("Parameter JSON exceeds 64,000 characters.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Parameters must be a JSON object.");
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = property.Name.StartsWith('@') ? property.Name : "@" + property.Name;
                if (!result.TryAdd(key, property.Value.Clone()) || property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    throw new ArgumentException("Duplicate parameters or non-scalar values are unsupported. Pass JSON payloads as strings.");
            }
            return result;
        }
        catch (JsonException) { throw new ArgumentException("Invalid parameter JSON."); }
    }
}
