using System.Net;
using System.Security.Cryptography;
using System.Text;
using SqlServerDevLens.Admin;
using SqlServerDevLens.Core;

var builder = WebApplication.CreateBuilder(args);
// Intentionally localhost-only. Reach it with SSH port forwarding, not a public Azure NSG rule.
builder.WebHost.UseUrls("http://127.0.0.1:8090");
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
var app = builder.Build();
var secretDir = Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY")
    ?? throw new InvalidOperationException("Missing protected systemd admin credential mount.");
var loginPath = Path.Combine(secretDir, "admin-token");
var loginToken = File.ReadAllText(loginPath).TrimEnd('\r', '\n');
if (loginToken.Length < 16) throw new InvalidOperationException("Admin login secret must be at least 16 characters.");
var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
static string F(string action, string csrf, string body) =>
    "<form method='post' action='" + E(action) + "'><input type='hidden' name='csrf' value='" + E(csrf) + "'>" + body + "</form>";
static string Input(string name, string label, string value = "", string type = "text", bool required = false) =>
    "<label>" + E(label) + "<input name='" + E(name) + "' type='" + E(type) + "' value='" +
    E(value) + "'" + (required ? " required" : "") + " maxlength='500' autocomplete='off'></label>";
static IResult Html(string text) => Results.Content(text, "text/html; charset=utf-8");
static string Page(string title, string body) => "<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'>"+
    "<meta name='viewport' content='width=device-width, initial-scale=1'><title>" + E(title) + " — DevLens</title>" +
    "<style>body{font:15px system-ui,Arial;background:#f5f7fa;color:#1d2939;margin:0}header{background:#14283e;color:white;padding:18px 5%}"+
    "main{max-width:1060px;margin:26px auto;padding:0 18px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:16px}"+
    "article,section{background:white;padding:22px;border-radius:12px;border:1px solid #dbe1eb;margin-bottom:18px}"+
    "label{display:block;font-weight:600;margin:12px 0}input,select{display:block;width:100%;max-width:470px;box-sizing:border-box;padding:10px;margin-top:5px;border:1px solid #a5b3c2;border-radius:7px}"+
    "input[type=checkbox]{display:inline;width:auto;margin-right:8px}button{background:#155eab;color:#fff;border:0;padding:10px 15px;border-radius:7px;cursor:pointer;margin:6px 6px 6px 0}"+
    "a{color:#1263b3}nav a{margin-right:20px}.muted{color:#54657b}.error{color:#a33}.pill{background:#e8eef7;border-radius:7px;padding:6px}"+
    " .good{font-weight:800;color:#137333}.bad{font-weight:800;color:#b42318}.pending{font-weight:800;color:#9a6700}.secret-panel{padding:12px 0;border-bottom:1px solid #dbe1eb}h1,h2,h3{margin:0 0 13px}code{font-size:13px}</style></head><body><header><strong>SQL Server DevLens · Linux Admin</strong>"+
    "</header><main><nav><a href='/'>All instances</a><a href='/new'>Add instance</a><a href='/logout'>Sign out</a></nav><hr>" +
    "<h1>" + E(title) + "</h1>" + body + "<p class='muted'>This portal is designed for SSH forwarding on localhost only. Never expose port 8090 on the internet.</p></main></body></html>";
static IResult Problem(string message) => Html(Page("Operation could not complete", "<article class='error'><p>"+E(message)+"</p><p><a href='/'>Return to dashboard</a></p></article>"));
static string Link(string instance) => "/instance/" + Uri.EscapeDataString(instance);
static string State(string status, bool boot = false)
{
    var good = boot ? status == "enabled" : status == "active";
    var pending = !boot && (status == "activating" || status == "deactivating");
    return "<strong class='" + (good ? "good" : pending ? "pending" : "bad") + "'>" +
        E(boot ? (good ? "Enabled" : "Disabled") :
            status == "active" ? "Running" : status == "inactive" ? "Stopped" : status == "failed" ? "Failed" : status) + "</strong>";
}
static string SecretPanel(string instance, string csrf, string kind, string title, string? external = null)
{
    var saved = PortalStorage.HasSecret(instance, kind);
    var link = external is null ? "" :
        "<p><a href='" + E(external) + "' target='_blank' rel='noopener noreferrer'>Create " +
        E(kind == "tunnel-key" ? "OpenAI API Key" : "Postman API Key") + "</a></p>";
    return "<div class='secret-panel'><h3>" + E(title) + "</h3>" +
        F(Link(instance)+"/secret",csrf,
            "<input type='hidden' name='kind' value='"+E(kind)+"'>"+
            Input("value","New "+title+" (existing value is never shown)","","password",true)+
            "<button>Save "+E(title)+"</button>") +
        "<p>Status: <strong class='" + (saved?"good":"bad") + "'>" +
        (saved?"Saved":"Missing — required")+"</strong></p>"+link+"</div>";
}
bool SignedIn(HttpContext c)
{
    if (!c.Request.Cookies.TryGetValue("devlens-session", out var supplied) || supplied is null) return false;
    return CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
        SHA256.HashData(Encoding.UTF8.GetBytes(session)));
}
app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
    if (context.Connection.RemoteIpAddress is not null && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
    { context.Response.StatusCode = 403; return; }
    if (!context.Request.Path.StartsWithSegments("/login") && !SignedIn(context))
    { context.Response.Redirect("/login"); return; }
    if (context.Request.Method == "POST" && !context.Request.Path.StartsWithSegments("/login"))
    {
        if (!context.Request.HasFormContentType || (await context.Request.ReadFormAsync())["csrf"].ToString() != csrf)
        { context.Response.StatusCode = 403; return; }
    }
    await next();
});
app.MapGet("/login", () => Html(Page("Administrator sign in",
    "<article><p>Use the administrator secret configured on the Linux VM. The connection is only available through SSH forwarding.</p>" +
    F("/login", "", Input("token", "Admin secret", "", "password", true) + "<button>Sign in</button>") + "</article>")));
app.MapPost("/login", async (HttpContext c) =>
{
    var supplied = (await c.Request.ReadFormAsync())["token"].ToString();
    var ok = CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
        SHA256.HashData(Encoding.UTF8.GetBytes(loginToken)));
    if (!ok) { await Task.Delay(350); return Problem("Invalid administrator secret."); }
    c.Response.Cookies.Append("devlens-session", session,
        new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false,
            Path = "/", MaxAge = TimeSpan.FromHours(6), IsEssential = true });
    return Results.Redirect("/");
});
app.MapGet("/logout", (HttpContext c) => { c.Response.Cookies.Delete("devlens-session"); return Results.Redirect("/login"); });
app.MapGet("/", () =>
{
    try
    {
        var cards = string.Join("", PortalStorage.Instances().Select(name =>
        {
            var cfg = PortalStorage.Database(name); var op = PortalStorage.Operator(name);
            return "<article><h2>"+E(name)+"</h2><p>"+E(cfg.Database)+" · "+E(cfg.Host)+":"+cfg.Port+"</p>"+
                "<p class='muted'>Mode: "+E(op.Mode)+" · Postman: "+(op.Postman is null ? "not selected" : "selected")+"</p>"+
                "<a href='"+Link(name)+"'>Configure instance</a></article>";
        }));
        return Html(Page("All DevLens instances", "<p>One administration site; separate SQL, Postman, tunnel and operator settings per instance.</p>"+
            "<div class='grid'>"+cards+"</div><p><a href='/new'>Add another instance</a></p>"));
    }
    catch { return Problem("Could not read private instance configurations. Check the system journal."); }
});
app.MapGet("/new", () => Html(Page("Add instance", "<article>"+
    F("/new", csrf, Input("name", "Instance name (lowercase, no spaces)", "", "text", true)+
    Input("tunnel", "Existing tunnel ID (tunnel_...)", "", "text", true)+
    "<p><a target='_blank' rel='noopener noreferrer' href='https://platform.openai.com/settings/organization/tunnels'>Create OpenAI Tunnel</a></p>"+
    Input("port", "Local dashboard port", "8080", "number", true)+
    "<button>Create instance</button>")+"</article>")));
app.MapPost("/new", async (HttpContext c) =>
{
    var f = await c.Request.ReadFormAsync();
    var name=f["name"].ToString(); var tunnel=f["tunnel"].ToString(); var port=f["port"].ToString();
    if (!PortalStorage.ValidName(name) || !System.Text.RegularExpressions.Regex.IsMatch(tunnel,"^tunnel_[A-Za-z0-9]+$") ||
        !int.TryParse(port,out var p) || p<1024 || p>65535) return Problem("Invalid instance name, tunnel ID or dashboard port.");
    try { PortalStorage.CreateInstance(name, tunnel, p); return Results.Redirect(Link(name)); }
    catch { return Problem("Provisioning failed. Review private server logs and existing instance configuration before retrying."); }
});
app.MapGet("/instance/{name}", (string name) =>
{
    try
    {
        var d=PortalStorage.Database(name); var op=PortalStorage.Operator(name);
        var form=F(Link(name)+"/db",csrf,
            Input("host","SQL Server DNS host",d.Host,"text",true)+Input("port","SQL port",d.Port.ToString(),"number",true)+
            Input("database","Database name",d.Database,"text",true)+Input("username","SQL login",d.Username,"text",true)+
            "<label><input type='checkbox' name='encrypt' value='yes' "+(d.EncryptConnection?"checked":"")+">Encrypt connection</label>"+
            "<label><input type='checkbox' name='trust' value='yes' "+(d.TrustServerCertificate?"checked":"")+">Trust server certificate (skip certificate validation)</label>"+
            "<p class='muted'>Skipping certificate validation weakens server identity verification. For trusted development SQL servers only.</p>"+
            "<button>Save SQL connection</button>");
        var sqlOnly = op.Mode == "sql-only";
        var modeForm=F(Link(name)+"/mode",csrf,
            "<label>Choose integration mode<select name='mode'>"+
            "<option value='sql-only' "+(sqlOnly||op.Mode=="read-only"?"selected":"")+">SQL Only — full SQL operations</option>"+
            "<option value='full' "+(op.Mode=="full"?"selected":"")+">SQL + Postman — full SQL and Postman operations</option>"+
            "</select></label><button>Save mode and continue</button>");
        var secretForm=SecretPanel(name,csrf,"sql-password","SQL Password")+
            SecretPanel(name,csrf,"tunnel-key","OpenAI Tunnel Key","https://platform.openai.com/api-keys")+
            (sqlOnly||op.Mode=="read-only"?"":SecretPanel(name,csrf,"postman-key","Postman API Key","https://go.postman.co/settings/me/api-keys"));
        var checks=string.Join("",SessionAuthorization.Categories.Where(category => !sqlOnly || !category.StartsWith("postman_", StringComparison.Ordinal)).Select(category =>
            "<label><input type='checkbox' name='category' value='"+E(category)+"' "+
            ((op.AutomaticCategories??[]).Contains(category) ? "checked " : "")+">"+E(category)+"</label>"));
        var policyForm=F(Link(name)+"/policy",csrf,
            "<input type='hidden' name='mode' value='"+E(op.Mode)+"'>"+
            "<p class='muted'>Select categories to auto-authorize exact staged actions. These permissions expire. "+
            "Unselected categories cannot execute without a separate approval mechanism.</p>"+checks+
            Input("hours","Hours until automatic authorization expires (1–24)","12","number")+
            "<button>Save operator policy</button>");
        var sqlCheckForm=F(Link(name)+"/sql-check",csrf,"<button>Test SQL connection</button>");
        var runtimeState=PortalStorage.UnitState(name,false);
        var tunnelState=PortalStorage.UnitState(name,true);
        var bootState=PortalStorage.BootState(name);
        var serviceForm=F(Link(name)+"/service",csrf,
            "<button name='action' value='enable'>Enable boot services</button>"+
            "<button name='action' value='start'>Start</button><button name='action' value='restart'>Restart</button>"+
            "<button name='action' value='stop'>Stop</button>");
        return Html(Page("Configure "+name,
            "<p class='muted'>SQL and Postman settings apply after restarting this instance. Dashboard and tunnel are isolated.</p>"+
            "<section><h2>Step 1 — Choose integration mode</h2>"+
            (op.Mode=="read-only"?"<p class='bad'>Legacy read-only mode is active. Save SQL Only here before configuring SQL authorization categories.</p>":"")+
            modeForm+"</section>"+
            "<div class='grid'><section><h2>SQL Connection</h2>"+
            (string.IsNullOrWhiteSpace(d.Host)||string.IsNullOrWhiteSpace(d.Database)||string.IsNullOrWhiteSpace(d.Username)?
            "<p class='bad'>Required SQL connection details are missing.</p>":"")+
            form+sqlCheckForm+"</section>"+
            "<section><h2>Protected Credentials</h2>"+secretForm+"</section>"+
            "<section><h2>Authorization</h2>"+policyForm+"</section>"+
            (sqlOnly||op.Mode=="read-only"?"":"<section><h2>Postman</h2><p>"+(op.Postman is null?"No collection selected":E(op.Postman.CollectionId))+
            "</p><a href='"+Link(name)+"/postman'>Select workspace &amp; collection</a></section>")+
            "<section><h2>Service</h2><p>SQL MCP runtime: "+State(runtimeState)+"</p><p>OpenAI tunnel: "+State(tunnelState)+
            "</p><p>Boot service: "+State(bootState,true)+"</p>"+serviceForm+
            "<p class='muted'>Enable once to start automatically whenever the Azure VM boots.</p></section></div>"));
    }
    catch { return Problem("Unknown instance or invalid private configuration."); }
});
app.MapPost("/instance/{name}/db", async (string name,HttpContext c) =>
{
    var f=await c.Request.ReadFormAsync();
    if (string.IsNullOrWhiteSpace(f["host"]) || string.IsNullOrWhiteSpace(f["database"]) ||
        string.IsNullOrWhiteSpace(f["username"]) || !int.TryParse(f["port"].ToString(),out var port) ||
        port is < 1 or > 65535)
        return Problem("SQL connection requires valid host, port (1–65535), database and login.");
    try { PortalStorage.SaveDatabase(name,new(name,f["host"].ToString(),port,f["database"].ToString(),f["username"].ToString(),
            f["encrypt"].ToString()=="yes",f["trust"].ToString()=="yes")); return Results.Redirect(Link(name)); }
    catch (ArgumentException) { return Problem("Invalid SQL host, port, database or login."); }
    catch (Exception e) { app.Logger.LogError("SQL config save failed: {ErrorType}", e.GetType().Name);
        return Problem("SQL configuration could not be saved. Check admin service filesystem permissions."); }
});
app.MapPost("/instance/{name}/sql-check", (string name) =>
{
    try { return Html(Page("SQL connection check — "+name,"<article><p>"+E(PortalStorage.SqlCheck(name))+
        "</p><p><a href='"+Link(name)+"'>Return to instance</a></p></article>")); }
    catch { return Problem("SQL test could not be started. Check the admin service journal."); }
});
app.MapPost("/instance/{name}/secret", async (string name,HttpContext c) =>
{
    var f=await c.Request.ReadFormAsync();
    if (string.IsNullOrWhiteSpace(f["value"].ToString())) return Problem("Credential is required before saving.");
    try { PortalStorage.SaveSecret(name,f["kind"].ToString(),f["value"].ToString()); return Results.Redirect(Link(name)); }
    catch { return Problem("Credential validation or secure storage failed."); }
});
app.MapPost("/instance/{name}/mode", async (string name, HttpContext c) =>
{
    var f = await c.Request.ReadFormAsync();
    var mode = f["mode"].ToString();
    if (mode is not ("sql-only" or "full")) return Problem("Choose SQL Only or SQL + Postman.");
    try
    {
        var prior=PortalStorage.Operator(name);
        var categories=prior.AutomaticCategories.Where(x =>
            mode=="full" || !x.StartsWith("postman_",StringComparison.Ordinal)).ToArray();
        PortalStorage.SaveOperator(name,new(mode,categories.Length>0?prior.AutomaticUntilUtc:null,
            categories,mode=="full"?prior.Postman:null));
        return Results.Redirect(Link(name));
    }
    catch { return Problem("Could not change mode. Clear expired authorization settings and try again."); }
});
app.MapPost("/instance/{name}/policy", async (string name,HttpContext c) =>
{
    var f=await c.Request.ReadFormAsync(); var mode=f["mode"].ToString();
    var categories=f.TryGetValue("category",out var list)?list.ToArray():[];
    if(!int.TryParse(f["hours"].ToString(),out var hours)||hours is <1 or >24) return Problem("Choose 1–24 hours.");
    try
    {
        var previous=PortalStorage.Operator(name);
        var expiry=mode!="read-only"&&categories.Length>0 ? DateTimeOffset.UtcNow.AddHours(hours) : (DateTimeOffset?)null;
        PortalStorage.SaveOperator(name,new(mode,expiry,categories,
            mode=="full"?previous.Postman:null));
        return Results.Redirect(Link(name));
    }
    catch { return Problem("Operator policy failed validation. Check mode and permissions."); }
});
app.MapGet("/instance/{name}/postman", async (string name,string? workspaceId) =>
{
    try
    {
        var key=PortalStorage.ReadPostmanKey(name);
        using var postman=new PostmanClient();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var workspaces=await postman.WorkspacesAsync(key,timeout.Token);
        var links=string.Join("",workspaces.Select(w=>"<li><a href='"+Link(name)+"/postman?workspaceId="+Uri.EscapeDataString(w.Id)+"'>"+E(w.Name)+"</a></li>"));
        var detail="";
        if (!string.IsNullOrEmpty(workspaceId) && workspaces.Any(w=>w.Id==workspaceId))
        {
            var collections=await postman.CollectionsAsync(key,workspaceId,timeout.Token);
            detail="<h3>Collections in selected workspace</h3>"+
                F(Link(name)+"/postman",csrf,"<input type='hidden' name='workspaceId' value='"+E(workspaceId)+"'>"+
                    "<label>Collection<select name='collectionId'>"+
                    string.Join("",collections.Select(k=>"<option value='"+E(k.Id)+"'>"+E(k.Name)+"</option>"))+
                    "</select></label><button>Use this collection</button>");
        }
        return Html(Page("Postman selection — "+name,
            "<article><p>These are live Postman workspace and collection names. The API key is never displayed.</p>"+
            "<h2>Choose workspace</h2><ul>"+links+"</ul>"+detail+"</article>"));
    }
    catch { return Problem("Could not load Postman workspaces. Save a valid Postman API key first and verify outbound HTTPS access."); }
});
app.MapPost("/instance/{name}/postman", async (string name,HttpContext c) =>
{
    var f=await c.Request.ReadFormAsync(); var workspace=f["workspaceId"].ToString(); var collection=f["collectionId"].ToString();
    if (!PortalStorage.ValidPostmanId(workspace)||!PortalStorage.ValidPostmanId(collection)) return Problem("Invalid Postman selection.");
    try
    {
        var key=PortalStorage.ReadPostmanKey(name);
        using var client=new PostmanClient();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var choices=await client.WorkspacesAsync(key,timeout.Token);
        var chosen=choices.SingleOrDefault(w=>w.Id==workspace)??throw new InvalidOperationException();
        var collections=await client.CollectionsAsync(key,chosen.Id,timeout.Token);
        var selected=collections.SingleOrDefault(x=>x.Id==collection)??throw new InvalidOperationException();
        _=await client.SelectAsync(key,chosen,selected,timeout.Token);
        var prior=PortalStorage.Operator(name);
        PortalStorage.SaveOperator(name,new("full",prior.AutomaticUntilUtc,
            prior.AutomaticCategories??[],new(workspace,collection)));
        return Results.Redirect(Link(name));
    }
    catch { return Problem("Postman selection could not be verified or securely saved."); }
});
app.MapPost("/instance/{name}/service", async (string name,HttpContext c) =>
{
    try { var f=await c.Request.ReadFormAsync(); PortalStorage.Service(name,f["action"].ToString()); return Results.Redirect(Link(name)); }
    catch { return Problem("Service operation failed. Check protected system logs and credentials."); }
});
app.Run();
