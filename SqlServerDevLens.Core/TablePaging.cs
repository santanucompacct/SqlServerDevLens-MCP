namespace SqlServerDevLens.Core;

public static class TablePaging
{
    public static void Validate(string schema, string name, string[] columns, int offset, int pageSize)
    {
        ProcedureSql.ValidateName(schema, name);
        if (offset < 0 || offset > int.MaxValue - 500 || pageSize is < 1 or > 500)
            throw new ArgumentException("Use a nonnegative offset up to 2,147,483,147 and pageSize 1-500.");
        if (columns is null || columns.Length is < 1 or > 100 || columns.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 128 || c.Any(char.IsControl)) ||
            columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
            throw new ArgumentException("Supply 1-100 distinct exact column names, not SQL expressions.");
    }
    public static string Query(string schema, string name, string[] columns, string[] keys)
    {
        Validate(schema, name, columns, 0, 1);
        if (keys.Length == 0) throw new ArgumentException("Primary key ordering is required.");
        static string Q(string value) => "[" + value.Replace("]", "]]") + "]";
        return $"SELECT {string.Join(',', columns.Select(Q))} FROM {ProcedureSql.QuotedName(schema, name)} ORDER BY {string.Join(',', keys.Select(Q))} OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY";
    }
}
