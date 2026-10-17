using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlServerDevLens.Core;

public static class ReadOnlySql
{
    public static void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 20000)
            throw new ArgumentException("Enter one SELECT query of at most 20,000 characters.");
        var parser = new TSql170Parser(true);
        var fragment = parser.Parse(new StringReader(sql), out var errors);
        if (errors.Count > 0 || fragment is not TSqlScript script || script.Batches.Count != 1 ||
            script.Batches[0].Statements.Count != 1 || script.Batches[0].Statements[0] is not SelectStatement select)
            throw new ArgumentException("Only one valid SELECT statement (including a CTE) is allowed. Batches, writes and stored procedures are disabled.");
        if (select.Into is not null || select.On is not null || select.OptimizerHints.Count > 0)
            throw new ArgumentException("SELECT INTO and query hints are disabled.");
        select.Accept(new Guard());
    }

    private sealed class Guard : TSqlFragmentVisitor
    {
        private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "ABS", "ROUND", "CEILING", "FLOOR",
            "LEN", "DATALENGTH", "LOWER", "UPPER", "LTRIM", "RTRIM", "TRIM", "LEFT", "RIGHT", "SUBSTRING",
            "REPLACE", "REPLICATE", "CONCAT", "CONCAT_WS", "CHARINDEX", "PATINDEX", "ISNULL", "NULLIF", "COALESCE",
            "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME", "CURRENT_TIMESTAMP", "DATEADD", "DATEDIFF",
            "DATEDIFF_BIG", "DATEPART", "DATENAME", "YEAR", "MONTH", "DAY", "EOMONTH", "DATEFROMPARTS",
            "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
            "STRING_AGG", "STDEV", "STDEVP", "VAR", "VARP", "DB_NAME", "ISDATE", "ISNUMERIC"
        };
        public override void Visit(SchemaObjectName node)
        {
            if (node.Identifiers.Count > 2 || node.Identifiers.Any(i => i.Value.StartsWith('#')))
                throw new ArgumentException("Use objects in the selected database only, with schema.object names. Cross-database, linked-server and temporary objects are disabled.");
        }
        public override void Visit(FunctionCall node)
        {
            if (node.CallTarget is not null || !Functions.Contains(node.FunctionName.Value) ||
                (node.FunctionName.Value.Equals("DB_NAME", StringComparison.OrdinalIgnoreCase) && node.Parameters.Count != 0))
                throw new ArgumentException("This function is not in the supported read-only built-in function list. User-defined and external functions are disabled.");
        }
        public override void Visit(NamedTableReference node)
        {
            if (node.TableHints.Count > 0) throw new ArgumentException("Table hints are disabled.");
            var identifiers = node.SchemaObject.Identifiers;
            if (identifiers.Count == 2 && identifiers[0].Value.Equals("sys", StringComparison.OrdinalIgnoreCase) &&
                !DatabaseCatalogs.Contains(identifiers[1].Value))
                throw new ArgumentException("Only selected-database catalog views are supported; server-wide system views are disabled.");
            if (identifiers.Count == 1 && identifiers[0].Value.StartsWith("sys", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use schema-qualified database objects. Legacy system views are disabled.");
        }
        private static readonly HashSet<string> DatabaseCatalogs = new(StringComparer.OrdinalIgnoreCase)
        {
            "tables", "views", "objects", "schemas", "columns", "types", "indexes", "index_columns",
            "foreign_keys", "foreign_key_columns", "key_constraints", "check_constraints", "default_constraints",
            "computed_columns", "sql_modules", "parameters", "partitions", "stats", "stats_columns"
        };
        public override void Visit(TSqlFragment node)
        {
            // Fail closed for every table-source kind except local names, joins and subqueries.
            if (node is TableReference && node is not (NamedTableReference or QualifiedJoin or UnqualifiedJoin or
                JoinParenthesisTableReference or QueryDerivedTable or InlineDerivedTable or PivotedTableReference or UnpivotedTableReference))
                throw new ArgumentException("Only local tables, views, CTEs and derived tables are supported. External sources and table-valued functions are disabled.");
            if (node is SelectSetVariable or NextValueForExpression or VariableReference or GlobalVariableExpression)
                throw new ArgumentException("Variables, assignments and sequence changes are disabled.");
        }
    }
}
