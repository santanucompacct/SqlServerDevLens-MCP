using System.Data;
using System.Security.AccessControl;
using System.Security.Principal;
using SqlServerDevLens.Core;
using Xunit;

namespace SqlServerDevLens.Tests;

public class ProcedureTests
{
    [Theory]
    [InlineData("CREATE PROCEDURE dbo.Example AS SELECT 1", true)]
    [InlineData("ALTER PROC [dbo].[Example] @n int=1 AS BEGIN SELECT @n; END", false)]
    public void OnlyExactProcedureDdlIsAccepted(string sql, bool create) => ProcedureSql.ValidateChange("dbo", "Example", sql, create);

    [Theory]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.Example AS SELECT 1", true)]
    [InlineData("CREATE PROCEDURE other.Example AS SELECT 1", true)]
    [InlineData("CREATE PROCEDURE dbo.Other AS SELECT 1", true)]
    [InlineData("CREATE PROCEDURE AnotherDb.dbo.Example AS SELECT 1", true)]
    [InlineData("CREATE PROCEDURE dbo.Example WITH ENCRYPTION AS SELECT 1", true)]
    [InlineData("CREATE PROCEDURE dbo.Example;2 AS SELECT 1", true)]
    [InlineData("CREATE PROCEDURE dbo.Example AS SELECT 1\nGO\nDROP TABLE dbo.T", true)]
    [InlineData("DROP PROCEDURE dbo.Example", false)]
    [InlineData("CREATE PROCEDURE dbo.Example AS SELECT 1", false)]
    public void RejectsWrongTargetsModesAndBatches(string sql, bool create) => Assert.Throws<ArgumentException>(() => ProcedureSql.ValidateChange("dbo", "Example", sql, create));

    private static ProcedureDefinition Definition(params ProcedureParameterInfo[] parameters) => new("dbo", "Example", "CREATE PROC dbo.Example AS SELECT 1", "r1", true, true, parameters);
    [Fact]
    public void ParametersAreTypedAndNeverConcatenated()
    {
        var definition = Definition(new("@id", "bigint", 8, 19, 0, false, false), new("@label", "nvarchar", 100, 0, 0, false, true), new("@out", "int", 4, 10, 0, true, false));
        var bound = SqlProcedureBackend.Bind(definition, "{\"id\":42,\"label\":\"x'; DROP TABLE dbo.T;--\"}");
        Assert.Equal(SqlDbType.BigInt, bound[0].SqlDbType); Assert.Equal(42L, bound[0].Value);
        Assert.Equal("x'; DROP TABLE dbo.T;--", bound[1].Value);
        Assert.Equal(ParameterDirection.InputOutput, bound[2].Direction);
        Assert.Equal(DBNull.Value, bound[2].Value);
        Assert.Equal(2, SqlProcedureBackend.Bind(definition, "{\"id\":42}").Count);
    }
    [Theory]
    [InlineData("{\"id\":1,\"@id\":2}")]
    [InlineData("{\"id\":{\"unexpected\":1}}")]
    [InlineData("{\"id\":\"1; DROP TABLE dbo.T\"}")]
    [InlineData("{\"other\":1}")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void InvalidParametersAreRejected(string json) => Assert.Throws<ArgumentException>(() =>
        SqlProcedureBackend.Bind(Definition(new ProcedureParameterInfo("@id", "int", 4, 10, 0, false, false)), json));

    [Fact]
    public async Task ApprovalIsExplicitSingleUseAndCannotBeReplacedByAnotherAction()
    {
        var backend = new FakeBackend(); Action<bool>? decision = null;
        using var workflow = new ProcedureWorkflow(backend, (_, decide, _) => decision = decide);
        var request = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        Assert.Equal("pending", request.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(request.RequestId, default));
        Assert.Equal(0, backend.Executed);
        decision!(true);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ApplyAsync(request.RequestId, default));
        await workflow.ExecuteAsync(request.RequestId, default);
        Assert.Equal(1, backend.Executed);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(request.RequestId, default));
    }
    [Fact]
    public async Task DenialExpiryStopAndMissingDesktopFailClosed()
    {
        var backend = new FakeBackend(); var clock = new TestClock(); Action<bool>? decide = null;
        using var workflow = new ProcedureWorkflow(backend, (_, decision, _) => decide = decision, clock);
        var request = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        decide!(false); decide(true); // A second decision cannot override rejection.
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(request.RequestId, default));
        var expiry = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        decide(true); clock.Now = clock.Now.AddMinutes(6);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(expiry.RequestId, default));
        var stopped = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        workflow.Dispose(); decide(true);
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(stopped.RequestId, default));
        using var noDesktop = new ProcedureWorkflow(backend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noDesktop.PrepareExecutionAsync("dbo", "Example", "{}", default));
        Assert.Equal(0, backend.Executed);
    }
    [Fact]
    public async Task ExactDiffAndSqlAreFrozenAtApprovalAndFailuresAreNotReplayed()
    {
        var backend = new FakeBackend(); ProcedureReview? shown = null;
        using var workflow = new ProcedureWorkflow(backend, (review, decision, _) => { shown = review; decision(true); });
        var sql = "ALTER PROC dbo.Example AS SELECT 2";
        var request = await workflow.PrepareChangeAsync("dbo", "Example", sql, false, default);
        Assert.Equal(sql, shown!.Proposed); Assert.Contains("+ALTER PROC dbo.Example AS SELECT 2", shown.Diff);
        backend.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(request.RequestId, default));
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ApplyAsync(request.RequestId, default));
        Assert.Equal(sql, backend.Sql);
    }
    [Fact]
    public async Task ParallelConsumersCannotExecuteTwice()
    {
        var backend = new FakeBackend(); using var workflow = new ProcedureWorkflow(backend, (_, d, _) => d(true));
        var request = await workflow.PrepareExecutionAsync("dbo", "Example", "{}", default);
        var errors = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Record.ExceptionAsync(() => workflow.ExecuteAsync(request.RequestId, default))));
        Assert.Single(errors, e => e is null); Assert.Equal(1, backend.Executed);
    }
    [Fact]
    public async Task PrivateBackupIsCompleteAndOwnerRestricted()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevLensTestBackups", Guid.NewGuid().ToString("N"));
        try
        {
            await new PrivateProcedureBackupStore(root).SaveAsync("FakeDatabase", Definition(), default);
            var file = Assert.Single(Directory.GetFiles(root));
            Assert.Contains(Definition().Definition, await File.ReadAllTextAsync(file));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
            }
            else
            {
                var acl = new DirectoryInfo(root).GetAccessControl();
                Assert.True(acl.AreAccessRulesProtected);
                var owner = WindowsIdentity.GetCurrent().User;
                foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                    Assert.Equal(owner, rule.IdentityReference);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class TestClock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FakeBackend : IProcedureBackend
    {
        public string Database => "FakeDatabase"; public int Executed; public bool Fail; public string? Sql;
        public Task<ProcedureDefinition?> ReadAsync(string schema, string name, CancellationToken token) => Task.FromResult<ProcedureDefinition?>(Definition());
        public void ValidateParameters(ProcedureDefinition definition, string parametersJson) => SqlProcedureBackend.Bind(definition, parametersJson);
        public Task<ProcedureExecution> ExecuteAsync(ProcedureDefinition definition, string parametersJson, CancellationToken token)
        { Interlocked.Increment(ref Executed); return Task.FromResult(new ProcedureExecution([], new Dictionary<string, object?>(), 0, true, "Finished")); }
        public Task<ProcedureApplyResult> ApplyAsync(string schema, string name, string sql, ProcedureDefinition? previous, CancellationToken token)
        { Sql = sql; if (Fail) throw new InvalidOperationException("Simulated database failure"); return Task.FromResult(new ProcedureApplyResult("applied", true)); }
    }
}
