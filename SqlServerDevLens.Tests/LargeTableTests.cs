using SqlServerDevLens.Core;
using Xunit;

namespace SqlServerDevLens.Tests;

public class LargeTableTests
{
    [Fact]
    public void LargeSchemaPagesReassembleAndRejectStaleRevisions()
    {
        var metadata = "{\"trigger\":\"" + new string('x', 260000) + "\"}";
        var source = new TableSchemaDefinition("dbo", "Example", metadata, "r1");
        var assembled = new System.Text.StringBuilder(); int offset = 0;
        while (true)
        {
            var page = TableSchemaPage.Create(source, offset, 32000, offset == 0 ? null : "r1");
            assembled.Append(page.Metadata);
            Assert.Equal(metadata.Length, page.TotalCharacters);
            if (page.Complete) { Assert.Null(page.NextOffset); break; }
            offset = page.NextOffset!.Value;
        }
        Assert.Equal(metadata, assembled.ToString());
        Assert.Throws<InvalidOperationException>(() => TableSchemaPage.Create(source, 32000, 32000, "stale"));
        Assert.Throws<ArgumentException>(() => TableSchemaPage.Create(source, 32000, 32000, null));
    }

    [Fact]
    public void DataPagingQuotesIdentifiersAndUsesAllPrimaryKeyColumns()
    {
        var query = TablePaging.Query("dbo", "T]odd", ["id", "a]; DROP TABLE dbo.T;--"], ["part", "id"]);
        Assert.Contains("[a]]; DROP TABLE dbo.T;--]", query);
        Assert.Contains("FROM [dbo].[T]]odd] ORDER BY [part],[id] OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY", query);
        Assert.Throws<ArgumentException>(() => TablePaging.Query("dbo", "T", ["id"], []));
        Assert.Throws<ArgumentException>(() => TablePaging.Validate("dbo", "T", ["id"], -1, 10));
        Assert.Throws<ArgumentException>(() => TablePaging.Validate("dbo", "T", ["id"], 0, 501));
        Assert.Throws<ArgumentException>(() => TablePaging.Validate("dbo", "T", [], 0, 10));
    }

    [Fact]
    public void LongTableSqlRetainsScopedValidationAndRowLimits()
    {
        var sql = "UPDATE dbo.Example SET label=N'" + new string('x', 140000) + "' WHERE id=1";
        Assert.Equal("table_update", TableSql.Validate("dbo", "Example", sql, 1).Action);
        Assert.Throws<ArgumentException>(() => TableSql.Validate("dbo", "Example", sql, 501));
        Assert.Throws<ArgumentException>(() => TableSql.Validate("dbo", "Example", sql.Replace(" WHERE id=1", ""), 1));
        Assert.Throws<ArgumentException>(() => TableSql.Validate("dbo", "Example", sql + "; DELETE dbo.Example", 1));
    }
}
