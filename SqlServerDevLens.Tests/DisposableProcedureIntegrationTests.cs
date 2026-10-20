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
    public async Task RealTableCreateInsertUpdateAlterLimitsBackupAndSqlPermissions()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("DEVLENS_PROCEDURE_TEST_CONNECTION_STRING"));
        Assert.Contains(builder.DataSource, new[] { "127.0.0.1,1433", "localhost,1433" });
        builder.InitialCatalog = "master"; builder.Pooling = false;
        var name = "DevLensTest_" + Guid.NewGuid().ToString("N");
        var backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevLensTestBackups", name);
        var login = "dvl_test_" + Guid.NewGuid().ToString("N"); var loginCreated = false;
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
            var backend = new SqlProcedureBackend(name, () => builder.ConnectionString, tableBackups: new PrivateTableSchemaBackupStore(backupPath));
            Action<bool>? decision = null;
            using var workflow = new ProcedureWorkflow(backend, (_, approve, _) => decision = approve);
            async Task<TableSqlResult> Run(string sql, int limit = 10)
            {
                var pending = await workflow.PrepareTableAsync("dbo", "Example", sql, limit, timeout.Token);
                await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyTableAsync(pending.RequestId, timeout.Token));
                decision!(true);
                return await workflow.ApplyTableAsync(pending.RequestId, timeout.Token);
            }
            Assert.Equal("committed", (await Run("CREATE TABLE dbo.Example(id int NOT NULL PRIMARY KEY, qty int NOT NULL CHECK(qty>=0), label nvarchar(50) NULL)")).Status);
            Assert.Equal(2, (await Run("INSERT dbo.Example(id,qty,label) VALUES(1,0,N'one'),(2,0,N'two')", 2)).AffectedRows);
            Assert.Equal(1, (await Run("UPDATE dbo.Example SET qty=qty+5 WHERE id=1", 1)).AffectedRows);
            await Assert.ThrowsAsync<ArgumentException>(() => workflow.PrepareTableAsync("dbo", "Example", "UPDATE dbo.Example SET qty=1 WHERE id>0", 1, timeout.Token));
            var drift = await workflow.PrepareTableAsync("dbo", "Example", "UPDATE dbo.Example SET qty=99 WHERE id>0", 2, timeout.Token); decision!(true);
            await using var db = new SqlConnection(builder.ConnectionString); await db.OpenAsync(timeout.Token);
            await Command(db, "INSERT dbo.Example(id,qty) VALUES(3,0)", timeout.Token);
            using var readerDatabase = new SqlDatabase(new("127.0.0.1", 1433, name, AuthenticationMode.Sql,
                builder.UserID, true, true), builder.Password);
            var firstRows = await readerDatabase.ReadTablePageAsync("dbo", "Example", ["id", "qty"], 0, 2, timeout.Token);
            Assert.Equal(2, firstRows.NextOffset); Assert.Equal(2, firstRows.Data.Rows.Count);
            var finalRows = await readerDatabase.ReadTablePageAsync("dbo", "Example", ["id", "qty"], firstRows.NextOffset!.Value, 2, timeout.Token);
            Assert.Null(finalRows.NextOffset); Assert.Single(finalRows.Data.Rows); Assert.Equal(3, finalRows.Data.Rows[0][0]);
            await Command(db, "CREATE TABLE dbo.NoKey(id int)", timeout.Token);
            await Assert.ThrowsAsync<ArgumentException>(() => readerDatabase.ReadTablePageAsync("dbo", "NoKey", ["id"], 0, 2, timeout.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyTableAsync(drift.RequestId, timeout.Token));
            await using (var check = db.CreateCommand())
            { check.CommandText = "SELECT qty FROM dbo.Example WHERE id=1"; Assert.Equal(5, await check.ExecuteScalarAsync(timeout.Token)); }
            var failing = await workflow.PrepareTableAsync("dbo", "Example", "UPDATE dbo.Example SET qty=-1 WHERE id=1", 1, timeout.Token); decision(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyTableAsync(failing.RequestId, timeout.Token));
            await using (var check = db.CreateCommand())
            { check.CommandText = "SELECT qty FROM dbo.Example WHERE id=1"; Assert.Equal(5, await check.ExecuteScalarAsync(timeout.Token)); }
            var largeMarker = new string('x', 160000);
            await Command(db, "CREATE TRIGGER dbo.ExampleAudit ON dbo.Example AFTER UPDATE AS BEGIN /*" + largeMarker + "*/ SET NOCOUNT ON; END", timeout.Token);
            var schema = await backend.ReadSchemaAsync("dbo", "Example", timeout.Token);
            Assert.True(schema.Metadata.Length > 128000); Assert.Contains(largeMarker, schema.Metadata);
            var firstSchema = await workflow.ReadTableSchemaAsync("dbo", "Example", 0, 32000, null, timeout.Token);
            Assert.False(firstSchema.Complete);
            var nextSchema = await workflow.ReadTableSchemaAsync("dbo", "Example", firstSchema.NextOffset!.Value, 32000, firstSchema.Revision, timeout.Token);
            Assert.Equal(firstSchema.Revision, nextSchema.Revision);
            Assert.True((await Run("ALTER TABLE dbo.Example ADD extra int NULL")).SchemaBackupSaved);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ReadTableSchemaAsync("dbo", "Example", firstSchema.NextOffset.Value, 32000, firstSchema.Revision, timeout.Token));
            var backup = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(backupPath)), timeout.Token);
            Assert.Contains("Catalog metadata only", backup); Assert.DoesNotContain("\"name\": \"extra\"", backup);
            Assert.Contains(largeMarker, backup);
            var stale = await workflow.PrepareTableAsync("dbo", "Example", "UPDATE dbo.Example SET qty=10 WHERE id=1", 1, timeout.Token); decision(true);
            await Command(db, "ALTER TABLE dbo.Example ADD outside_change int NULL", timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyTableAsync(stale.RequestId, timeout.Token));
            var blockedBackend = new SqlProcedureBackend(name, () => builder.ConnectionString, tableBackups: new FailedTableBackup());
            var blocked = await blockedBackend.PrepareTableAsync(TableSql.Validate("dbo", "Example", "ALTER TABLE dbo.Example ADD never_added int NULL", 1), timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => blockedBackend.ApplyTableAsync(blocked, timeout.Token));
            await using (var check = db.CreateCommand())
            { check.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Example') AND name='never_added'"; Assert.Equal(0, await check.ExecuteScalarAsync(timeout.Token)); }
            await Assert.ThrowsAsync<ArgumentException>(() => workflow.PrepareTableAsync("dbo", "Example", "DELETE dbo.Example WHERE id>0", 1, timeout.Token));
            var deletion = await workflow.PrepareTableAsync("dbo", "Example", "DELETE dbo.Example WHERE id=3", 1, timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyTableAsync(deletion.RequestId, timeout.Token));
            decision(true); Assert.Equal(1, (await workflow.ApplyTableAsync(deletion.RequestId, timeout.Token)).AffectedRows);
            await Assert.ThrowsAsync<ArgumentException>(() => workflow.ApplyTableAsync(deletion.RequestId, timeout.Token));
            await using (var verifyDelete = db.CreateCommand())
            { verifyDelete.CommandText = "SELECT COUNT(*) FROM dbo.Example WHERE id=3"; Assert.Equal(0, await verifyDelete.ExecuteScalarAsync(timeout.Token)); }
            // Even a desktop-approved action cannot bypass the SQL account's rights.
            var password = "Dvl-" + Guid.NewGuid().ToString("N") + "-1!";
            await Command(admin, $"CREATE LOGIN [{login}] WITH PASSWORD=N'{password}'", timeout.Token); loginCreated = true;
            await Command(db, $"CREATE USER [{login}] FOR LOGIN [{login}]; GRANT SELECT ON dbo.Example TO [{login}]; GRANT VIEW DEFINITION ON OBJECT::dbo.Example TO [{login}]", timeout.Token);
            var limited = new SqlConnectionStringBuilder(builder.ConnectionString) { UserID = login, Password = password, IntegratedSecurity = false };
            using var restricted = new ProcedureWorkflow(new SqlProcedureBackend(name, () => limited.ConnectionString), (_, approve, _) => approve(true));
            var denied = await restricted.PrepareTableAsync("dbo", "Example", "INSERT dbo.Example(id,qty) VALUES(4,0)", 1, timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restricted.ApplyTableAsync(denied.RequestId, timeout.Token));
            await using (var check = db.CreateCommand())
            { check.CommandText = "SELECT COUNT(*) FROM dbo.Example WHERE id=4"; Assert.Equal(0, await check.ExecuteScalarAsync(timeout.Token)); }
            var deniedDelete = await restricted.PrepareTableAsync("dbo", "Example", "DELETE dbo.Example WHERE id=2", 1, timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restricted.ApplyTableAsync(deniedDelete.RequestId, timeout.Token));
        }
        finally
        {
            await Command(admin, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", CancellationToken.None);
            if (loginCreated) await Command(admin, $"DROP LOGIN [{login}]", CancellationToken.None);
            if (Directory.Exists(backupPath)) Directory.Delete(backupPath, true);
        }
    }
    private sealed class FailedTableBackup : ITableSchemaBackupStore
    { public Task SaveAsync(string metadata, CancellationToken token) => throw new InvalidOperationException("Simulated table backup failure"); }
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
            // Exercise full read, approval, execution, ALTER and private backup above the old 128K cap.
            var create = "CREATE PROCEDURE dbo.Example @value int, @label nvarchar(50)=N'default', @output int OUTPUT AS BEGIN /*" + new string('x', 160000) + "*/ SET NOCOUNT ON; SELECT @value AS value,@label AS label; SELECT @value+1 AS second_value; SET @output=@value*2; RETURN 7; END";
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
            Assert.True(bounded.Completed); Assert.Equal(600, bounded.ResultSets[0].Rows.Count); Assert.False(bounded.ResultSets[0].Truncated);
            await Assert.ThrowsAsync<ArgumentException>(() => workflow.ExecuteAsync(large.RequestId, timeout.Token));
            await using var count = db.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM dbo.Effects";
            Assert.Equal(1, await count.ExecuteScalarAsync(timeout.Token));
            // Full large JSON/text/output parameters, >10 result sets and >100 columns: disposable DB only.
            var textSql = "CREATE PROC dbo.FullText @output nvarchar(max) OUTPUT AS BEGIN SET NOCOUNT ON; SET @output=REPLICATE(CAST(N'o' AS nvarchar(max)),130000); SELECT REPLICATE(CAST(N'x' AS nvarchar(max)),350000) AS resultreturn; "
                + string.Join(" ", Enumerable.Range(0, 11).Select(i => $"SELECT {i} AS n;"))
                + " SELECT " + string.Join(",", Enumerable.Range(0, 110).Select(i => $"{i} AS c{i}")) + "; RETURN 9; END";
            await Command(db, textSql, timeout.Token);
            var full = await workflow.PrepareExecutionAsync("dbo", "FullText", "{}", timeout.Token); decision(true);
            var fullResult = await workflow.ExecuteAsync(full.RequestId, timeout.Token);
            Assert.True(fullResult.Completed); Assert.True(fullResult.ReadCapturedResult);
            var captured = new System.Text.StringBuilder(); var cursor = 0;
            while (true)
            {
                var page = workflow.Results.Read(fullResult.Capture!.ResultId, cursor, 128000, fullResult.Capture.Revision);
                captured.Append(page.Json); if (page.NextOffset is not int next) break; cursor = next;
            }
            using var doc = System.Text.Json.JsonDocument.Parse(captured.ToString());
            Assert.Equal(13, doc.RootElement.GetProperty("resultSets").GetArrayLength());
            Assert.Equal(350000, doc.RootElement.GetProperty("resultSets")[0].GetProperty("rows")[0][0].GetString()!.Length);
            Assert.Equal(110, doc.RootElement.GetProperty("resultSets")[12].GetProperty("columns").GetArrayLength());
            Assert.Equal(130000, doc.RootElement.GetProperty("outputParameters").GetProperty("@output").GetString()!.Length);
            Assert.Equal(9, doc.RootElement.GetProperty("returnCode").GetInt32());

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
