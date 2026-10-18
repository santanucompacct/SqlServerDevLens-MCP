using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SqlServerDevLens.Core;

public interface ITableSchemaBackupStore
{
    Task SaveAsync(string metadata, CancellationToken token);
}
public sealed class PrivateTableSchemaBackupStore(string? directory = null) : ITableSchemaBackupStore
{
    public Task SaveAsync(string metadata, CancellationToken token) => new PrivateProcedureBackupStore(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlServerDevLens", "TableSchemaBackups"))
        .SaveContentAsync(metadata, ".json", token);
}

public sealed partial class SqlProcedureBackend
{
    private readonly ITableSchemaBackupStore _tableBackups = tableBackups ?? new PrivateTableSchemaBackupStore();
    private sealed record SchemaSnapshot(string Revision, string Metadata);
    public Task<TableSchemaDefinition> ReadSchemaAsync(string schema, string name, CancellationToken token) => Safe(async ct =>
    {
        ProcedureSql.ValidateName(schema, name);
        await using var connection = await OpenAsync(ct);
        var snapshot = await ReadTableSchema(connection, null, new(schema, name, "", "", 1, null, 0), ct)
            ?? throw new ArgumentException("Table not found or not visible.");
        return new TableSchemaDefinition(schema, name, snapshot.Metadata, snapshot.Revision);
    }, token);
    public Task<TableSqlSnapshot> PrepareTableAsync(TableSqlPlan plan, CancellationToken token) => Safe(async ct =>
    {
        plan = TableSql.Validate(plan.Schema, plan.Name, plan.Sql, plan.MaxRows);
        await using var connection = await OpenAsync(ct);
        var schema = await ReadTableSchema(connection, null, plan, ct);
        if (plan.Action == "table_create" && schema is not null) throw new ArgumentException("Table already exists. CREATE will not replace it.");
        if (plan.Action != "table_create" && schema is null) throw new ArgumentException("A visible user table and VIEW DEFINITION permission are required. Views/synonyms are not supported targets.");
        var matches = await MatchingRows(connection, null, plan, ct);
        if (matches > plan.MaxRows) throw new ArgumentException("UPDATE matches more than maxAffectedRows. Narrow the WHERE clause.");
        return new TableSqlSnapshot(plan, schema?.Revision, schema?.Metadata ?? "Table does not exist.", matches);
    }, token);

    public Task<TableSqlResult> ApplyTableAsync(TableSqlSnapshot snapshot, CancellationToken token) => Safe(async ct =>
    {
        var plan = TableSql.Validate(snapshot.Plan.Schema, snapshot.Plan.Name, snapshot.Plan.Sql, snapshot.Plan.MaxRows);
        await _actions.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                await using (var setup = connection.CreateCommand())
                {
                    setup.Transaction = transaction; setup.CommandTimeout = 15;
                    setup.CommandText = "SET XACT_ABORT ON; SET LOCK_TIMEOUT 5000;";
                    if (snapshot.Revision is not null)
                        setup.CommandText += $" SELECT TOP(1) 1 FROM {ProcedureSql.QuotedName(plan.Schema, plan.Name)} WITH (TABLOCKX,HOLDLOCK);";
                    await setup.ExecuteScalarAsync(ct);
                }
                var current = await ReadTableSchema(connection, transaction, plan, ct);
                if (current?.Revision != snapshot.Revision) throw new InvalidOperationException("Table structure changed after review. No action applied; prepare and approve again.");
                var matches = await MatchingRows(connection, transaction, plan, ct);
                if (matches != snapshot.MatchingRows || matches > plan.MaxRows)
                    throw new InvalidOperationException("The matching-row count changed after review or exceeds the approved limit. No action applied; prepare again.");
                var backup = plan.Action == "table_alter";
                if (backup) await _tableBackups.SaveAsync(current!.Metadata, ct);
                await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 15;
                var dml = plan.Action is "table_insert" or "table_update";
                command.CommandText = plan.Sql + (dml ? "\n; SET @__devlens_affected = @@ROWCOUNT;" : "");
                SqlParameter? rows = null;
                if (dml) { rows = command.Parameters.Add("@__devlens_affected", SqlDbType.Int); rows.Direction = ParameterDirection.Output; }
                await command.ExecuteNonQueryAsync(ct);
                int? affected = dml && rows!.Value is int count ? count : null;
                if (dml && (affected is null || affected > plan.MaxRows || affected != snapshot.MatchingRows))
                    throw new InvalidOperationException("Affected-row verification failed. The transaction was rolled back; do not assume any rows were saved.");
                await transaction.CommitAsync(ct);
                return new TableSqlResult("committed", affected, backup);
            }
            catch
            {
                try { await transaction.RollbackAsync(CancellationToken.None); }
                catch (Exception error) when (error is SqlException or InvalidOperationException) { }
                throw;
            }
        }
        finally { _actions.Release(); }
    }, token, mayWrite: true);

    private static async Task<int> MatchingRows(SqlConnection connection, SqlTransaction? transaction, TableSqlPlan plan, CancellationToken token)
    {
        if (plan.Action == "table_insert") return plan.InsertRows;
        if (plan.Action != "table_update") return 0;
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 15;
        command.CommandText = $"SELECT COUNT(*) FROM (SELECT TOP(@limit) 1 AS n FROM {ProcedureSql.QuotedName(plan.Schema, plan.Name)} WHERE {plan.Predicate}) AS matches;";
        command.Parameters.Add("@limit", SqlDbType.Int).Value = plan.MaxRows + 1;
        return (int)(await command.ExecuteScalarAsync(token))!;
    }
    private static async Task<SchemaSnapshot?> ReadTableSchema(SqlConnection connection, SqlTransaction? transaction, TableSqlPlan plan, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 15;
        command.CommandText = """
            SELECT t.object_id,t.modify_date,t.is_memory_optimized,t.temporal_type,t.is_filetable,
                HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT','VIEW DEFINITION') AS can_view
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            WHERE s.name=@schema AND t.name=@name AND t.is_ms_shipped=0;
            SELECT
            (SELECT c.column_id,c.name,ty.name AS type_name,c.max_length,c.precision,c.scale,c.is_nullable,c.collation_name,
                c.is_identity,CONVERT(nvarchar(100),id.seed_value) AS identity_seed,CONVERT(nvarchar(100),id.increment_value) AS identity_increment,
                dc.name AS default_name,dc.definition AS default_sql,cc.definition AS computed_sql,cc.is_persisted
                FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id
                LEFT JOIN sys.identity_columns id ON id.object_id=c.object_id AND id.column_id=c.column_id
                LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
                LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
                WHERE c.object_id=OBJECT_ID(QUOTENAME(@schema)+'.'+QUOTENAME(@name),'U') ORDER BY c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES),
            (SELECT i.index_id,i.name,i.type_desc,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.filter_definition,i.is_disabled,
                ic.key_ordinal,ic.index_column_id,ic.is_included_column,ic.is_descending_key,c.name AS column_name
                FROM sys.indexes i LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                LEFT JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(QUOTENAME(@schema)+'.'+QUOTENAME(@name),'U') ORDER BY i.index_id,ic.index_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),
            (SELECT name,definition,is_disabled,is_not_trusted FROM sys.check_constraints
                WHERE parent_object_id=OBJECT_ID(QUOTENAME(@schema)+'.'+QUOTENAME(@name),'U') ORDER BY name FOR JSON PATH,INCLUDE_NULL_VALUES),
            (SELECT f.name,f.is_disabled,f.is_not_trusted,f.delete_referential_action_desc,f.update_referential_action_desc,
                pc.name AS parent_column,ps.name AS parent_schema,pt.name AS parent_table,
                rc.name AS referenced_column,rs.name AS referenced_schema,rt.name AS referenced_table
                FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
                JOIN sys.tables pt ON pt.object_id=f.parent_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
                JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fc.parent_column_id
                JOIN sys.tables rt ON rt.object_id=f.referenced_object_id JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
                JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fc.referenced_column_id
                WHERE f.parent_object_id=OBJECT_ID(QUOTENAME(@schema)+'.'+QUOTENAME(@name),'U') OR f.referenced_object_id=OBJECT_ID(QUOTENAME(@schema)+'.'+QUOTENAME(@name),'U')
                ORDER BY f.name,fc.constraint_column_id FOR JSON PATH,INCLUDE_NULL_VALUES),
            (SELECT tr.name,tr.is_disabled,tr.is_instead_of_trigger,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier
                FROM sys.triggers tr LEFT JOIN sys.sql_modules m ON m.object_id=tr.object_id
                WHERE tr.parent_id=OBJECT_ID(QUOTENAME(@schema)+'.'+QUOTENAME(@name),'U') ORDER BY tr.name FOR JSON PATH,INCLUDE_NULL_VALUES);
            """;
        command.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = plan.Schema;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = plan.Name;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        if (reader.GetBoolean(2) || reader.GetByte(3) != 0 || reader.GetBoolean(4))
            throw new ArgumentException("Memory-optimized, temporal and FileTable targets are unsupported.");
        if (reader.IsDBNull(5) || reader.GetInt32(5) != 1) throw new InvalidOperationException("VIEW DEFINITION permission is required to review and back up the table schema.");
        var objectId = reader.GetInt32(0); var modified = reader.GetDateTime(1);
        await reader.NextResultAsync(token); await reader.ReadAsync(token);
        var metadata = new Dictionary<string, object?> { ["database"] = connection.Database, ["schema"] = plan.Schema, ["table"] = plan.Name,
            ["objectId"] = objectId, ["modified"] = modified, ["notice"] = "Catalog metadata only. This is NOT a row backup or a complete restore/deployment script." };
        var keys = new[] { "columns", "indexes", "checks", "foreignKeys", "triggers" };
        for (var i = 0; i < keys.Length; i++)
        {
            // Empty FOR JSON catalog subqueries can be SQL NULL (for example, no triggers).
            var json = reader.IsDBNull(i) ? "[]" : reader.GetString(i);
            using var document = JsonDocument.Parse(json); metadata[keys[i]] = document.RootElement.Clone();
        }
        var text = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), text);
    }
}
