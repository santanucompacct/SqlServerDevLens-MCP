using Microsoft.Data.SqlClient;
using SqlServerDevLens.Core;
using Xunit;

namespace SqlServerDevLens.Tests;

public class ProcedureServerFactAttribute : FactAttribute
{
    public ProcedureServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DEVLENS_PROCEDURE_TEST_CONNECTION_STRING") is null ||
            Environment.GetEnvironmentVariable("DEVLENS_PROCEDURE_TEST_ALLOW_DATABASE_CREATE") != "1")
            Skip = "Requires explicit opt-in to a disposable localhost SQL Server. Never uses a saved production profile.";
    }
}

public class DisposableProcedureIntegrationTests
{
    [ProcedureServerFact]
    public async Task RealCreateExecuteAlterBackupRevisionAndTruncation()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("DEVLENS_PROCEDURE_TEST_CONNECTION_STRING"));
        Assert.Contains(builder.DataSource, new[] { "127.0.0.1,1433", "localhost,1433" });
        builder.InitialCatalog = "master"; builder.Pooling = false;
        var name = "DevLensTest_" + Guid.NewGuid().ToString("N");
        var backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevLensTestBackups", name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var admin = new SqlConnection(builder.ConnectionString);
        for (var attempt = 0; ; attempt++)
        {
            try { await admin.OpenAsync(timeout.Token); break; }
            catch (SqlException) when (attempt < 40) { await Task.Delay(1500, timeout.Token); }
        }
        await Command(admin, $"CREATE DATABASE [{name}]", timeout.Token);
        try
        {
            builder.InitialCatalog = name;
            var backend = new SqlProcedureBackend(name, () => builder.ConnectionString, new PrivateProcedureBackupStore(backupPath));
            Action<bool>? decision = null;
            using var workflow = new ProcedureWorkflow(backend, (_, approve, _) => decision = approve);
            const string create = "CREATE PROCEDURE dbo.Example @value int, @label nvarchar(50)=N'default', @output int OUTPUT AS BEGIN SET NOCOUNT ON; SELECT @value AS value,@label AS label; SELECT @value+1 AS second_value; SET @output=@value*2; RETURN 7; END";
            var pending = await workflow.PrepareChangeAsync("dbo", "Example", create, true, timeout.Token);
            Assert.Null(await backend.ReadAsync("dbo", "Example", timeout.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(pending.RequestId, timeout.Token));
            decision!(false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(pending.RequestId, timeout.Token));
            var approved = await workflow.PrepareChangeAsync("dbo", "Example", create, true, timeout.Token); decision(true);
            Assert.False((await workflow.ApplyAsync(approved.RequestId, timeout.Token)).BackupSaved);
            var definition = await workflow.ReadAsync("dbo", "Example", timeout.Token);
            Assert.Equal(create, definition.Definition);
            Assert.True(definition.Parameters.Single(p => p.Name == "@label").HasDefault);
            var execution = await workflow.PrepareExecutionAsync("dbo", "Example", "{\"value\":4}", timeout.Token); decision(true);
            var result = await workflow.ExecuteAsync(execution.RequestId, timeout.Token);
            Assert.True(result.Completed); Assert.Equal(2, result.ResultSets.Count);
            Assert.Equal(4, result.ResultSets[0].Rows[0][0]); Assert.Equal("default", result.ResultSets[0].Rows[0][1]);
            Assert.Equal(8, result.OutputParameters["@output"]); Assert.Equal(7, result.ReturnCode);
            await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(execution.RequestId, timeout.Token));
            var alter = create.Replace("CREATE PROCEDURE", "ALTER PROCEDURE").Replace("@value*2", "@value*3");
            var update = await workflow.PrepareChangeAsync("dbo", "Example", alter, false, timeout.Token); decision(true);
            Assert.True((await workflow.ApplyAsync(update.RequestId, timeout.Token)).BackupSaved);
            Assert.Contains(create, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(backupPath)), timeout.Token));
            var stale = await workflow.PrepareExecutionAsync("dbo", "Example", "{\"value\":1}", timeout.Token); decision(true);
            await using var db = new SqlConnection(builder.ConnectionString); await db.OpenAsync(timeout.Token);
            await Command(db, alter.Replace("@value*3", "@value*4"), timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ExecuteAsync(stale.RequestId, timeout.Token));
            var current = await backend.ReadAsync("dbo", "Example", timeout.Token);
            var failedBackupBackend = new SqlProcedureBackend(name, () => builder.ConnectionString, new FailedBackup());
            await Assert.ThrowsAsync<InvalidOperationException>(() => failedBackupBackend.ApplyAsync("dbo", "Example", alter, current, timeout.Token));
            Assert.Equal(current!.Revision, (await backend.ReadAsync("dbo", "Example", timeout.Token))!.Revision);
            // Synthetic data only. Verify a committed side effect is never replayed.
            await Command(db, "CREATE TABLE dbo.Effects(n int NOT NULL)", timeout.Token);
            await Command(db, "CREATE PROCEDURE dbo.LargeResult AS BEGIN SET NOCOUNT ON; INSERT dbo.Effects VALUES(1); SELECT TOP(600) 1 AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b; END", timeout.Token);
            var large = await workflow.PrepareExecutionAsync("dbo", "LargeResult", "{}", timeout.Token); decision(true);
            var bounded = await workflow.ExecuteAsync(large.RequestId, timeout.Token);
            Assert.False(bounded.Completed); Assert.Equal(500, bounded.ResultSets[0].Rows.Count); Assert.True(bounded.ResultSets[0].Truncated);
            await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(large.RequestId, timeout.Token));
            await using var count = db.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM dbo.Effects";
            Assert.Equal(1, await count.ExecuteScalarAsync(timeout.Token));
        }
        finally
        {
            await Command(admin, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", CancellationToken.None);
            if (Directory.Exists(backupPath)) Directory.Delete(backupPath, true);
        }
    }
    private static async Task Command(SqlConnection connection, string sql, CancellationToken token)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 30; await command.ExecuteNonQueryAsync(token); }
    private sealed class FailedBackup : IProcedureBackupStore
    { public Task SaveAsync(string database, ProcedureDefinition previous, CancellationToken token) => throw new InvalidOperationException("Simulated backup failure"); }
}
