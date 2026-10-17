using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SqlServerDevLens.Core;

public sealed class SqlProcedureBackend(string database, Func<string> connectionString,
    IProcedureBackupStore? backups = null) : IProcedureBackend
{
    private readonly SemaphoreSlim _actions = new(1, 1);
    private readonly IProcedureBackupStore _backups = backups ?? new PrivateProcedureBackupStore();
    public string Database { get; } = database;
    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var builder = new SqlConnectionStringBuilder(connectionString()) { ApplicationIntent = ApplicationIntent.ReadWrite, Pooling = false };
        var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            if (!string.Equals(connection.Database, Database, StringComparison.Ordinal)) throw new InvalidOperationException("Wrong database. Return to setup and test again.");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static async Task<T> Safe<T>(Func<CancellationToken, Task<T>> action, CancellationToken token, bool mayWrite = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { return await action(timeout.Token).ConfigureAwait(false); }
        catch (SqlException error) { throw new InvalidOperationException($"SQL procedure operation failed (error {error.Number}). Check permissions, parameters and SQL. " + (mayWrite ? "Changes may already have committed. Verify state before preparing a retry." : "No action was applied.")); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("Procedure operation timed out. " + (mayWrite ? "Changes may already have committed. Verify state before preparing a retry." : "No action was applied.")); }
    }
    public Task<ProcedureDefinition?> ReadAsync(string schema, string name, CancellationToken token) => Safe(async ct =>
    {
        ProcedureSql.ValidateName(schema, name);
        await using var connection = await OpenAsync(ct);
        return await ReadOnAsync(connection, schema, name, ct);
    }, token);
    private static async Task<ProcedureDefinition?> ReadOnAsync(SqlConnection connection, string schema, string name, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = 15;
        command.CommandText = """
            SELECT o.object_id,o.modify_date,SUBSTRING(m.definition,1,128001),m.uses_ansi_nulls,m.uses_quoted_identifier
            FROM sys.procedures o JOIN sys.schemas s ON s.schema_id=o.schema_id
            LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id
            WHERE s.name=@schema AND o.name=@name AND o.type='P' AND o.is_ms_shipped=0;
            SELECT p.name,t.name,p.max_length,p.precision,p.scale,p.is_output,p.is_readonly,t.is_user_defined
            FROM sys.parameters p JOIN sys.procedures o ON o.object_id=p.object_id
            JOIN sys.schemas s ON s.schema_id=o.schema_id JOIN sys.types t ON t.user_type_id=p.user_type_id
            WHERE s.name=@schema AND o.name=@name AND p.parameter_id>0 ORDER BY p.parameter_id;
            """;
        command.Parameters.Add("@schema", SqlDbType.NVarChar, 128).Value = schema;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = name;
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        if (reader.IsDBNull(2)) throw new InvalidOperationException("Full definition is unavailable. VIEW DEFINITION permission is required; encrypted/CLR procedures are unsupported.");
        var sql = reader.GetString(2);
        if (sql.Length > 128000) throw new ArgumentException("Definition exceeds the 128,000-character review limit; it was not silently truncated.");
        var ansi = reader.GetBoolean(3); var quoted = reader.GetBoolean(4);
        var revision = ProcedureSql.Revision(sql, reader.GetInt32(0), reader.GetDateTime(1), ansi, quoted);
        var defaults = ProcedureSql.Parse(sql).Parameters.Where(p => p.Value is not null).Select(p => p.VariableName.Value).ToHashSet(StringComparer.Ordinal);
        var parameters = new List<ProcedureParameterInfo>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (parameters.Count >= 100) throw new ArgumentException("At most 100 parameters are supported by this review workflow.");
            parameters.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4), reader.GetBoolean(5), defaults.Contains(reader.GetString(0)), reader.GetBoolean(6), reader.GetBoolean(7)));
        }
        return new(schema, name, sql, revision, ansi, quoted, parameters);
    }
    public void ValidateParameters(ProcedureDefinition definition, string parametersJson) => Bind(definition, parametersJson);
    public static IReadOnlyList<SqlParameter> Bind(ProcedureDefinition definition, string json)
    {
        var supplied = ProcedureSql.Parameters(json);
        if (supplied.Keys.Any(k => !definition.Parameters.Any(p => p.Name == k))) throw new ArgumentException("Unknown parameter name. Use the exact names from get_procedure_definition.");
        var result = new List<SqlParameter>();
        foreach (var info in definition.Parameters)
        {
            if (info.Structured || info.UserDefined) throw new ArgumentException("Table-valued and user-defined parameters are unsupported for execution.");
            var suppliedValue = supplied.TryGetValue(info.Name, out var value);
            if (!suppliedValue && !info.Output)
            {
                if (info.HasDefault) continue;
                throw new ArgumentException($"Required parameter missing: {info.Name}.");
            }
            var type = info.Type.ToLowerInvariant() switch
            {
                "bigint" => SqlDbType.BigInt, "int" => SqlDbType.Int, "smallint" => SqlDbType.SmallInt, "tinyint" => SqlDbType.TinyInt,
                "bit" => SqlDbType.Bit, "decimal" or "numeric" => SqlDbType.Decimal, "money" => SqlDbType.Money, "smallmoney" => SqlDbType.SmallMoney,
                "float" => SqlDbType.Float, "real" => SqlDbType.Real, "date" => SqlDbType.Date, "datetime" => SqlDbType.DateTime,
                "datetime2" => SqlDbType.DateTime2, "smalldatetime" => SqlDbType.SmallDateTime, "datetimeoffset" => SqlDbType.DateTimeOffset,
                "time" => SqlDbType.Time, "uniqueidentifier" => SqlDbType.UniqueIdentifier, "varchar" => SqlDbType.VarChar,
                "nvarchar" => SqlDbType.NVarChar, "char" => SqlDbType.Char, "nchar" => SqlDbType.NChar,
                _ => throw new ArgumentException($"Unsupported parameter type: {info.Type}. Binary, XML, legacy text and structured parameters are not supported in this milestone.")
            };
            var size = info.Size;
            if (type is SqlDbType.NVarChar or SqlDbType.NChar && size > 0) size /= 2;
            var parameter = new SqlParameter(info.Name, type) { Precision = info.Precision, Scale = info.Scale,
                Direction = info.Output ? ParameterDirection.InputOutput : ParameterDirection.Input };
            if (type is SqlDbType.VarChar or SqlDbType.NVarChar or SqlDbType.Char or SqlDbType.NChar)
                parameter.Size = info.Output && (size == -1 || size > 20000) ? 20000 : size;
            try
            {
                parameter.Value = !suppliedValue || value.ValueKind == JsonValueKind.Null ? DBNull.Value : type switch
                {
                    SqlDbType.BigInt => value.GetInt64(), SqlDbType.Int => value.GetInt32(), SqlDbType.SmallInt => value.GetInt16(), SqlDbType.TinyInt => value.GetByte(),
                    SqlDbType.Bit => value.GetBoolean(), SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => value.GetDecimal(),
                    SqlDbType.Float => value.GetDouble(), SqlDbType.Real => value.GetSingle(), SqlDbType.UniqueIdentifier => value.GetGuid(),
                    SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime => value.GetDateTime(),
                    SqlDbType.DateTimeOffset => value.GetDateTimeOffset(), SqlDbType.Time => TimeSpan.ParseExact(value.GetString()!, "c", CultureInfo.InvariantCulture),
                    _ => value.GetString()!
                };
                if (parameter.Value is string text && ((size > 0 && text.Length > size) || text.Length > 20000))
                    throw new ArgumentException($"Parameter {info.Name} exceeds its declared or 20,000-character limit.");
                if (type == SqlDbType.Decimal && parameter.Value is decimal number &&
                    (decimal.Round(number, Math.Min(28, (int)info.Scale)) != number ||
                     decimal.Truncate(decimal.Abs(number)).ToString(CultureInfo.InvariantCulture).TrimStart('0').Length > info.Precision - info.Scale))
                    throw new ArgumentException($"Parameter {info.Name} exceeds its declared decimal precision/scale; rounding is not automatic.");
            }
            catch (Exception error) when (error is FormatException or InvalidOperationException or OverflowException)
            { throw new ArgumentException($"Invalid value for {info.Name} ({info.Type}). Use JSON scalars and ISO dates."); }
            result.Add(parameter);
        }
        return result;
    }
    private static void CheckRevision(ProcedureDefinition? current, ProcedureDefinition? expected)
    {
        if (current?.Revision != expected?.Revision) throw new InvalidOperationException("Procedure changed after review or now exists. Prepare and approve again. No action was applied.");
    }
    public Task<ProcedureExecution> ExecuteAsync(ProcedureDefinition definition, string parametersJson, CancellationToken token) => Safe(async ct =>
    {
        await _actions.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            CheckRevision(await ReadOnAsync(connection, definition.Schema, definition.Name, ct), definition);
            await using var command = connection.CreateCommand(); command.CommandTimeout = 15;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = ProcedureSql.QuotedName(definition.Schema, definition.Name);
            foreach (var parameter in Bind(definition, parametersJson)) command.Parameters.Add(parameter);
            var returnCode = command.Parameters.Add("@__devlens_return_value", SqlDbType.Int); returnCode.Direction = ParameterDirection.ReturnValue;
            var sets = new List<SqlResult>(); var rowsRemaining = 500; var characters = 0; var complete = true;
            await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct))
            {
                do
                {
                    if (reader.FieldCount == 0) continue;
                    if (sets.Count >= 10 || reader.FieldCount > 100) { complete = false; break; }
                    var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new SqlColumn(reader.GetName(i), reader.GetDataTypeName(i))).ToArray();
                    var rows = new List<object?[]>(); var valuesTruncated = false; var truncated = false;
                    while (await reader.ReadAsync(ct))
                    {
                        if (rowsRemaining == 0 || characters >= 200000) { truncated = true; complete = false; break; }
                        var row = new object?[reader.FieldCount];
                        for (var i = 0; i < row.Length; i++)
                        {
                            if (await reader.IsDBNullAsync(i, ct)) continue;
                            var type = reader.GetDataTypeName(i).ToLowerInvariant();
                            if (type is "nvarchar" or "varchar" or "char" or "nchar" or "text" or "ntext")
                            {
                                using var text = reader.GetTextReader(i); var buffer = new char[2001]; var count = 0;
                                while (count < buffer.Length) { var n = await text.ReadAsync(buffer.AsMemory(count), ct); if (n == 0) break; count += n; }
                                row[i] = new string(buffer, 0, Math.Min(count, 2000)); valuesTruncated |= count > 2000;
                            }
                            else if (type is "binary" or "varbinary" or "image" or "timestamp" or "rowversion" or "xml" or "sql_variant" or "geography" or "geometry" or "hierarchyid")
                            { row[i] = "[complex/binary value omitted]"; valuesTruncated = true; }
                            else row[i] = Format(reader.GetValue(i));
                            characters += Convert.ToString(row[i], CultureInfo.InvariantCulture)?.Length ?? 0;
                        }
                        if (characters > 200000) { truncated = true; complete = false; break; }
                        rows.Add(row); rowsRemaining--;
                    }
                    sets.Add(new(Database, columns, rows, truncated, valuesTruncated));
                    if (!complete) break;
                } while (await reader.NextResultAsync(ct));
                if (!complete) command.Cancel();
            }
            var output = new Dictionary<string, object?>(); var outputTruncated = false;
            if (complete)
                foreach (SqlParameter parameter in command.Parameters)
                    if (parameter.Direction == ParameterDirection.InputOutput)
                    {
                        output[parameter.ParameterName] = Format(parameter.Value);
                        outputTruncated |= parameter.Value is string text && text.Length > 2000;
                        outputTruncated |= definition.Parameters.Any(p => p.Name == parameter.ParameterName && (p.Size == -1 || p.Size > 20000));
                    }
            return new ProcedureExecution(sets, output, complete && returnCode.Value is int code ? code : null, complete,
                complete ? "Execution finished. Output strings are bounded to 2000 characters." : "Result limit reached; remaining results were cancelled. Changes may already have committed. Do not retry automatically.", outputTruncated);
        }
        finally { _actions.Release(); }
    }, token, mayWrite: true);
    private static object? Format(object value) => value switch
    {
        DBNull => null, DateTime date => date.ToString("O", CultureInfo.InvariantCulture), DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        byte or short or int or long or decimal or double or float or bool => value,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) is { } text ? text[..Math.Min(text.Length, 2000)] : null
    };
    public Task<ProcedureApplyResult> ApplyAsync(string schema, string name, string sql, ProcedureDefinition? previous, CancellationToken token) => Safe(async ct =>
    {
        ProcedureSql.ValidateChange(schema, name, sql, previous is null);
        await _actions.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            CheckRevision(await ReadOnAsync(connection, schema, name, ct), previous);
            if (previous is not null) await _backups.SaveAsync(Database, previous, ct); // Must succeed before DDL.
            CheckRevision(await ReadOnAsync(connection, schema, name, ct), previous);
            await using (var options = connection.CreateCommand())
            {
                options.CommandTimeout = 15;
                options.CommandText = $"SET ANSI_NULLS {(previous?.AnsiNulls != false ? "ON" : "OFF")}; SET QUOTED_IDENTIFIER {(previous?.QuotedIdentifier != false ? "ON" : "OFF")}; SET LOCK_TIMEOUT 5000;";
                await options.ExecuteNonQueryAsync(ct);
            }
            await using var command = connection.CreateCommand(); command.CommandTimeout = 15; command.CommandText = sql;
            await command.ExecuteNonQueryAsync(ct);
            return new ProcedureApplyResult("applied", previous is not null);
        }
        finally { _actions.Release(); }
    }, token, mayWrite: true);
}
