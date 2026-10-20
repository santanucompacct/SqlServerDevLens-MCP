using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SqlServerDevLens.Core;

public sealed record CapturedResultReference(string ResultId, string Revision, int TotalCharacters, DateTimeOffset ExpiresAt);
public sealed record CapturedResultPage(string ResultId, string Revision, string Json, int TotalCharacters,
    int Offset, int? NextOffset, bool Complete, DateTimeOffset ExpiresAt);

// Desktop-session memory only: no credentials/data are written to the repository or disk.
// Pages read immutable JSON from ONE completed call, never execute a procedure/HTTP request.
public sealed class CapturedResults(TimeProvider? clock = null) : IDisposable
{
    public const int MaxCaptureCharacters = 16 * 1024 * 1024;
    public const int MaxSessionCharacters = 64 * 1024 * 1024;
    public const int InlineCharacters = 16000;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, (CapturedResultReference Reference, string Json)> _results = [];
    private int _reserved;
    private bool _disposed;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private void Prune()
    {
        if (_disposed) throw new InvalidOperationException("Captured results were cleared by Stop.");
        foreach (var id in _results.Where(x => x.Value.Reference.ExpiresAt <= _clock.GetUtcNow()).Select(x => x.Key).ToArray()) _results.Remove(id);
    }
    // Reserve before the external call, so a full session fails BEFORE consuming approval/executing.
    public Reservation Reserve()
    {
        lock (_gate)
        {
            Prune();
            if (_results.Values.Sum(x => (long)x.Json.Length) + _reserved + MaxCaptureCharacters > MaxSessionCharacters)
                throw new InvalidOperationException("Captured-result memory is full. Read/release earlier results before starting another action. Nothing was started.");
            _reserved += MaxCaptureCharacters;
            return new(this);
        }
    }
    public sealed class Reservation(CapturedResults owner) : IDisposable
    {
        private bool _active = true;
        public CapturedResultReference Save<T>(T value)
        {
            // The bounded stream stops serialization growth rather than allocating an unlimited JSON string.
            using var output = new BoundedStream(MaxCaptureCharacters * 3);
            JsonSerializer.Serialize(output, value, JsonOptions);
            var json = Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
            if (json.Length > MaxCaptureCharacters) throw new InvalidOperationException("Result exceeds the 16 Mi-character capture budget. Outcome may have committed; do not rerun automatically.");
            lock (owner._gate)
            {
                owner.Prune();
                if (!_active) throw new InvalidOperationException("Capture reservation already consumed.");
                var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                var reference = new CapturedResultReference(id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
                    json.Length, owner._clock.GetUtcNow().AddMinutes(15));
                owner._results.Add(id, (reference, json));
                Dispose();
                return reference;
            }
        }
        public void Dispose() { lock (owner._gate) { if (_active) { owner._reserved -= MaxCaptureCharacters; _active = false; } } }
    }
    private sealed class BoundedStream(int maximum) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        private void Check(int count) { if (Length + count > maximum) throw new InvalidOperationException("Serialized result exceeds capture budget. The action may have committed. Do not retry automatically."); }
    }
    public CapturedResultPage Read(string id, int offset = 0, int maxCharacters = 32000, string? expectedRevision = null)
    {
        lock (_gate)
        {
            Prune();
            if (!_results.TryGetValue(id, out var item)) throw new ArgumentException("Captured result is unknown, expired, released or cleared by Stop. Do not re-execute an uncertain action.");
            var page = ProcedureDefinitionPage.Create(new("capture", id, item.Json, item.Reference.Revision, true, true, []), offset, maxCharacters, expectedRevision);
            return new(id, page.Revision, page.Definition, page.TotalCharacters, offset, page.NextOffset, page.Complete, item.Reference.ExpiresAt);
        }
    }
    public void Release(string id) { lock (_gate) { Prune(); if (!_results.Remove(id)) throw new ArgumentException("Captured result is unknown or expired."); } }
    public void Dispose() { lock (_gate) { _disposed = true; _results.Clear(); } }
}

// Text is streamed into a bounded accumulator: oversize data fails explicitly, never clips JSON.
public sealed class ResultReadBudget
{
    private int _used;
    public void Add(int characters)
    {
        if (characters > CapturedResults.MaxCaptureCharacters - _used)
            throw new InvalidOperationException("Result exceeds the 16 Mi-character capture budget. Execution may have committed. Do not retry automatically.");
        _used += characters;
    }
    public async Task<string> ReadTextAsync(TextReader reader, CancellationToken token)
    {
        var value = new StringBuilder(); var buffer = new char[8192];
        while (true) { var n = await reader.ReadAsync(buffer.AsMemory(), token); if (n == 0) break; Add(n); value.Append(buffer, 0, n); }
        return value.ToString();
    }
}
