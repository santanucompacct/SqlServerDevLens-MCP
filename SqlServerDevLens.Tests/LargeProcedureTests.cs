using SqlServerDevLens.Core;
using Xunit;

namespace SqlServerDevLens.Tests;

public class LargeProcedureTests
{
    [Fact]
    public void LargeSourceReassemblesExactlyAndKeepsFullRevision()
    {
        var sql = "CREATE PROC dbo.Example AS /*" + new string('x', 256500) + "*/ SELECT N'😀';";
        ProcedureSql.ValidateChange("dbo", "Example", sql, true);
        var source = new ProcedureDefinition("dbo", "Example", sql,
            ProcedureSql.Revision(sql, 1, DateTime.UnixEpoch, true, true), true, true, []);
        var output = new System.Text.StringBuilder();
        var offset = 0;
        do
        {
            var page = ProcedureDefinitionPage.Create(source, offset, 32000, offset == 0 ? null : source.Revision);
            Assert.Equal(sql.Length, page.TotalCharacters);
            Assert.Equal(source.Revision, page.Revision);
            Assert.InRange(page.Definition.Length, 1, 32000);
            output.Append(page.Definition);
            if (page.Complete) { Assert.Null(page.NextOffset); break; }
            offset = page.NextOffset!.Value;
        } while (true);
        Assert.Equal(sql, output.ToString());
        ProcedureSql.ValidateChange("dbo", "Example", sql.Replace("CREATE PROC", "ALTER PROC"), false);
    }

    [Fact]
    public void ContinuationsRejectMissingOrChangedRevisionsAndInvalidBounds()
    {
        var source = new ProcedureDefinition("dbo", "Example", new string('x', 1000), "r1", true, true, []);
        Assert.Throws<ArgumentException>(() => ProcedureDefinitionPage.Create(source, 256));
        Assert.Throws<InvalidOperationException>(() => ProcedureDefinitionPage.Create(source, 256, 256, "r0"));
        Assert.Throws<ArgumentException>(() => ProcedureDefinitionPage.Create(source, -1));
        Assert.Throws<ArgumentException>(() => ProcedureDefinitionPage.Create(source, 1001, 256, "r1"));
        Assert.Throws<ArgumentException>(() => ProcedureDefinitionPage.Create(source, 0, 128001));
    }

    [Fact]
    public void PageBoundaryPreservesSurrogatePairs()
    {
        var sql = new string('x', 255) + "😀tail";
        var source = new ProcedureDefinition("dbo", "Example", sql, "r1", true, true, []);
        var first = ProcedureDefinitionPage.Create(source, 0, 256);
        Assert.Equal(255, first.NextOffset);
        var last = ProcedureDefinitionPage.Create(source, first.NextOffset!.Value, 256, first.Revision);
        Assert.Equal(sql, first.Definition + last.Definition);
        Assert.True(last.Complete);
        Assert.Throws<ArgumentException>(() => ProcedureDefinitionPage.Create(source, 256, 256, "r1"));
    }
}
