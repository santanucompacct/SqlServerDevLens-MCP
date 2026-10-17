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
    private sealed class Backend : IProcedureBackend
    {
        public string Database => "FakeDatabase"; public int Calls;
        public Task<ProcedureDefinition?> ReadAsync(string schema, string name, CancellationToken token) =>
            Task.FromResult<ProcedureDefinition?>(new(schema, name, "CREATE PROC dbo.Example AS SELECT 1", "fake-revision", true, true, []));
        public void ValidateParameters(ProcedureDefinition definition, string parametersJson) => ProcedureSql.Parameters(parametersJson);
        public async Task<ProcedureExecution> ExecuteAsync(ProcedureDefinition definition, string parametersJson, CancellationToken token)
        { Calls++; await Task.Delay(TimeSpan.FromSeconds(6), token); return new([], new Dictionary<string, object?>(), 0, true, "Finished"); }
        public Task<ProcedureApplyResult> ApplyAsync(string schema, string name, string sql, ProcedureDefinition? previous, CancellationToken token) =>
            Task.FromResult(new ProcedureApplyResult("applied", true));
    }
}
