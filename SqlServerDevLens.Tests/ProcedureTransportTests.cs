using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class ProcedureTransportTests
{
    [Fact]
    public async Task RealStdioCannotApproveOrReplayAndLongOperationSurvivesHandshakeBudget()
    {
        var pipe = "dvt-" + Guid.NewGuid().ToString("N");
        Action<bool>? decision = null;
        var backend = new Backend();
        var workflow = new ProcedureWorkflow(backend, (_, approve, _) => decision = approve);
        await using var server = new DesktopMcpServer(pipe);
        await server.StartAsync(DateTimeOffset.UtcNow, new FakeDatabase(workflow));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SqlServerDevLens.slnx"))) root = root.Parent;
        var dll = Path.Combine(root!.FullName, "SqlServerDevLens.McpHost", "bin", "Release", "net10.0", "SqlServerDevLens.McpHost.dll");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = "dotnet", Arguments = [dll, "--desktop", "--pipe-name", pipe] }), cancellationToken: timeout.Token);
        var first = await client.CallToolAsync("get_procedure_definition", new Dictionary<string, object?>
        { ["schema"] = "dbo", ["name"] = "Example", ["maxCharacters"] = 1000 }, cancellationToken: timeout.Token);
        Assert.False(first.IsError ?? false);
        using var page = JsonDocument.Parse(Assert.IsType<TextContentBlock>(first.Content[0]).Text);
        Assert.False(page.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(1000, page.RootElement.GetProperty("nextOffset").GetInt32());
        var next = await client.CallToolAsync("get_procedure_definition", new Dictionary<string, object?>
        { ["schema"] = "dbo", ["name"] = "Example", ["offset"] = 1000,
            ["expectedRevision"] = page.RootElement.GetProperty("revision").GetString() }, cancellationToken: timeout.Token);
        Assert.False(next.IsError ?? false);
        var stalePage = await client.CallToolAsync("get_procedure_definition", new Dictionary<string, object?>
        { ["schema"] = "dbo", ["name"] = "Example", ["offset"] = 1000, ["expectedRevision"] = "stale" }, cancellationToken: timeout.Token);
        Assert.True(stalePage.IsError);
        var staged = await client.CallToolAsync("prepare_procedure_execution", new Dictionary<string, object?>
        { ["schema"] = "dbo", ["name"] = "Example", ["parametersJson"] = "{}" }, cancellationToken: timeout.Token);
        Assert.False(staged.IsError ?? false);
        using var parsed = JsonDocument.Parse(Assert.IsType<TextContentBlock>(staged.Content[0]).Text);
        var id = parsed.RootElement.GetProperty("requestId").GetString();
        var arguments = new Dictionary<string, object?> { ["requestId"] = id, ["approved"] = true };
        var unapproved = await client.CallToolAsync("execute_prepared_procedure", arguments, cancellationToken: timeout.Token);
        Assert.True(unapproved.IsError); Assert.Equal(0, backend.Calls);
        decision!(true);
        arguments.Remove("approved");
        var executed = await client.CallToolAsync("execute_prepared_procedure", arguments, cancellationToken: timeout.Token);
        Assert.False(executed.IsError ?? false); Assert.Equal(1, backend.Calls);
        var replay = await client.CallToolAsync("execute_prepared_procedure", arguments, cancellationToken: timeout.Token);
        Assert.True(replay.IsError); Assert.Equal(1, backend.Calls);
        var change = await client.CallToolAsync("prepare_procedure_change", new Dictionary<string, object?>
        { ["schema"] = "dbo", ["name"] = "Example", ["sql"] = "ALTER PROC dbo.Example AS SELECT 2" }, cancellationToken: timeout.Token);
        Assert.False(change.IsError ?? false);
        using var changeJson = JsonDocument.Parse(Assert.IsType<TextContentBlock>(change.Content[0]).Text);
        var changeId = changeJson.RootElement.GetProperty("requestId").GetString();
        decision(true);
        var wrongAction = await client.CallToolAsync("apply_table_sql", new Dictionary<string, object?> { ["requestId"] = changeId }, cancellationToken: timeout.Token);
        Assert.True(wrongAction.IsError);
        var table = await client.CallToolAsync("prepare_table_sql", new Dictionary<string, object?>
        { ["schema"] = "dbo", ["name"] = "Example", ["sql"] = "INSERT dbo.Example(id) VALUES(1)", ["maxAffectedRows"] = 1 }, cancellationToken: timeout.Token);
        Assert.False(table.IsError ?? false);
        using var tableJson = JsonDocument.Parse(Assert.IsType<TextContentBlock>(table.Content[0]).Text);
        var tableArgs = new Dictionary<string, object?> { ["requestId"] = tableJson.RootElement.GetProperty("requestId").GetString(), ["approved"] = true };
        var tableDenied = await client.CallToolAsync("apply_table_sql", tableArgs, cancellationToken: timeout.Token);
        Assert.True(tableDenied.IsError); Assert.Equal(0, backend.TableCalls);
        decision(true); tableArgs.Remove("approved");
        Assert.False((await client.CallToolAsync("apply_table_sql", tableArgs, cancellationToken: timeout.Token)).IsError ?? false);
        Assert.True((await client.CallToolAsync("apply_table_sql", tableArgs, cancellationToken: timeout.Token)).IsError);
        Assert.Equal(1, backend.TableCalls);
        await server.StopAsync();
        await server.StartAsync(DateTimeOffset.UtcNow, new FakeDatabase(new ProcedureWorkflow(new Backend(), (_, approve, _) => approve(true))));
        var old = await client.CallToolAsync("apply_procedure_change", new Dictionary<string, object?> { ["requestId"] = changeId }, cancellationToken: timeout.Token);
        Assert.True(old.IsError);
    }
    private sealed class FakeDatabase(ProcedureWorkflow procedures) : ISqlDatabase
    {
        public string Database => "FakeDatabase";
        public ProcedureWorkflow Procedures => procedures;
        public void Dispose() => procedures.Dispose();
        public Task<SqlResult> GetDatabaseInfoAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SqlResult> ListObjectsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SqlResult> DescribeObjectAsync(string schema, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SqlResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Backend : IProcedureBackend, ITableActionBackend
    {
        public string Database => "FakeDatabase"; public int Calls; public int TableCalls;
        public Task<TableSqlSnapshot> PrepareTableAsync(TableSqlPlan plan, CancellationToken token) => Task.FromResult(new TableSqlSnapshot(plan,"revision","fake schema",1));
        public Task<TableSqlResult> ApplyTableAsync(TableSqlSnapshot snapshot, CancellationToken token) { TableCalls++; return Task.FromResult(new TableSqlResult("committed",1,false)); }
        public Task<ProcedureDefinition?> ReadAsync(string schema, string name, CancellationToken token) =>
            Task.FromResult<ProcedureDefinition?>(new(schema, name, "CREATE PROC dbo.Example AS /*" + new string('x', 160000) + "*/ SELECT 1", "fake-revision", true, true, []));
        public void ValidateParameters(ProcedureDefinition definition, string parametersJson) => ProcedureSql.Parameters(parametersJson);
        public async Task<ProcedureExecution> ExecuteAsync(ProcedureDefinition definition, string parametersJson, CancellationToken token)
        { Calls++; await Task.Delay(TimeSpan.FromSeconds(6), token); return new([], new Dictionary<string, object?>(), 0, true, "Finished"); }
        public Task<ProcedureApplyResult> ApplyAsync(string schema, string name, string sql, ProcedureDefinition? previous, CancellationToken token) =>
            Task.FromResult(new ProcedureApplyResult("applied", true));
    }
}
