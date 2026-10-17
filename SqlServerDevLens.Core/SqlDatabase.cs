using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace SqlServerDevLens.Core;

public sealed record SqlColumn(string Name, string Type);
public sealed record SqlResult(string Database, IReadOnlyList<SqlColumn> Columns, IReadOnlyList<object?[]> Rows,
    bool Truncated, bool ValuesTruncated);

public interface ISqlDatabase : IDisposable
{
    string Database { get; }
    Task<SqlResult> GetDatabaseInfoAsync(CancellationToken cancellationToken);
    Task<SqlResult> ListObjectsAsync(CancellationToken cancellationToken);
    Task<SqlResult> DescribeObjectAsync(string schema, string name, CancellationToken cancellationToken);
    Task<SqlResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken);
}

// This object is owned by the running desktop, never serialized or sent to the adapter.
public sealed class SqlDatabase : ISqlDatabase
{
    private string? _connectionString;
    private readonly CancellationTokenSource _stopped = new();
    private readonly SemaphoreSlim _operations = new(4, 4);
    public string Database { get; }
    public SqlDatabase(ConnectionProfile profile, string? password)
    {
        Database = profile.Database.Trim();
        var builder = new SqlConnectionStringBuilder(SqlConnectionTester.BuildConnectionString(profile, password))
        { Pooling = false, ApplicationIntent = ApplicationIntent.ReadOnly };
        _connectionString = builder.ConnectionString;
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref _connectionString, null);
        _stopped.Cancel();
    }

    public Task<SqlResult> GetDatabaseInfoAsync(CancellationToken cancellationToken) => ExecuteAsync(
        "SELECT DB_NAME() AS database_name, CONVERT(nvarchar(128),DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS collation, CONVERT(nvarchar(128),DATABASEPROPERTYEX(DB_NAME(), 'Updateability')) AS updateability", 1, null, cancellationToken);

    public Task<SqlResult> ListObjectsAsync(CancellationToken cancellationToken) => ExecuteAsync("""
        SELECT s.name AS schema_name, o.name AS object_name, o.type_desc AS object_type
        FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
        WHERE o.is_ms_shipped=0 AND o.type IN ('U','V','P','FN','IF','TF')
        ORDER BY s.name,o.name
        """, 1000, null, cancellationToken);

    public Task<SqlResult> DescribeObjectAsync(string schema, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schema) || schema.Length > 128 || string.IsNullOrWhiteSpace(name) || name.Length > 128)
            throw new ArgumentException("Supply a schema and object name, each at most 128 characters.");
        return ExecuteAsync("""
            SELECT c.column_id, c.name AS column_name, t.name AS data_type, c.max_length,
                c.precision, c.scale, c.is_nullable, c.is_identity,
                CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns ic
                    JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id
                    WHERE ic.object_id=c.object_id AND ic.column_id=c.column_id AND i.is_primary_key=1)
                    THEN 1 ELSE 0 END) AS is_primary_key,
                rs.name AS referenced_schema, ro.name AS referenced_object, rc.name AS referenced_column
            FROM sys.columns c
            JOIN sys.objects o ON o.object_id=c.object_id
            JOIN sys.schemas s ON s.schema_id=o.schema_id
            JOIN sys.types t ON t.user_type_id=c.user_type_id
            LEFT JOIN sys.foreign_key_columns fk ON fk.parent_object_id=c.object_id AND fk.parent_column_id=c.column_id
            LEFT JOIN sys.objects ro ON ro.object_id=fk.referenced_object_id
            LEFT JOIN sys.schemas rs ON rs.schema_id=ro.schema_id
            LEFT JOIN sys.columns rc ON rc.object_id=ro.object_id AND rc.column_id=fk.referenced_column_id
            WHERE s.name=@schema AND o.name=@name AND o.type IN ('U','V')
            ORDER BY c.column_id
            """, 1000, command =>
            {
                command.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = schema;
                command.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = name;
            }, cancellationToken);
    }
    public Task<SqlResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        ReadOnlySql.Validate(sql);
        if (maxRows is < 1 or > 500) throw new ArgumentException("maxRows must be between 1 and 500.");
        return ExecuteAsync(sql, maxRows, null, cancellationToken);
    }

    private async Task<SqlResult> ExecuteAsync(string sql, int maxRows, Action<SqlCommand>? configure, CancellationToken cancellationToken)
    {
        var connectionString = _connectionString ?? throw new InvalidOperationException("SQL context is stopped. Test the connection again.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopped.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var acquired = false;
        try
        {
            await _operations.WaitAsync(timeout.Token).ConfigureAwait(false);
            acquired = true;
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
            // A caller cannot supply USE, but verify the actual initial database as well.
            if (!string.Equals(connection.Database, Database, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQL opened a different database. Test the selected database again.");
            await using var command = connection.CreateCommand();
            command.CommandText = sql; command.CommandTimeout = 15;
            configure?.Invoke(command);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult, timeout.Token).ConfigureAwait(false);
            if (reader.FieldCount > 100) throw new ArgumentException("Result has more than 100 columns. Select fewer columns.");
            var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new SqlColumn(reader.GetName(i), reader.GetDataTypeName(i))).ToArray();
            var rows = new List<object?[]>();
            var valuesTruncated = false; var truncated = false; var characters = 0;
            while (await reader.ReadAsync(timeout.Token).ConfigureAwait(false))
            {
                if (rows.Count == maxRows || characters >= 200000) { truncated = true; break; }
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    if (await reader.IsDBNullAsync(i, timeout.Token).ConfigureAwait(false)) continue;
                    var type = reader.GetDataTypeName(i).ToLowerInvariant();
                    if (type is "binary" or "varbinary" or "image" or "timestamp" or "rowversion")
                    { row[i] = "[binary omitted]"; valuesTruncated = true; }
                    else if (type is "varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext")
                    {
                        using var text = reader.GetTextReader(i);
                        var buffer = new char[2001]; var count = 0;
                        while (count < buffer.Length)
                        {
                            var read = await text.ReadAsync(buffer.AsMemory(count), timeout.Token).ConfigureAwait(false);
                            if (read == 0) break; count += read;
                        }
                        row[i] = new string(buffer, 0, Math.Min(count, 2000));
                        if (count > 2000) valuesTruncated = true;
                    }
                    else if (type is "xml" or "geography" or "geometry" or "hierarchyid" or "sql_variant")
                    { row[i] = "[complex value omitted]"; valuesTruncated = true; }
                    else
                    {
                        var value = reader.GetValue(i);
                        row[i] = value switch
                        {
                            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
                            DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
                            byte or short or int or long or bool or decimal or double or float => value,
                            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                        };
                    }
                    characters += Convert.ToString(row[i], CultureInfo.InvariantCulture)?.Length ?? 0;
                }
                if (characters > 200000) { truncated = true; break; }
                rows.Add(row);
            }
            return new(Database, columns, rows, truncated, valuesTruncated);
        }
        catch (SqlException error)
        {
            throw new InvalidOperationException(error.Number switch
            {
                -2 => "SQL operation timed out. Narrow the query and try again.",
                229 or 230 => "SQL permission denied. Ask the database administrator for read access to this object.",
                208 => "Object not found or not visible in the selected database. Check schema and object name.",
                18456 => "SQL login failed. Return to DevLens connection setup and test again.",
                _ => $"SQL operation failed (error {error.Number}). Check query syntax, permissions and connectivity."
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("SQL operation timed out. Narrow the query and try again."); }
        catch (Exception error) when (error is OverflowException or System.Security.Authentication.AuthenticationException or PlatformNotSupportedException)
        { throw new InvalidOperationException("SQL could not read this result. Check platform/TLS support and convert unsupported values in the SELECT query."); }
        finally { if (acquired) _operations.Release(); }
    }
}
