using System.Net;
using System.Text.Json;

namespace SqlServerDevLens.Core;

public sealed record PostmanChoice(string Id, string Name)
{
    public override string ToString() => $"{Name} ({Id})";
}
public sealed record PostmanRequestSummary(string Name, string Method);
public sealed record PostmanSelection(PostmanChoice Workspace, PostmanChoice Collection,
    PostmanRequestSummary[] Requests, string[] VariableNames, DateTimeOffset CheckedAt);
public interface IPostmanClient
{
    Task<PostmanChoice[]> WorkspacesAsync(string key, CancellationToken token);
    Task<PostmanChoice[]> CollectionsAsync(string key, string workspaceId, CancellationToken token);
    Task<PostmanSelection> SelectAsync(string key, PostmanChoice workspace, PostmanChoice collection, CancellationToken token);
}

// Only metadata GETs to the Postman control API. Never executes collection URLs or scripts.
public sealed class PostmanClient : IPostmanClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    public PostmanClient() : this(new Uri("https://api.postman.com/")) { }
    // Alternate origins are limited to loopback for local HTTP contract tests.
    public PostmanClient(Uri endpoint)
    {
        if (endpoint.AbsoluteUri != "https://api.postman.com/" && !(endpoint.IsLoopback && endpoint.Scheme == "http"))
            throw new ArgumentException("Postman endpoint must be the official HTTPS API or a loopback test server.");
        _baseUri = endpoint;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromSeconds(15) };
    }
    private static string Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Invalid Postman resource ID.");
        return value;
    }
    private static string Text(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out var item) || item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            throw new InvalidOperationException("Postman returned incomplete metadata. Reload and select again.");
        var result = item.GetString()!;
        if (result.Length > 300 || result.Any(char.IsControl)) throw new InvalidOperationException("Postman metadata exceeds the display limits.");
        return result;
    }
    internal async Task<JsonDocument> GetAsync(string key, string path, CancellationToken token, HttpMethod? method = null, string? json = null)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1280 || key.Any(c => c < '!' || c > '~'))
            throw new ArgumentException("Enter a valid Postman API key without spaces.");
        try
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(_baseUri, path));
            if (json is not null) request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("X-API-Key", key);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Postman rejected the API key (401). Check or regenerate it.",
                    HttpStatusCode.Forbidden => "Postman denied access (403). Check workspace permissions, region and plan restrictions.",
                    HttpStatusCode.NotFound => "Postman resource not found or no longer accessible (404). Reload the lists.",
                    HttpStatusCode.TooManyRequests => "Postman rate limit reached (429). Wait before loading again.",
                    _ => $"Postman returned HTTP {(int)response.StatusCode}. Try again later."
                });
            const int limit = 4 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("Postman response exceeds the 4 MB limit.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await input.ReadAsync(buffer, token)) != 0)
            {
                if (output.Length + read > limit) throw new InvalidOperationException("Postman response exceeds the 4 MB limit.");
                output.Write(buffer, 0, read);
            }
            return JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Cannot reach Postman. Check internet, proxy/VPN and TLS settings."); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("Postman timed out. Try loading again."); }
        catch (JsonException) { throw new InvalidOperationException("Postman returned invalid or unsupported JSON metadata."); }
    }
    private static PostmanChoice[] Choices(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 1000)
            throw new InvalidOperationException("Postman list is unsupported or exceeds 1000 entries.");
        return array.EnumerateArray().Select(x => new PostmanChoice(Id(x.TryGetProperty("uid", out var uid) ? uid.GetString()! : Text(x, "id")), Text(x, "name")))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<PostmanChoice[]> WorkspacesAsync(string key, CancellationToken token)
    {
        using var doc = await GetAsync(key, "workspaces", token);
        if (!doc.RootElement.TryGetProperty("workspaces", out var array)) throw new InvalidOperationException("Postman workspace list is missing.");
        return Choices(array);
    }
    public async Task<PostmanChoice[]> CollectionsAsync(string key, string workspaceId, CancellationToken token)
    {
        using var doc = await GetAsync(key, "workspaces/" + Id(workspaceId), token);
        if (!doc.RootElement.TryGetProperty("workspace", out var workspace)) throw new InvalidOperationException("Postman workspace is missing.");
        if (Text(workspace, "id") != workspaceId) throw new InvalidOperationException("Postman workspace identity changed. Reload it.");
        return workspace.TryGetProperty("collections", out var array) ? Choices(array) : [];
    }
    public async Task<PostmanSelection> SelectAsync(string key, PostmanChoice workspace, PostmanChoice collection, CancellationToken token)
    {
        var available = await CollectionsAsync(key, workspace.Id, token);
        if (!available.Any(x => x.Id == collection.Id)) throw new InvalidOperationException("Collection is no longer in this workspace. Reload collections.");
        using var doc = await GetAsync(key, "collections/" + Id(collection.Id), token);
        if (!doc.RootElement.TryGetProperty("collection", out var root) || !root.TryGetProperty("info", out var info))
            throw new InvalidOperationException("Postman collection metadata is missing.");
        if (Text(info, "_postman_id") != collection.Id &&
            (!info.TryGetProperty("uid", out var actualUid) || actualUid.GetString() != collection.Id))
            throw new InvalidOperationException("Postman returned a different collection. Reload and select again.");
        var requests = new List<PostmanRequestSummary>();
        void Walk(JsonElement parent, string prefix, int depth)
        {
            if (depth > 20) throw new InvalidOperationException("Collection nesting exceeds 20 folders.");
            if (!parent.TryGetProperty("item", out var items)) return;
            if (items.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Unsupported collection item format.");
            foreach (var item in items.EnumerateArray())
            {
                var name = prefix + Text(item, "name");
                if (name.Length > 1000 || requests.Count >= 1000) throw new InvalidOperationException("Collection exceeds request/display limits.");
                if (item.TryGetProperty("request", out var request))
                    requests.Add(new(name, request.ValueKind == JsonValueKind.Object ? Text(request, "method") : "GET"));
                else Walk(item, name + " / ", depth + 1);
            }
        }
        Walk(root, "", 0);
        var variables = root.TryGetProperty("variable", out var vars) ? vars.EnumerateArray().Select(x => Text(x, "key")).Distinct().ToArray() : [];
        if (variables.Length > 1000) throw new InvalidOperationException("Collection exceeds 1000 variables.");
        if (requests.Sum(x => x.Name.Length + x.Method.Length) + variables.Sum(x => x.Length) > 128000)
            throw new InvalidOperationException("Collection metadata exceeds the 128,000-character limit.");
        // Do not retain raw JSON, variable values, auth, URLs, bodies, scripts or the key.
        return new(workspace, collection with { Name = Text(info, "name") }, requests.ToArray(), variables, DateTimeOffset.UtcNow);
    }
    public void Dispose() => _http.Dispose();
}
