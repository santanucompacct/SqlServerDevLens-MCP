using SqlServerDevLens.Core;
using SqlServerDevLens.Headless;
using Xunit;

namespace SqlServerDevLens.Headless.Tests;

public sealed class OperatorModeTests
{
    private static InstanceConfiguration Config() => new("operator-test", "localhost", 1433, "DisposableDb", "test-user");

    [Fact]
    public void DefaultReadOnlyInstanceStillAdvertisesOnlyTenTools()
    {
        var cfg = Config();
        using var database = new SqlDatabase(cfg.Profile(), "synthetic-only");
        var settings = new OperatorSettings("read-only", null, null, null);
        var names = settings.CreateTools(cfg, database, settings.NewAuthorization(), null, null)
            .Select(t => t.ProtocolTool.Name).ToArray();
        Assert.Equal(10, names.Length);
        Assert.True(names.ToHashSet().SetEquals(ReadOnlyCatalog.AllowedNames));
    }

    [Fact]
    public void LinuxFullModeReusesExistingPrepareApplyAndSqlInspectionTools()
    {
        var cfg = Config();
        var settings = new OperatorSettings("full", null, [], null);
        var grants = settings.NewAuthorization();
        using var database = new SqlDatabase(cfg.Profile(), "synthetic-only", authorization: grants);
        var names = settings.CreateTools(cfg, database, grants, null, null)
            .Select(t => t.ProtocolTool.Name).ToHashSet();
        Assert.Contains("prepare_procedure_execution", names);
        Assert.Contains("execute_prepared_procedure", names);
        Assert.Contains("prepare_procedure_change", names);
        Assert.Contains("prepare_table_sql", names);
        Assert.Contains("apply_table_sql", names);
        Assert.Contains("get_database_info", names);
        Assert.DoesNotContain("prepare_postman_run", names);
        Assert.All(grants.Snapshot().Values, x => Assert.False(x));
    }

    [Fact]
    public async Task AutomaticGrantsAreCategorySpecificAndExpire()
    {
        var settings = new OperatorSettings("full", DateTimeOffset.UtcNow.AddMilliseconds(150),
            ["procedure_execute", "postman_run"], null);
        var grants = settings.NewAuthorization();
        Assert.NotNull(grants.Grant("procedure_execute"));
        Assert.NotNull(grants.Grant("postman_run"));
        Assert.Null(grants.Grant("table_delete"));
        await OperatorSettings.ExpireAsync(grants, settings.AutomaticUntilUtc, CancellationToken.None);
        Assert.All(grants.Snapshot().Values, x => Assert.False(x));
    }

    [Fact]
    public void ExpiredGrantsCannotReenableAtStart()
    {
        var settings = new OperatorSettings("full", DateTimeOffset.UtcNow.AddHours(-1),
            ["table_update"], null);
        Assert.All(settings.NewAuthorization().Snapshot().Values, x => Assert.False(x));
    }
}
