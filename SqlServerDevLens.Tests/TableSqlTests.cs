using SqlServerDevLens.Core;
using Xunit;

namespace SqlServerDevLens.Tests;

public class TableSqlTests
{
    [Theory]
    [InlineData("CREATE TABLE dbo.Example(id int NOT NULL PRIMARY KEY, label nvarchar(50) NULL)", "table_create")]
    [InlineData("ALTER TABLE dbo.Example ADD extra int NULL", "table_alter")]
    [InlineData("ALTER TABLE dbo.Example ALTER COLUMN label nvarchar(100) NULL", "table_alter")]
    [InlineData("INSERT dbo.Example(id,label) VALUES(1,N'one'),(2,N'two')", "table_insert")]
    [InlineData("UPDATE dbo.Example SET label=N'updated' WHERE id=1", "table_update")]
    [InlineData("DELETE dbo.Example WHERE id=1", "table_delete")]
    public void AcceptsExactScopedOperations(string sql, string action) => Assert.Equal(action, TableSql.Validate("dbo", "Example", sql, 10).Action);

    [Theory]
    [InlineData("UPDATE dbo.Example SET label=N'all'")]
    [InlineData("UPDATE dbo.Other SET label=N'x' WHERE id=1")]
    [InlineData("UPDATE OtherDb.dbo.Example SET label=N'x' WHERE id=1")]
    [InlineData("UPDATE TOP(1) dbo.Example SET label=N'x' WHERE id>0")]
    [InlineData("UPDATE e SET label=N'x' FROM dbo.Example e WHERE id=1")]
    [InlineData("UPDATE dbo.Example SET id=(SELECT MAX(id) FROM dbo.Other) WHERE id=1")]
    [InlineData("UPDATE dbo.Example SET id=dbo.CustomFunction(1) WHERE id=1")]
    [InlineData("UPDATE dbo.Example SET id=NEXT VALUE FOR dbo.Seq WHERE id=1")]
    [InlineData("UPDATE dbo.Example SET id=@unapproved WHERE id=1")]
    [InlineData("UPDATE dbo.Example SET label=N'x' OUTPUT inserted.id WHERE id=1")]
    [InlineData("INSERT dbo.Example(id) SELECT id FROM dbo.Other")]
    [InlineData("INSERT dbo.Example EXEC dbo.Something")]
    [InlineData("DELETE dbo.Example")]
    [InlineData("DELETE TOP(1) dbo.Example WHERE id=1")]
    [InlineData("DELETE e FROM dbo.Example e WHERE id=1")]
    [InlineData("DELETE OtherDb.dbo.Example WHERE id=1")]
    [InlineData("DELETE dbo.Example OUTPUT deleted.id WHERE id=1")]
    [InlineData("DELETE dbo.Example WHERE id IN (SELECT id FROM dbo.Other)")]
    [InlineData("TRUNCATE TABLE dbo.Example")]
    [InlineData("DROP TABLE dbo.Example")]
    [InlineData("ALTER TABLE dbo.Example DROP COLUMN label")]
    [InlineData("CREATE TABLE dbo.Other(id int)")]
    [InlineData("CREATE TABLE dbo.Example(id int); DROP TABLE dbo.Other")]
    [InlineData("CREATE TABLE dbo.Example(id int)\nGO\nSELECT 1")]
    public void RejectsUnsupportedOrUnsafeStatements(string sql) => Assert.Throws<ArgumentException>(() => TableSql.Validate("dbo", "Example", sql, 10));

    [Fact]
    public void LiteralInsertCountCannotExceedLimit()
    {
        Assert.Throws<ArgumentException>(() => TableSql.Validate("dbo", "Example", "INSERT dbo.Example(id) VALUES(1),(2)", 1));
        Assert.Throws<ArgumentException>(() => TableSql.Validate("dbo", "Example", "UPDATE dbo.Example SET id=1 WHERE id=2", 501));
    }
    [Fact]
    public async Task TableActionsUseSameHumanApprovalAndCannotBeReplayedOrRunThroughProcedureTools()
    {
        var backend = new Backend(); Action<bool>? decide = null; ProcedureReview? shown = null;
        using var workflow = new ProcedureWorkflow(backend, (review, decision, _) => { shown = review; decide = decision; });
        const string sql = "UPDATE dbo.Example SET id=2 WHERE id=1";
        var request = await workflow.PrepareTableAsync("dbo", "Example", sql, 1, default);
        Assert.Equal("table", shown!.TargetKind); Assert.Contains(sql, shown.Diff); Assert.Equal("pending", request.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyTableAsync(request.RequestId, default));
        decide!(true);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ApplyAsync(request.RequestId, default));
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(request.RequestId, default));
        Assert.Equal("committed", (await workflow.ApplyTableAsync(request.RequestId, default)).Status);
        Assert.Equal(1, backend.Calls);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ApplyTableAsync(request.RequestId, default));
        var stopped = await workflow.PrepareTableAsync("dbo", "Example", sql, 1, default); decide(true); workflow.Dispose();
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ApplyTableAsync(stopped.RequestId, default));
    }
    private sealed class Backend : IProcedureBackend, ITableActionBackend
    {
        public string Database => "FakeDatabase"; public int Calls;
        public Task<TableSqlSnapshot> PrepareTableAsync(TableSqlPlan plan, CancellationToken token) => Task.FromResult(new TableSqlSnapshot(plan,"revision","fake schema metadata",1));
        public Task<TableSqlResult> ApplyTableAsync(TableSqlSnapshot snapshot, CancellationToken token) { Calls++; return Task.FromResult(new TableSqlResult("committed",1,false)); }
        public Task<ProcedureDefinition?> ReadAsync(string schema, string name, CancellationToken token) => throw new NotSupportedException();
        public void ValidateParameters(ProcedureDefinition definition, string parametersJson) => throw new NotSupportedException();
        public Task<ProcedureExecution> ExecuteAsync(ProcedureDefinition definition, string parametersJson, CancellationToken token) => throw new NotSupportedException();
        public Task<ProcedureApplyResult> ApplyAsync(string schema, string name, string sql, ProcedureDefinition? previous, CancellationToken token) => throw new NotSupportedException();
    }
}
