using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class PostmanActionTests
{
    [Fact]
    public async Task ApprovalCannotBeForgedReplayedOrUsedForWrongAction()
    {
        var backend=new FakeBackend(); Action<bool>? decision=null;
        using var workflow=new PostmanWorkflow(backend,(_,approve,_)=>decision=approve);
        var pending=await workflow.PrepareRunAsync("request",default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>workflow.ApplyAsync(pending.RequestId,true,default));
        decision!(true);
        await Assert.ThrowsAsync<ArgumentException>(()=>workflow.ApplyAsync(pending.RequestId,false,default));
        var results=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{try {await workflow.ApplyAsync(pending.RequestId,true,default);return true;}catch(ArgumentException){return false;}}));
        Assert.Single(results,x=>x); Assert.Equal(1,backend.Applied);
        await Assert.ThrowsAsync<ArgumentException>(()=>workflow.ApplyAsync(pending.RequestId,true,default));
        pending=await workflow.PrepareRunAsync("request",default);decision(false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>workflow.ApplyAsync(pending.RequestId,true,default));
        pending=await workflow.PrepareRunAsync("request",default);decision(true);backend.Fail=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>workflow.ApplyAsync(pending.RequestId,true,default));
        Assert.Throws<ArgumentException>(()=>workflow.Status(pending.RequestId));
        pending=await workflow.PrepareRunAsync("request",default);workflow.Dispose();decision(true);
        Assert.Throws<ArgumentException>(()=>workflow.Status(pending.RequestId));
    }
    [Fact]
    public async Task ExpiryAndUnavailableReviewFailClosed()
    {
        using(var unavailable=new PostmanWorkflow(new FakeBackend(),null)) await Assert.ThrowsAsync<InvalidOperationException>(()=>unavailable.PrepareRunAsync("request",default));
        var clock=new Clock();Action<bool>? decide=null;
        using var workflow=new PostmanWorkflow(new FakeBackend(),(_,approve,_)=>decide=approve,clock);
        var pending=await workflow.PrepareRunAsync("request",default);clock.Now=clock.Now.AddMinutes(6);decide!(true);
        Assert.Throws<ArgumentException>(()=>workflow.Status(pending.RequestId));
    }
    [Fact]
    public async Task RealStdioForwardsPostmanApprovalAndClearsOnStop()
    {
        var pipe="dpa-"+Guid.NewGuid().ToString("N");Action<bool>? decide=null;
        var backend=new FakeBackend();
        using var workflow=new PostmanWorkflow(backend,(_,approve,_)=>decide=approve);
        await using var server=new DesktopMcpServer(pipe);
        await server.StartAsync(DateTimeOffset.UtcNow,postmanActions:workflow);
        var dll=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../SqlServerDevLens.McpHost/bin/Release/net10.0/SqlServerDevLens.McpHost.dll"));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client=await McpClient.CreateAsync(new StdioClientTransport(new(){Command="dotnet",Arguments=[dll,"--desktop","--pipe-name",pipe]}),cancellationToken:timeout.Token);
        var prepared=await client.CallToolAsync("prepare_postman_run",new Dictionary<string,object?>{["requestId"]="request",["approved"]=true},cancellationToken:timeout.Token);
        var id=JsonDocument.Parse(((TextContentBlock)prepared.Content[0]).Text).RootElement.GetProperty("requestId").GetString()!;
        var denied=await client.CallToolAsync("run_prepared_postman_request",new Dictionary<string,object?>{["requestId"]=id},cancellationToken:timeout.Token);
        Assert.True(denied.IsError);Assert.Equal(0,backend.Applied);
        decide!(true);
        var applied=await client.CallToolAsync("run_prepared_postman_request",new Dictionary<string,object?>{["requestId"]=id},cancellationToken:timeout.Token);
        Assert.NotEqual(true,applied.IsError);Assert.Equal(1,backend.Applied);
        var another=await workflow.PrepareRunAsync("request",timeout.Token);
        await server.StopAsync();Assert.Throws<ArgumentException>(()=>workflow.Status(another.RequestId));
    }
    [PostmanSimulationFact]
    public async Task RealHttpClientAgainstPostmanSimulationActions()
    {
        var baseUri=new Uri(new Uri(Environment.GetEnvironmentVariable("DEVLENS_POSTMAN_SIMULATION_URL")!),"../actions/");
        using var control=new HttpClient();control.DefaultRequestHeaders.Add("X-API-Key","fixture-action-key");
        async Task Change(string json) {using var response=await control.PostAsync(new Uri(baseUri,"test/change"),new StringContent(json,Encoding.UTF8,"application/json"));response.EnsureSuccessStatusCode();}
        async Task Reset() {using var response=await control.PostAsync(new Uri(baseUri,"test/reset"),new StringContent("{}"));response.EnsureSuccessStatusCode();}
        async Task<JsonElement> Stats() {using var doc=JsonDocument.Parse(await control.GetStringAsync(new Uri(baseUri,"test/stats")));return doc.RootElement.Clone();}
        await Reset();var backups=new Backups();
        var selection=new PostmanSelection(new("workspace-actions","Fixture"),new("123-actions","Action Fixture"),[],[],DateTimeOffset.UtcNow);
        using var backend=new PostmanActionBackend("fixture-action-key",selection,backups,baseUri);
        var list=await backend.ListAsync(default);Assert.Contains(list,x=>x.Id=="template");
        await Assert.ThrowsAsync<ArgumentException>(()=>backend.ReadAsync("outside",default));
        var read=await backend.ReadAsync("template",default);Assert.DoesNotContain("SENSITIVE-APP-KEY",read);
        var run=await backend.PrepareRunAsync("template",default);Assert.DoesNotContain("SENSITIVE-APP-KEY",run.After);Assert.Contains("127.0.0.1",run.After);
        Assert.Equal(0,(await Stats()).GetProperty("runs").GetInt32());
        var response=await backend.ApplyAsync(run,default);Assert.Equal(200,response.HttpStatus);Assert.DoesNotContain("SENSITIVE-APP-KEY",response.Body);Assert.Contains("REDACTED",response.Body);
        Assert.Equal(1,(await Stats()).GetProperty("runs").GetInt32());
        run=await backend.PrepareRunAsync("template",default);await Change("{\"variable\":\"CHANGED-KEY\"}");
        await Assert.ThrowsAsync<PostmanReviewChangedException>(()=>backend.ApplyAsync(run,default));Assert.Equal(1,(await Stats()).GetProperty("runs").GetInt32());
        await Reset();
        var folder=await backend.PrepareChangeAsync("create_folder","New Folder","","","",null,null,default);
        var saved=await backend.ApplyAsync(folder,default);Assert.Equal("saved",saved.Status);Assert.Equal(1,backups.Count);
        var create=await backend.PrepareChangeAsync("create_request","Example","","template",saved.ResourceId!,"{\"spc\":\"NEW\"}","{\"value\":2}",default);
        var created=await backend.ApplyAsync(create,default);Assert.Equal("saved",created.Status);
        var edit=await backend.PrepareChangeAsync("edit_request","Renamed",created.ResourceId!,"","","{\"spc\":\"EDITED\"}","{\"value\":3}",default);
        await backend.ApplyAsync(edit,default);Assert.Contains("Renamed",await backend.ReadAsync(created.ResourceId!,default));
        edit=await backend.PrepareChangeAsync("edit_request","Again",created.ResourceId!,"","",null,null,default);backups.Fail=true;
        var backupFailure=await Assert.ThrowsAsync<PostmanWriteStageException>(()=>backend.ApplyAsync(edit,default));
        Assert.Equal("private backup",backupFailure.Stage); Assert.False(backupFailure.SubmissionStarted);
        Assert.Equal(3,(await Stats()).GetProperty("writes").GetInt32());backups.Fail=false;
        await Reset(); await Change("{\\"rejectNextWrite\\":400}");
        var rejectedPlan=await backend.PrepareChangeAsync("create_folder","Rejected Folder","","","",null,null,default);
        var apiFailure=await Assert.ThrowsAsync<PostmanWriteStageException>(()=>backend.ApplyAsync(rejectedPlan,default));
        Assert.Equal("Postman write submission",apiFailure.Stage); Assert.True(apiFailure.SubmissionStarted);
        Assert.Contains("HTTP 400",apiFailure.Message); Assert.DoesNotContain("SECRET-SERVER-BODY",apiFailure.Message);
        Assert.Equal(0,(await Stats()).GetProperty("writes").GetInt32());
        await Assert.ThrowsAsync<ArgumentException>(()=>backend.PrepareChangeAsync("edit_request","",created.ResourceId!,"","","{\"Authorization\":\"plaintext\"}",null,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>backend.PrepareChangeAsync("edit_request","",created.ResourceId!,"","","{\"Host\":\"evil\"}",null,default));
        await Change("{\"script\":true}");await Assert.ThrowsAsync<ArgumentException>(()=>backend.PrepareRunAsync("template",default));
        await Reset();await Change("{\"route\":\"redirect\"}");run=await backend.PrepareRunAsync("template",default);response=await backend.ApplyAsync(run,default);
        Assert.Equal(302,response.HttpStatus);Assert.Equal(1,(await Stats()).GetProperty("runs").GetInt32());
        await Reset();await Change("{\"route\":\"large\"}");run=await backend.PrepareRunAsync("template",default);response=await backend.ApplyAsync(run,default);Assert.False(response.Truncated);Assert.True(response.Body!.Length>200000);
        using var capturedWorkflow = new PostmanWorkflow(backend, (_, approve, _) => approve(true));
        var stagedLarge = await capturedWorkflow.PrepareRunAsync("template", default);
        var capturedLarge = await capturedWorkflow.ApplyAsync(stagedLarge.RequestId, true, default);
        Assert.True(capturedLarge.ReadCapturedResult); Assert.Null(capturedLarge.Body);
        var callsBeforePages = (await Stats()).GetProperty("runs").GetInt32();
        var bodyPages = new StringBuilder(); var position = 0;
        while (true)
        {
            var page = capturedWorkflow.Results.Read(capturedLarge.Capture!.ResultId, position, 32000, capturedLarge.Capture.Revision);
            bodyPages.Append(page.Json); if (page.NextOffset is not int next) break; position = next;
        }
        using (var resultDoc = JsonDocument.Parse(bodyPages.ToString()))
        using (var responseDoc = JsonDocument.Parse(resultDoc.RootElement.GetProperty("body").GetString()!))
            Assert.Equal(200000, responseDoc.RootElement.GetProperty("data").GetString()!.Length);
        Assert.Equal(callsBeforePages, (await Stats()).GetProperty("runs").GetInt32());

        await Reset();await Change("{\"route\":\"unauthorized\"}");run=await backend.PrepareRunAsync("template",default);response=await backend.ApplyAsync(run,default);Assert.Equal(401,response.HttpStatus);Assert.DoesNotContain("SENSITIVE-APP-KEY",response.Body);
        await Reset();await Change("{\"variable\":\"\"}");await Assert.ThrowsAsync<ArgumentException>(()=>backend.PrepareRunAsync("template",default));
        await Reset();await Change("{\"variable\":\"fixture-action-key\"}");await Assert.ThrowsAsync<ArgumentException>(()=>backend.PrepareRunAsync("template",default));
    }
    private sealed class Backups : IPostmanBackupStore
    { public int Count;public bool Fail;public Task SaveAsync(string json,CancellationToken token){if(Fail)throw new IOException("Simulated backup failure");Count++;return Task.CompletedTask;} }
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class FakeBackend : IPostmanActionBackend
    {
        public string CollectionName=>"Fixture";public int Applied;public bool Fail;
        public Task<PostmanItem[]> ListAsync(CancellationToken token)=>Task.FromResult<PostmanItem[]>([]);
        public Task<string> ReadAsync(string id,CancellationToken token)=>Task.FromResult("masked");
        public Task<PostmanPlan> PrepareChangeAsync(string action,string name,string requestId,string templateRequestId,string folderId,string? headers,string? body,CancellationToken token)=>Task.FromResult(new PostmanPlan(action,name,"rev","before","after","scope"));
        public Task<PostmanPlan> PrepareRunAsync(string id,CancellationToken token)=>Task.FromResult(new PostmanPlan("run","Example","rev","","masked","scope"));
        public Task<PostmanActionResult> ApplyAsync(PostmanPlan plan,CancellationToken token){Interlocked.Increment(ref Applied);if(Fail)throw new InvalidOperationException();return Task.FromResult(new PostmanActionResult("completed",HttpStatus:200));}
        public void Dispose(){}
    }
}
