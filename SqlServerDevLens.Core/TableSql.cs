using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlServerDevLens.Core;

public sealed record TableSqlPlan(string Schema, string Name, string Sql, string Action, int MaxRows, string? Predicate, int InsertRows);
public sealed record TableSqlSnapshot(TableSqlPlan Plan, string? Revision, string Metadata, int MatchingRows);
public sealed record TableSqlResult(string Status, int? AffectedRows, bool SchemaBackupSaved);
public interface ITableActionBackend
{
    Task<TableSchemaDefinition> ReadSchemaAsync(string schema, string name, CancellationToken token) =>
        throw new NotSupportedException("Update DevLens for table schema paging.");
    Task<TableSqlSnapshot> PrepareTableAsync(TableSqlPlan plan, CancellationToken token);
    Task<TableSqlResult> ApplyTableAsync(TableSqlSnapshot snapshot, CancellationToken token);
}
public sealed record TableSchemaDefinition(string Schema, string Name, string Metadata, string Revision);
public sealed record TableSchemaPage(string Schema, string Name, string Metadata, string Revision, int TotalCharacters,
    int Offset, int? NextOffset, bool Complete)
{
    public static TableSchemaPage Create(TableSchemaDefinition source, int offset, int maxCharacters, string? expectedRevision)
    {
        // Shared text paging rules include UTF-16 boundaries and mandatory continuation revisions.
        if (expectedRevision is not null && expectedRevision != source.Revision)
            throw new InvalidOperationException("Table schema changed. Discard previous pages and restart at offset 0.");
        var page = ProcedureDefinitionPage.Create(new(source.Schema, source.Name, source.Metadata, source.Revision, true, true, []),
            offset, maxCharacters, expectedRevision);
        return new(source.Schema, source.Name, page.Definition, page.Revision, page.TotalCharacters, page.Offset, page.NextOffset, page.Complete);
    }
}

public static class TableSql
{
    public static TableSqlPlan Validate(string schema, string name, string sql, int maxRows)
    {
        ProcedureSql.ValidateName(schema, name);
        if (maxRows is < 1 or > 500 || string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("Use one complete SQL statement and maxAffectedRows from 1 to 500.");
        var parsed = new TSql170Parser(true).Parse(new StringReader(sql), out var errors) as TSqlScript;
        if (errors.Count > 0 || parsed is null || parsed.Batches.Count != 1 || parsed.Batches[0].Statements.Count != 1)
            throw new ArgumentException("Supply one valid CREATE TABLE, supported ALTER TABLE, INSERT VALUES or UPDATE statement. No GO or extra statements.");
        var statement = parsed.Batches[0].Statements[0];
        SchemaObjectName target; string action; string? predicate = null; var insertRows = 0;
        switch (statement)
        {
            case CreateTableStatement create when !create.AsFileTable && !create.AsNode && !create.AsEdge &&
                create.SelectStatement is null && create.CloneSource is null && create.Options.Count == 0 &&
                create.OnFileGroupOrPartitionScheme is null && create.FileStreamOn is null && create.FederationScheme is null:
                target = create.SchemaObjectName; action = "table_create"; break;
            case AlterTableAddTableElementStatement add:
                target = add.SchemaObjectName; action = "table_alter"; break;
            case AlterTableAlterColumnStatement alter:
                target = alter.SchemaObjectName; action = "table_alter"; break;
            case InsertStatement insert when insert.WithCtesAndXmlNamespaces is null && insert.OptimizerHints.Count == 0 &&
                insert.InsertSpecification.InsertSource is ValuesInsertSource values:
                target = DirectTarget(insert.InsertSpecification); action = "table_insert";
                insertRows = values.IsDefaultValues ? 1 : values.RowValues.Count;
                if (insertRows > maxRows) throw new ArgumentException("INSERT VALUES exceeds the approved affected-row limit.");
                break;
            case UpdateStatement update when update.WithCtesAndXmlNamespaces is null && update.OptimizerHints.Count == 0 &&
                update.UpdateSpecification.WhereClause is not null && update.UpdateSpecification.FromClause is null &&
                update.UpdateSpecification.WhereClause.Cursor is null:
                target = DirectTarget(update.UpdateSpecification); action = "table_update";
                new Sql170ScriptGenerator().GenerateScript(update.UpdateSpecification.WhereClause.SearchCondition, out predicate);
                break;
            default:
                throw new ArgumentException("Only regular CREATE TABLE, ALTER ADD/ALTER COLUMN, INSERT VALUES and UPDATE with WHERE are supported. DELETE, DROP, TRUNCATE, MERGE, INSERT SELECT/EXEC, joined updates and unrestricted batches are disabled.");
        }
        if (target.Identifiers.Count != 2 || target.Identifiers[0].Value != schema || target.Identifiers[1].Value != name)
            throw new ArgumentException("SQL must target exactly the supplied schema.table in the selected database.");
        ReadOnlySql.ValidateReferences(statement);
        statement.Accept(new SimpleExpressions());
        return new(schema, name, sql, action, maxRows, predicate, insertRows);
    }
    private static SchemaObjectName DirectTarget(DataModificationSpecification specification)
    {
        if (specification.Target is not NamedTableReference table || table.Alias is not null ||
            specification.TopRowFilter is not null || specification.OutputClause is not null || specification.OutputIntoClause is not null)
            throw new ArgumentException("Use a direct schema.table target, without TOP, aliases or OUTPUT.");
        return table.SchemaObject;
    }
    private sealed class SimpleExpressions : TSqlFragmentVisitor
    {
        public override void Visit(ScalarSubquery node) => throw new ArgumentException("Subqueries are unsupported in approved table SQL. Use direct values/expressions and a direct WHERE predicate.");
    }
}
