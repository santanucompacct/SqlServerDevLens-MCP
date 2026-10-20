using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SqlServerDevLens.Core;

public interface IPostmanBackupStore { Task SaveAsync(string json, CancellationToken token); }
public sealed class PrivatePostmanBackupStore(string? directory = null) : IPostmanBackupStore
{
    public Task SaveAsync(string json, CancellationToken token) => new PrivateProcedureBackupStore(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlServerDevLens", "PostmanBackups"))
        .SaveContentAsync(json, ".json", token);
}

public sealed class PostmanActionBackend : IPostmanActionBackend
{
    private string? _key;
    private readonly PostmanSelection _selection;
    private readonly PostmanClient _control;
    private readonly HttpClient _application = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly IPostmanBackupStore _backups;
    private readonly bool _loopbackTest;
    private readonly SemaphoreSlim _actions = new(1, 1);
    public string CollectionName => _selection.Collection.Name;
    public PostmanActionBackend(string key, PostmanSelection selection, IPostmanBackupStore? backups = null, Uri? testControlEndpoint = null)
    {
        _key = key; _selection = selection; _backups = backups ?? new PrivatePostmanBackupStore();
        _control = testControlEndpoint is null ? new PostmanClient() : new PostmanClient(testControlEndpoint);
        _loopbackTest = testControlEndpoint is not null && testControlEndpoint.IsLoopback;
    }
    private string Key => _key ?? throw new InvalidOperationException("Postman session was stopped.");
    private sealed record Snapshot(JsonObject Collection, string ApiId, string Revision, string[] Secrets);
    private sealed record Located(JsonObject Item, JsonObject[] Scopes, string Path);
    private static string Id(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 160 || id.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Use an exact Postman ID from list_postman_items.");
        return id;
    }
    private static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw new ArgumentException("Name must be 1–200 display characters.");
        return name;
    }
    private static bool Sensitive(string key) => Regex.IsMatch(key, "key|token|secret|password|authorization|cookie|credential", RegexOptions.IgnoreCase);
    private static Dictionary<string,string> Variables(JsonObject collection)
    {
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var item in (collection["variable"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (item["disabled"]?.GetValue<bool>() == true) continue;
            var key = item["key"]?.ToString() ?? "";
            if (!result.TryAdd(key, item["value"]?.ToString() ?? "")) throw new ArgumentException("Duplicate collection variables are unsupported.");
        }
        return result;
    }
    private static string Resolve(string text, Dictionary<string,string> variables)
    {
        for(var depth=0; depth<10 && text.Contains("{{"); depth++)
            text = Regex.Replace(text, @"\{\{([^{}]+)\}\}", m=>variables.TryGetValue(m.Groups[1].Value,out var value) && !string.IsNullOrEmpty(value)
                ? value : throw new ArgumentException("A required shared collection variable is missing or empty. Update it in Postman; do not send secrets in chat."));
        if (text.Contains("{{") || text.Length > 128000) throw new ArgumentException("Unresolved variables or oversized request.");
        return text;
    }
    private static string Redact(string text, IEnumerable<string> secrets)
    {
        foreach(var secret in secrets.Where(s=>!string.IsNullOrEmpty(s)).Distinct().OrderByDescending(s=>s.Length))
        {
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            text = text.Replace(Uri.EscapeDataString(secret), "[REDACTED]", StringComparison.Ordinal);
            text = text.Replace(JsonSerializer.Serialize(secret)[1..^1], "[REDACTED]", StringComparison.Ordinal);
        }
        return text;
    }
    private static string Mask(JsonNode node, string[] secrets)
    {
        var clone = node.DeepClone();
        void Scrub(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach(var field in obj.ToArray())
                    if (Sensitive(field.Key) && field.Key != "key")
                    { if(field.Value is not JsonValue reference || !Regex.IsMatch(reference.ToString(),@"^\{\{[^{}]+\}\}$")) obj[field.Key] = "[REDACTED]"; }
                    else if (field.Key is "raw" or "rawModeData" or "body" && field.Value is JsonValue text && text.GetValueKind()==JsonValueKind.String)
                    {
                        try { var nested=JsonNode.Parse(text.ToString()); if(nested is not null) { Scrub(nested); obj[field.Key]=nested.ToJsonString(); } } catch(JsonException) { }
                    }
                    else Scrub(field.Value);
                // Postman's key/value arrays need masking by the semantic key, too.
                if (obj["key"] is JsonValue k && Sensitive(k.ToString()) && obj.ContainsKey("value") &&
                    !Regex.IsMatch(obj["value"]?.ToString()??"",@"^\{\{[^{}]+\}\}$")) obj["value"] = "[REDACTED]";
            }
            else if (current is JsonArray array) foreach(var value in array) Scrub(value);
        }
        Scrub(clone);
        return Redact(clone.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), secrets);
    }
    private async Task<Snapshot> LoadAsync(CancellationToken token)
    {
        var key = Key;
        if (!(await _control.CollectionsAsync(key, _selection.Workspace.Id, token)).Any(c=>c.Id==_selection.Collection.Id))
            throw new InvalidOperationException("Selected collection is no longer in the workspace.");
        using var doc = await _control.GetAsync(key, "collections/"+Id(_selection.Collection.Id), token);
        var collection = JsonNode.Parse(doc.RootElement.GetRawText())?["collection"] as JsonObject
            ?? throw new InvalidOperationException("Postman collection is unavailable.");
        var info = collection["info"];
        if (info?["uid"]?.ToString() != _selection.Collection.Id && info?["_postman_id"]?.ToString() != _selection.Collection.Id)
            throw new InvalidOperationException("Postman collection identity mismatch.");
        var json = collection.ToJsonString();
        var secrets = new List<string> { key };
        foreach(var item in Variables(collection)) if(Sensitive(item.Key) && item.Value.Length > 0) secrets.Add(item.Value);
        // Collect literal auth/header secrets throughout the selected collection for output masking.
        void Collect(JsonNode? node)
        {
            if(node is JsonObject obj)
            {
                var semanticKey=obj["key"]?.ToString();
                if(semanticKey is not null && semanticKey!="key" && (Sensitive(semanticKey) || semanticKey=="value") && obj["value"] is JsonValue value && !value.ToString().Contains("{{")) secrets.Add(value.ToString());
                foreach(var field in obj)
                {
                    if(Sensitive(field.Key) && field.Key!="key" && field.Value is JsonValue v && v.GetValueKind()==JsonValueKind.String) secrets.Add(v.ToString());
                    else if(field.Key is "raw" or "rawModeData" && field.Value is JsonValue raw && raw.GetValueKind()==JsonValueKind.String)
                    { try { Collect(JsonNode.Parse(raw.ToString())); } catch(JsonException){} }
                    else Collect(field.Value);
                }
            }
            else if(node is JsonArray array) foreach(var value in array) Collect(value);
        }
        Collect(collection);
        return new(collection, Id(info!["_postman_id"]!.ToString()), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), secrets.Where(x=>x.Length>0).ToArray());
    }
    private static List<Located> Items(Snapshot snapshot)
    {
        var result = new List<Located>(); var total=0;
        void Walk(JsonObject parent, JsonObject[] scopes, string prefix, int depth)
        {
            if(depth>20) throw new ArgumentException("Collection nesting exceeds 20 folders.");
            foreach(var item in (parent["item"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if(result.Count>=1000) throw new ArgumentException("Collection exceeds 1000 items.");
                var path=prefix+item["name"]?.ToString();
                total+=path.Length;
                if(path.Length>1000 || total>128000) throw new ArgumentException("Collection item paths exceed the display limits.");
                result.Add(new(item, [..scopes,item], path));
                if(item["item"] is JsonArray) Walk(item,[..scopes,item],path+" / ",depth+1);
            }
        }
        Walk(snapshot.Collection,[snapshot.Collection],"",0); return result;
    }
    private static Located Find(Snapshot snapshot, string id, bool folder = false) => Items(snapshot).SingleOrDefault(x=>x.Item["id"]?.ToString()==Id(id) &&
        (folder ? x.Item["item"] is JsonArray : x.Item["request"] is JsonObject)) ?? throw new ArgumentException("Selected item is not a visible request/folder in this collection.");
    public async Task<PostmanItem[]> ListAsync(CancellationToken token)
    {
        var snapshot=await LoadAsync(token);
        return Items(snapshot).Select(x=>new PostmanItem(Id(x.Item["id"]?.ToString()??""),Redact(x.Path,snapshot.Secrets),x.Item["request"] is null?"folder":"request",x.Item["request"]?["method"]?.ToString())).ToArray();
    }
    public async Task<string> ReadAsync(string requestId, CancellationToken token)
    {
        var snapshot=await LoadAsync(token); var found=Find(snapshot,requestId);
        var text=Mask(new JsonObject { ["id"]=requestId,["path"]=found.Path,["request"]=found.Item["request"]!.DeepClone() },snapshot.Secrets);
        if(text.Length>64000) throw new ArgumentException("Request definition exceeds the 64,000-character display limit.");
        return text;
    }
    private static void NoScripts(IEnumerable<JsonObject> scopes)
    {
        static bool HasCode(JsonNode? code) => code is JsonArray lines ? lines.Any(line=>!string.IsNullOrWhiteSpace(line?.ToString())) : !string.IsNullOrWhiteSpace(code?.ToString());
        foreach(var scope in scopes)
            if ((scope["event"] as JsonArray ?? scope["events"] as JsonArray)?.Any(e=>HasCode(e?["script"]?["exec"]) || e?["script"]?["src"] is not null ||
                    (e?["script"]?["packages"] as JsonObject)?.Count>0 || (e?["script"]?["requests"] as JsonObject)?.Count>0)==true ||
                !string.IsNullOrWhiteSpace(scope["preRequestScript"]?.ToString()) || !string.IsNullOrWhiteSpace(scope["tests"]?.ToString()))
                throw new ArgumentException("Requests with nonempty scripts/tests are unsupported. DevLens will not silently skip or execute them.");
    }
    private static string Scope(Snapshot snapshot, string name) => $"Selected collection: {name}. Revision: {snapshot.Revision}. Masked secrets remain in desktop memory. No environment, Vault, scripts, cookies or file uploads are supported.";
    public async Task<PostmanPlan> PrepareChangeAsync(string action, string name, string requestId, string templateRequestId,
        string folderId, string? headersJson, string? bodyJson, CancellationToken token)
    {
        if(action is not ("create_folder" or "create_request" or "edit_request")) throw new ArgumentException("Use create_folder, create_request or edit_request.");
        if(action=="edit_request" && folderId.Length>0) throw new ArgumentException("Editing cannot move a request to another folder.");
        var snapshot=await LoadAsync(token); JsonObject? parent=null;
        if(folderId.Length>0) parent=Find(snapshot,folderId,true).Item;
        if(action=="create_folder")
        {
            Name(name);
            if((parent??snapshot.Collection)["item"]!.AsArray().OfType<JsonObject>().Any(x=>x["name"]?.ToString()==name)) throw new ArgumentException("An item with that name already exists in this folder.");
            var payload=new JsonObject { ["name"]=name }; if(parent is not null) payload["folder"]=Id(folderId);
            return new(action,name,snapshot.Revision,"",Mask(payload,snapshot.Secrets),Scope(snapshot,CollectionName))
            { Payload=payload.ToJsonString(),FolderId=folderId,Backup=snapshot.Collection.ToJsonString(),Secrets=snapshot.Secrets };
        }
        var found=Find(snapshot,action=="create_request"?templateRequestId:requestId);
        NoScripts(found.Scopes);
        if(found.Scopes.Skip(1).Any(x=>(x["variable"] as JsonArray)?.Count>0)) throw new ArgumentException("Template/request folder variables are unsupported.");
        var targetScopes=parent is null ? new[]{snapshot.Collection} : Find(snapshot,folderId,true).Scopes;
        if(action=="create_request")
        {
            NoScripts(targetScopes);
            var sourceAuth=EffectiveAuth(found.Scopes); var destinationAuth=EffectiveAuth(targetScopes);
            if((found.Item["request"]?["auth"] is null || found.Item["request"]?["auth"]?["type"]?.ToString()=="inherit") && !JsonNode.DeepEquals(sourceAuth,destinationAuth))
                throw new ArgumentException("Template inherits different folder authentication. Choose a compatible destination.");
        }
        using var nativeDoc=await _control.GetAsync(Key,"collections/"+snapshot.ApiId+"/requests/"+Id(action=="create_request"?templateRequestId:requestId),token);
        var native=JsonNode.Parse(nativeDoc.RootElement.GetRawText())?["data"] as JsonObject ?? throw new InvalidOperationException("Request definition unavailable.");
        if(native["id"]?.ToString() != (action=="create_request"?templateRequestId:requestId)) throw new InvalidOperationException("Request identity changed.");
        NoScripts([native]);
        if(native.ToJsonString().Length>64000) throw new ArgumentException("Existing request exceeds the review limit.");
        if(native["dataMode"] is not null && native["dataMode"]?.ToString()!="raw") throw new ArgumentException("Only raw JSON requests are editable in this milestone.");
        var proposed=new JsonObject();
        // Preserve native fields required by Postman's request API; do not replace the collection.
        foreach(var field in new[]{"name","description","method","url","headerData","queryParams","dataMode","rawModeData","dataOptions","auth","events","protocolProfileBehavior","pathVariableData","variables","dataDisabled"})
            proposed[field]=native[field]?.DeepClone();
        if(name.Length>0) proposed["name"]=Name(name); else if(action=="create_request") throw new ArgumentException("New request name is required.");
        if(action=="create_request" && (parent??snapshot.Collection)["item"]!.AsArray().OfType<JsonObject>().Any(x=>x["name"]?.ToString()==name)) throw new ArgumentException("An item with that name already exists in this folder.");
        if(bodyJson is not null)
        {
            if(bodyJson.Length>20000) throw new ArgumentException("JSON body exceeds 20,000 characters.");
            try { using var body=JsonDocument.Parse(bodyJson); if(body.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) throw new ArgumentException("Body must be a JSON object or array."); }
            catch(JsonException) { throw new ArgumentException("Invalid request JSON body."); }
            proposed["dataMode"]="raw"; proposed["rawModeData"]=bodyJson;
            proposed["dataOptions"]=new JsonObject { ["raw"]=new JsonObject { ["language"]="json" } };
        }
        if(headersJson is not null)
        {
            if(headersJson.Length>10000) throw new ArgumentException("Header changes exceed 10,000 characters.");
            JsonObject changes; try { changes=JsonNode.Parse(headersJson) as JsonObject ?? throw new ArgumentException("Headers must be a JSON object of string values."); }
            catch(JsonException) { throw new ArgumentException("Invalid header JSON."); }
            var headers=proposed["headerData"] as JsonArray ?? new JsonArray();
            foreach(var field in changes)
            {
                if(field.Value is not JsonValue value || value.GetValueKind()!=JsonValueKind.String) throw new ArgumentException("Header values must be strings.");
                var text=value.ToString(); ValidateHeader(field.Key,text);
                if(Sensitive(field.Key) && !Regex.IsMatch(text,@"^\{\{[A-Za-z0-9_.-]+\}\}$")) throw new ArgumentException("Sensitive headers must use a collection variable reference, never a plaintext secret.");
                var matching=headers.OfType<JsonObject>().Where(x=>string.Equals(x["key"]?.ToString(),field.Key,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(matching.Length>1) throw new ArgumentException("Duplicate header keys are unsupported.");
                var item=matching.SingleOrDefault();
                if(item is null) { item=new JsonObject { ["key"]=field.Key }; headers.Add(item); }
                item["value"]=text; item["enabled"]=true;
            }
            proposed["headerData"]=headers;
        }
        if(proposed.ToJsonString().Length>64000) throw new ArgumentException("Request exceeds the 64,000-character review limit.");
        return new(action,proposed["name"]!.ToString(),snapshot.Revision,action=="edit_request"?Mask(native,snapshot.Secrets):"",Mask(proposed,snapshot.Secrets),Scope(snapshot,CollectionName)+"\nURL, method and authentication are preserved from the existing request/template. This saves a request; it does not run it.")
        { Payload=proposed.ToJsonString(),TargetId=action=="edit_request"?requestId:"",FolderId=folderId,Backup=snapshot.Collection.ToJsonString(),Secrets=snapshot.Secrets };
    }
    private static JsonNode? EffectiveAuth(IEnumerable<JsonObject> scopes) => scopes.Reverse().Select(x=>x["request"]?["auth"]??x["auth"]).FirstOrDefault(x=>x is not null && x["type"]?.ToString()!="inherit");
    private static void ValidateHeader(string name,string value)
    {
        if(name.Length is <1 or >100 || !Regex.IsMatch(name,@"^[!#$%&'*+.^_`|~0-9A-Za-z-]+$") || value.Length>8000 || value.Any(c=>c<' ' || c=='\u007f') ||
            new[]{"Host","Cookie","Content-Length","Transfer-Encoding","Connection","Proxy-Authorization","Content-Type"}.Contains(name,StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported header name/value. Content-Type is managed as application/json; routing, cookie and transport headers cannot be set.");
    }
    public async Task<PostmanPlan> PrepareRunAsync(string requestId,CancellationToken token)
    {
        var snapshot=await LoadAsync(token); var found=Find(snapshot,requestId); NoScripts(found.Scopes);
        if(found.Scopes.Any(s=>(s!=snapshot.Collection && (s["variable"] as JsonArray)?.Count>0) || (s["protocolProfileBehavior"] as JsonObject)?.Count>0))
            throw new ArgumentException("Folder/request variables and custom protocol settings are unsupported.");
        var request=found.Item["request"]!; var variables=Variables(snapshot.Collection);
        if((request["variable"] as JsonArray)?.Count>0 || (request["url"] is JsonObject urlObject && (urlObject["variable"] as JsonArray)?.Count>0))
            throw new ArgumentException("Request/path variables are unsupported; use shared collection variables.");
        var method=request["method"]?.ToString()??"";
        if(!new[]{"GET","POST","PUT","PATCH","DELETE","HEAD","OPTIONS"}.Contains(method)) throw new ArgumentException("Unsupported HTTP method.");
        var urlNode=request["url"]; var url=Resolve(urlNode is JsonValue ? urlNode.ToString() : urlNode?["raw"]?.ToString()??"",variables);
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || (uri.Scheme!="https" && !(_loopbackTest && uri.IsLoopback && uri.Scheme=="http")) || uri.UserInfo.Length>0 || uri.Fragment.Length>0)
            throw new ArgumentException("Application URL must be absolute HTTPS without embedded credentials or fragments.");
        if(uri.Host.Equals("api.postman.com",StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("api.getpostman.com",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Application requests cannot target the Postman control API.");
        string? body=null;
        if(request["body"] is JsonObject bodyNode)
        {
            if(bodyNode["mode"]?.ToString()!="raw") throw new ArgumentException("Only raw JSON bodies are supported. No uploads/form/binary bodies.");
            body=Resolve(bodyNode["raw"]?.ToString()??"",variables);
            if(body.Length>64000) throw new ArgumentException("Resolved body exceeds 64,000 characters.");
            try { using var parsed=JsonDocument.Parse(body); } catch(JsonException) { throw new ArgumentException("Resolved body is not valid JSON."); }
        }
        var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); var secrets=snapshot.Secrets.ToList();
        foreach(var header in (request["header"] as JsonArray??[]).OfType<JsonObject>())
        {
            if(header["disabled"]?.GetValue<bool>()==true) continue;
            var name=header["key"]?.ToString()??""; var value=Resolve(header["value"]?.ToString()??"",variables);
            if(name.Equals("Content-Type",StringComparison.OrdinalIgnoreCase) && value.Equals("application/json",StringComparison.OrdinalIgnoreCase)) continue;
            ValidateHeader(name,value); if(!headers.TryAdd(name,value)) throw new ArgumentException("Duplicate headers are unsupported.");
            if(Sensitive(name)) secrets.Add(value);
        }
        var auth=EffectiveAuth(found.Scopes); var type=auth?["type"]?.ToString();
        if(type is not (null or "noauth"))
        {
            var values=(auth![type] as JsonArray??[]).OfType<JsonObject>().ToDictionary(x=>x["key"]!.ToString(),x=>Resolve(x["value"]?.ToString()??"",variables));
            string name,value;
            if(type=="apikey" && values.GetValueOrDefault("in")=="header") { name=values.GetValueOrDefault("key")??""; value=values.GetValueOrDefault("value")??""; }
            else if(type=="bearer") { name="Authorization"; value="Bearer "+values.GetValueOrDefault("token"); }
            else throw new ArgumentException("Only no-auth, header API-key and bearer authentication are supported.");
            if(string.IsNullOrWhiteSpace(value) || value=="Bearer ") throw new ArgumentException("Application authentication is empty. Update shared collection variables.");
            ValidateHeader(name,value); if(!headers.TryAdd(name,value)) throw new ArgumentException("Conflicting explicit and inherited authentication headers.");
            secrets.Add(value); if(type=="bearer") secrets.Add(values["token"]);
        }
        if(secrets.Any(s=>s.Length>0 && (url.Contains(s,StringComparison.Ordinal) || url.Contains(Uri.EscapeDataString(s),StringComparison.Ordinal))))
            throw new ArgumentException("Secrets in application URLs are unsupported. Use header authentication.");
        if(headers.Values.Any(v=>v.Contains(Key,StringComparison.Ordinal)) || (body?.Contains(Key,StringComparison.Ordinal)??false))
            throw new ArgumentException("The Postman control API key must never be sent to an application.");
        var display=new JsonObject { ["method"]=method,["url"]=url,["headers"]=JsonSerializer.SerializeToNode(headers),["body"]=body };
        var masked=Mask(display,secrets.ToArray());
        return new("run",found.Path,snapshot.Revision,"",masked,Scope(snapshot,CollectionName)+"\nRUN ONCE: sends this resolved HTTP request to the displayed destination. Any method may have side effects. Redirects, retries, scripts and cookie persistence are disabled. Response data is shared with chat, with known secrets masked and bounded output.")
        { TargetId=requestId,Request=new(method,url,headers,body),Secrets=secrets.ToArray() };
    }
    public async Task<PostmanActionResult> ApplyAsync(PostmanPlan plan,CancellationToken token)
    {
        await _actions.WaitAsync(token);
        try
        {
            var current=await LoadAsync(token);
            if(current.Revision!=plan.Revision) throw new PostmanReviewChangedException();
            if(plan.Action=="run")
            {
                var resolved=plan.Request!;
                using var request=new HttpRequestMessage(new HttpMethod(resolved.Method),resolved.Url);
                if(resolved.Body is not null) request.Content=new StringContent(resolved.Body,Encoding.UTF8,"application/json");
                foreach(var header in resolved.Headers) request.Headers.Add(header.Key,header.Value);
                // Separate client: the Postman control API key can never become a default application header.
                using var response=await _application.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
                await using var stream=await response.Content.ReadAsStreamAsync(token); using var output=new MemoryStream();
                var buffer=new byte[8192];
                while(true)
                {
                    var read=await stream.ReadAsync(buffer,token); if(read==0) break;
                    if(output.Length+read>CapturedResults.MaxCaptureCharacters) throw new InvalidOperationException("HTTP response exceeds the 16 MiB capture budget. Request may have completed; do not retry automatically.");
                    output.Write(buffer,0,read);
                }
                var text=Encoding.UTF8.GetString(output.ToArray());
                try { var json=JsonNode.Parse(text); if(json is not null) text=Mask(json,plan.Secrets); } catch(JsonException){}
                text=Redact(text,plan.Secrets);
                return new("completed",HttpStatus:(int)response.StatusCode,Body:text,Truncated:false,Message:"HTTP status is not an application-level success guarantee. Inspect response status/data. Redirects were not followed; no scripts/tests ran.");
            }
            await _backups.SaveAsync(plan.Backup,token);
            var path="collections/"+current.ApiId;
            if(plan.Action=="create_folder") path+="/folders";
            else path+="/requests"+(plan.Action=="edit_request"?"/"+Id(plan.TargetId):plan.FolderId.Length>0?"?folder="+Id(plan.FolderId):"");
            using var result=await _control.GetAsync(Key,path,token,plan.Action=="edit_request"?HttpMethod.Put:HttpMethod.Post,plan.Payload);
            var id=result.RootElement.TryGetProperty("data",out var data) && data.TryGetProperty("id",out var itemId)?itemId.GetString():null;
            if(string.IsNullOrEmpty(id)) throw new InvalidOperationException("Postman write returned no resource ID. Verify before retry.");
            var after=await LoadAsync(token); var saved=Find(after,id,plan.Action=="create_folder");
            if(saved.Item["name"]?.ToString()!=plan.Name) throw new InvalidOperationException("Postman save could not be verified. Inspect before retry.");
            if(plan.Action!="create_folder")
            {
                using var verify=await _control.GetAsync(Key,"collections/"+after.ApiId+"/requests/"+Id(id),token);
                var actual=JsonNode.Parse(verify.RootElement.GetRawText())?["data"]; var expected=JsonNode.Parse(plan.Payload)!;
                foreach(var field in new[]{"method","url","rawModeData","headerData","auth"})
                    if(!JsonNode.DeepEquals(actual?[field],expected[field])) throw new InvalidOperationException("Saved request fields differ from approved content. Inspect before retry.");
            }
            return new("saved",id,Message:"Approved Postman resource saved and read back. Private pre-change collection backup saved. Application request was not executed.");
        }
        catch(HttpRequestException) { throw new InvalidOperationException("HTTP operation failed. Outcome may be uncertain; do not retry automatically."); }
        finally { _actions.Release(); }
    }
    public void Dispose() { _key=null; _control.Dispose(); _application.Dispose(); }
}
