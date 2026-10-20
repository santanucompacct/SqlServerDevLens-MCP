using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Core;
using SqlServerDevLens.Desktop.ViewModels;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class LargeResultsAuthorizationTests
{
    private static readonly string LargeText = JsonSerializer.Serialize(new { data = new string('x', 350000), unicode = "😀漢字\n\"tail" });
    [Fact]
    public async Task FullResultsPagesAreLosslessAndNeverExecuteAgain()
    {
        var backend = new Backend();
        using var workflow = new ProcedureWorkflow(backend, (_, decide, _) => decide(true));
        var request = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        var result = await workflow.ExecuteAsync(request.RequestId, default);
        Assert.True(result.Completed); Assert.True(result.ReadCapturedResult); Assert.Empty(result.ResultSets);
        var reference = Assert.IsType<CapturedResultReference>(result.Capture);
        var json = ReadAll(workflow.Results, reference);
        using var document = JsonDocument.Parse(json);
        var sets = document.RootElement.GetProperty("resultSets");
        Assert.Equal(12, sets.GetArrayLength()); Assert.Equal(650, sets[0].GetProperty("rows").GetArrayLength());
        Assert.Equal(LargeText, sets[0].GetProperty("rows")[0][0].GetString());
        Assert.Equal(new string('o', 130000), document.RootElement.GetProperty("outputParameters").GetProperty("@output").GetString());
        Assert.Equal(9, document.RootElement.GetProperty("returnCode").GetInt32());
        Assert.Equal(json, ReadAll(workflow.Results, reference)); Assert.Equal(1, backend.Executions);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(request.RequestId, default));
        workflow.Results.Release(reference.ResultId);
        Assert.Throws<ArgumentException>(() => workflow.Results.Read(reference.ResultId));
    }
    private static string ReadAll(CapturedResults store, CapturedResultReference reference)
    {
        var result = new StringBuilder(); var offset = 0;
        while (true)
        {
            var page = store.Read(reference.ResultId, offset, 32000, offset == 0 ? null : reference.Revision);
            result.Append(page.Json);
            if (page.NextOffset is not int next) { Assert.True(page.Complete); break; }
            Assert.True(next > offset); offset = next;
        }
        return result.ToString();
    }
    [Fact]
    public void CapturesExpireAreSessionScopedAndReleaseOnStop()
    {
        var clock = new Clock(); using var store = new CapturedResults(clock);
        using var reserve = store.Reserve(); var reference = reserve.Save(new { value = LargeText });
        Assert.Throws<ArgumentException>(() => store.Read(reference.ResultId, 256));
        Assert.Throws<InvalidOperationException>(() => store.Read(reference.ResultId, 0, 256, "wrong"));
        using var otherSession = new CapturedResults();
        Assert.Throws<ArgumentException>(() => otherSession.Read(reference.ResultId));
        clock.Now = clock.Now.AddMinutes(16);
        Assert.Throws<ArgumentException>(() => store.Read(reference.ResultId));
        store.Dispose(); Assert.Throws<InvalidOperationException>(() => store.Read(reference.ResultId));
    }
    [Fact]
    public async Task FullCaptureMemoryFailsBeforeExecutingAndBudgetNeverSilentlyClips()
    {
        using var store = new CapturedResults();
        using var a = store.Reserve(); using var b = store.Reserve(); using var c = store.Reserve(); using var d = store.Reserve();
        var backend = new Backend(); using var workflow = new ProcedureWorkflow(backend, (_, decide, _) => decide(true), results: store);
        var pending = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(pending.RequestId, default));
        Assert.Equal(0, backend.Executions); Assert.Equal("approved", workflow.Status(pending.RequestId).Status);
        var budget = new ResultReadBudget(); budget.Add(CapturedResults.MaxCaptureCharacters);
        Assert.Throws<InvalidOperationException>(() => budget.Add(1));
    }
    [Fact]
    public async Task AutomaticAuthorizationIsLocalScopedRevocableAndSingleUse()
    {
        var policy = new SessionAuthorization(); Assert.All(policy.Snapshot().Values, v => Assert.False(v));
        var backend = new Backend(); var reviews = 0;
        using var workflow = new ProcedureWorkflow(backend, (_, _, _) => reviews++, authorization: policy);
        policy.Set("procedure_execute", true);
        var run = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        Assert.Equal("approved", run.Status); Assert.Equal("session_policy", run.Authorization); Assert.Equal(0, reviews);
        var alter = await workflow.PrepareChangeAsync("dbo", "Example", "ALTER PROC dbo.Example AS SELECT 2", false, default);
        Assert.Equal("pending", alter.Status); Assert.Equal(1, reviews);
        policy.Set("procedure_execute", false); policy.Set("procedure_execute", true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(run.RequestId, default));
        Assert.Equal(0, backend.Executions);
        run = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        await workflow.ExecuteAsync(run.RequestId, default); Assert.Equal(1, backend.Executions);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(run.RequestId, default));
        policy.Reset(); Assert.All(policy.Snapshot().Values, v => Assert.False(v));
        Assert.Throws<ArgumentException>(() => policy.Set("anything_else", true));
    }
    [Fact]
    public async Task PostmanRunAuthorizationDoesNotAuthorizeChangesAndCanBeRevoked()
    {
        var policy = new SessionAuthorization(); var backend = new PostmanBackend();
        using var workflow = new PostmanWorkflow(backend, (_, _, _) => { }, authorization: policy);
        policy.Set("postman_run", true);
        var pending = await workflow.PrepareRunAsync("request", default);
        Assert.Equal("session_policy", pending.Authorization);
        var change = await workflow.PrepareChangeAsync("edit_request", "name", "request", "", "", null, null, default);
        Assert.Equal("pending", change.Status);
        policy.Set("postman_run", false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(pending.RequestId, true, default));
        Assert.Equal(0, backend.Executions);
        policy.Set("postman_run", true); pending = await workflow.PrepareRunAsync("request", default);
        var result = await workflow.ApplyAsync(pending.RequestId, true, default);
        Assert.True(result.ReadCapturedResult); Assert.Null(result.Body);
        using var captured = JsonDocument.Parse(ReadAll(workflow.Results, result.Capture!));
        Assert.Equal(LargeText, captured.RootElement.GetProperty("body").GetString()); Assert.Equal(1, backend.Executions);
    }
    [Fact]
    public async Task DesktopCheckboxesResetOnStop()
    {
        await using var server = new DesktopMcpServer("dva-" + Guid.NewGuid().ToString("N"));
        var model = new ServerViewModel(server) { AutoProcedureExecution = true, AutoDelete = true, AutoPostmanRun = true };
        Assert.True(model.Authorization.Snapshot()["table_delete"]);
        await model.StopAsync();
        Assert.False(model.AutoProcedureExecution); Assert.False(model.AutoDelete); Assert.False(model.AutoPostmanRun);
        Assert.All(model.Authorization.Snapshot().Values, v => Assert.False(v));
    }
    [Fact]
    public async Task NewResultToolsAreForwardedThroughRealStdioWithoutLiveSql()
    {
        var name = "dvc-" + Guid.NewGuid().ToString("N");
        using var data = new Data(); using var reservation = data.Results.Reserve();
        var reference = reservation.Save(new { payload = LargeText });
        await using var server = new DesktopMcpServer(name); await server.StartAsync(DateTimeOffset.UtcNow, data);
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../SqlServerDevLens.McpHost/bin/Release/net10.0/SqlServerDevLens.McpHost.dll"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new() { Command = "dotnet", Arguments = [dll,"--desktop","--pipe-name",name] }), cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Contains(tools, t => t.Name == "read_result_page"); Assert.DoesNotContain(tools, t => t.Name.Contains("set_authorization"));
        var result = new StringBuilder(); var offset = 0;
        while (true)
        {
            var reply = await client.CallToolAsync("read_result_page", new Dictionary<string, object?> { ["resultId"] = reference.ResultId,
                ["offset"] = offset, ["maxCharacters"] = 128000, ["expectedRevision"] = reference.Revision }, cancellationToken: timeout.Token);
            Assert.NotEqual(true, reply.IsError);
            using var page = JsonDocument.Parse(((TextContentBlock)reply.Content[0]).Text);
            result.Append(page.RootElement.GetProperty("json").GetString());
            if (page.RootElement.GetProperty("complete").GetBoolean()) break;
            offset = page.RootElement.GetProperty("nextOffset").GetInt32();
        }
        using var document = JsonDocument.Parse(result.ToString()); Assert.Equal(LargeText, document.RootElement.GetProperty("payload").GetString());
        var release = await client.CallToolAsync("release_result", new Dictionary<string, object?> { ["resultId"] = reference.ResultId }, cancellationToken: timeout.Token);
        Assert.NotEqual(true, release.IsError);
        Assert.Throws<ArgumentException>(() => data.Results.Read(reference.ResultId));
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Backend : IProcedureBackend
    {
        public string Database => "Synthetic"; public int Executions;
        public Task<ProcedureDefinition?> ReadAsync(string schema, string name, CancellationToken token) => Task.FromResult<ProcedureDefinition?>(new(schema,name,"CREATE PROC dbo.Example AS SELECT 1","r1",true,true,[]));
        public void ValidateParameters(ProcedureDefinition d, string p) { }
        public Task<ProcedureExecution> ExecuteAsync(ProcedureDefinition d, string p, CancellationToken token)
        {
            Executions++;
            var sets = Enumerable.Range(0,12).Select(i => new SqlResult(Database,[new("json","nvarchar")],
                i == 0 ? Enumerable.Range(0,650).Select(row => new object?[] { row == 0 ? LargeText : "row" }).ToArray() : [new object?[] { "extra set" }],false,false)).ToArray();
            return Task.FromResult(new ProcedureExecution(sets,new Dictionary<string,object?> { ["@output"] = new string('o',130000) },9,true,"completed"));
        }
        public Task<ProcedureApplyResult> ApplyAsync(string schema,string name,string sql,ProcedureDefinition? previous,CancellationToken token) => Task.FromResult(new ProcedureApplyResult("applied",true));
    }
    private sealed class PostmanBackend : IPostmanActionBackend
    {
        public string CollectionName => "Synthetic"; public int Executions;
        public Task<PostmanItem[]> ListAsync(CancellationToken token) => Task.FromResult(Array.Empty<PostmanItem>());
        public Task<string> ReadAsync(string id,CancellationToken token) => Task.FromResult("{}");
        public Task<PostmanPlan> PrepareRunAsync(string id,CancellationToken token) => Task.FromResult(new PostmanPlan("run","Synthetic","r1","","{}","fixture"));
        public Task<PostmanPlan> PrepareChangeAsync(string action,string name,string id,string template,string folder,string? headers,string? body,CancellationToken token) => Task.FromResult(new PostmanPlan(action,name,"r1","","{}","fixture"));
        public Task<PostmanActionResult> ApplyAsync(PostmanPlan plan,CancellationToken token) { Executions++; return Task.FromResult(new PostmanActionResult("completed",Body:LargeText)); }
        public void Dispose() { }
    }
    private sealed class Data : ISqlDatabase
    {
        public string Database => "Synthetic"; public CapturedResults Results { get; } = new();
        public Task<SqlResult> GetDatabaseInfoAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SqlResult> ListObjectsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SqlResult> DescribeObjectAsync(string schema,string name,CancellationToken token) => throw new NotSupportedException();
        public Task<SqlResult> QueryAsync(string sql,int maxRows,CancellationToken token) => throw new NotSupportedException();
        public void Dispose() => Results.Dispose();
    }
}
