using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class SqlToolsTests
{
    [Theory]
    [InlineData("SELECT 1 AS value")]
    [InlineData("SELECT TOP (10) c.Id, COUNT(*) AS n FROM dbo.Customers c GROUP BY c.Id ORDER BY n DESC")]
    [InlineData("WITH c AS (SELECT Id FROM dbo.Customers) SELECT * FROM c")]
    [InlineData("SELECT 'DELETE FROM dbo.T' AS label /* harmless comment */")]
    [InlineData("SELECT CAST(1 AS decimal(10,2)), CONVERT(date, GETDATE()), COALESCE(NULL, 1)")]
    [InlineData("SELECT ROW_NUMBER() OVER (ORDER BY Id) AS n FROM dbo.T")]
    [InlineData("SELECT * FROM (VALUES (1),(2)) AS t(n)")]
    public void AcceptsSupportedReadOnlySql(string sql) => ReadOnlySql.Validate(sql);

    [Theory]
    [InlineData("DELETE FROM dbo.T")]
    [InlineData("SELECT 1; DROP TABLE dbo.T")]
    [InlineData("SELECT * INTO dbo.Copy FROM dbo.T")]
    [InlineData("SELECT NEXT VALUE FOR dbo.Seq")]
    [InlineData("USE Other; SELECT 1")]
    [InlineData("SELECT * FROM Other.dbo.T")]
    [InlineData("SELECT * FROM [server].[db].[dbo].[T]")]
    [InlineData("SELECT * FROM OPENQUERY(linked, 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENROWSET(BULK 'C:\\private.txt', SINGLE_CLOB) AS x")]
    [InlineData("SELECT * FROM dbo.CustomFunction()")]
    [InlineData("SELECT dbo.CustomFunction(1)")]
    [InlineData("SELECT * FROM dbo.T WITH (UPDLOCK)")]
    [InlineData("SELECT 1 OPTION (QUERYTRACEON 460)")]
    [InlineData("SELECT @x=1")]
    [InlineData("SELECT @@SERVERNAME")]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("WITH x AS (SELECT Id FROM dbo.T) DELETE FROM x")]
    [InlineData("SELECT * FROM #Temp")]
    [InlineData("SELECT DB_NAME(1)")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT * FROM sys.server_principals")]
    [InlineData("SELECT * FROM sys.dm_exec_sessions")]
    [InlineData("SELECT * FROM sysprocesses")]
    public void RejectsUnsupportedOrUnsafeSql(string sql) => Assert.Throws<ArgumentException>(() => ReadOnlySql.Validate(sql));

    [Fact]
    public async Task InvalidQueriesAndLimitsAreRejectedBeforeConnecting()
    {
        using var database = new SqlDatabase(new("invalid", 1433, "Example", AuthenticationMode.Sql, "fake"), "fake");
        await Assert.ThrowsAsync<ArgumentException>(() => database.QueryAsync("DROP TABLE dbo.T", 1, default));
        await Assert.ThrowsAsync<ArgumentException>(() => database.QueryAsync("SELECT 1", 501, default));
        await Assert.ThrowsAsync<ArgumentException>(() => database.DescribeObjectAsync("", "T", default));
        database.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.QueryAsync("SELECT 1", 1, default));
    }

    [Fact]
    public async Task StdioForwardsSqlArgumentsAndStopDropsContextWithoutStaleResults()
    {
        var name = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(name);
        var database = new FakeDatabase();
        await server.StartAsync(DateTimeOffset.UtcNow, database);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SqlServerDevLens.slnx"))) root = root.Parent;
        var dll = Path.Combine(root!.FullName, "SqlServerDevLens.McpHost", "bin", "Release", "net10.0", "SqlServerDevLens.McpHost.dll");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = "dotnet", Arguments = [dll, "--desktop", "--pipe-name", name] }), cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(5, tools.Count);
        var status = await client.CallToolAsync("get_server_status", cancellationToken: timeout.Token);
        Assert.Contains("\"sqlConfigured\":true", Assert.IsType<TextContentBlock>(status.Content[0]).Text);
        foreach (var tool in new[] { "get_database_info", "list_database_objects", "describe_table", "query_read_only" })
        {
            var args = tool switch
            {
                "describe_table" => new Dictionary<string, object?> { ["schema"] = "sales", ["name"] = "Orders" },
                "query_read_only" => new Dictionary<string, object?> { ["sql"] = "SELECT 1", ["maxRows"] = 7 },
                _ => null
            };
            var result = await client.CallToolAsync(tool, args, cancellationToken: timeout.Token);
            Assert.False(result.IsError ?? false);
            Assert.Contains("Selected", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        }
        Assert.Equal(("sales", "Orders"), database.Described);
        Assert.Equal(("SELECT 1", 7), database.Query);
        await server.StopAsync(); Assert.True(database.Disposed);
        var stopped = await client.CallToolAsync("query_read_only", new Dictionary<string, object?> { ["sql"] = "SELECT 1" }, cancellationToken: timeout.Token);
        Assert.True(stopped.IsError);
        Assert.DoesNotContain("Selected", Assert.IsType<TextContentBlock>(stopped.Content[0]).Text);
        await server.StartAsync(DateTimeOffset.UtcNow, new FakeDatabase());
        var recovered = await client.CallToolAsync("get_database_info", cancellationToken: timeout.Token);
        Assert.False(recovered.IsError ?? false);
    }

    private sealed class FakeDatabase : ISqlDatabase
    {
        public string Database => "Selected";
        public bool Disposed;
        public (string, string) Described;
        public (string, int) Query;
        private Task<SqlResult> Result() => Task.FromResult(new SqlResult(Database, [new("value", "int")], [new object?[] { 1 }], false, false));
        public Task<SqlResult> GetDatabaseInfoAsync(CancellationToken cancellationToken) => Result();
        public Task<SqlResult> ListObjectsAsync(CancellationToken cancellationToken) => Result();
        public Task<SqlResult> DescribeObjectAsync(string schema, string name, CancellationToken cancellationToken) { Described = (schema, name); return Result(); }
        public Task<SqlResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken) { Query = (sql, maxRows); return Result(); }
        public void Dispose() => Disposed = true;
    }
}

public class SavedProfileFactAttribute : FactAttribute
{
    public SavedProfileFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DEVLENS_TEST_SAVED_PROFILE") != "1")
            Skip = "Opt-in live test: set DEVLENS_TEST_SAVED_PROFILE=1. Uses the current user's saved profile and secure credential; never prints them.";
    }
}

public class LiveSqlTests
{
    [SavedProfileFact]
    public async Task SelectedDatabaseReadOnlyOperationsAndBounds()
    {
        var profile = await JsonProfileStore.CreateDefault().LoadAsync();
        Assert.NotNull(profile);
        var credentials = CredentialStores.CreateDefault();
        var password = profile.Authentication == AuthenticationMode.Sql
            ? await credentials.ReadAsync(CredentialStores.ProfileId(profile)) : null;
        using var database = new SqlDatabase(profile, password);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var info = await database.GetDatabaseInfoAsync(timeout.Token);
        Assert.Equal(profile.Database, info.Database);
        Assert.Single(info.Rows);
        var objects = await database.ListObjectsAsync(timeout.Token);
        Assert.True(objects.Rows.Count <= 1000);
        var table = objects.Rows.FirstOrDefault(r => r[2]?.ToString() is "USER_TABLE" or "VIEW");
        if (table is not null)
        {
            var columns = await database.DescribeObjectAsync(table[0]!.ToString()!, table[1]!.ToString()!, timeout.Token);
            Assert.NotEmpty(columns.Rows);
        }
        var result = await database.QueryAsync("SELECT n FROM (VALUES (1),(2),(3)) t(n) ORDER BY n", 2, timeout.Token);
        Assert.Equal(2, result.Rows.Count); Assert.True(result.Truncated);
        var values = await database.QueryAsync("SELECT CAST(NULL AS int) n, REPLICATE(CAST('x' AS varchar(max)),3000) text_value, CAST(0x1234 AS varbinary(max)) binary_value", 1, timeout.Token);
        Assert.Null(values.Rows[0][0]); Assert.Equal(2000, ((string)values.Rows[0][1]!).Length);
        Assert.True(values.ValuesTruncated);
        await Assert.ThrowsAsync<ArgumentException>(() => database.QueryAsync("DELETE FROM dbo.T", 1, timeout.Token));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.QueryAsync("SELECT 1", 1, cancelled.Token));

        // End-to-end through the same stdio adapter used by the plugin. Credentials
        // stay in this desktop-owned context; only the synthetic result crosses MCP.
        var pipe = "dvt-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(pipe);
        await server.StartAsync(DateTimeOffset.UtcNow, new SqlDatabase(profile, password));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SqlServerDevLens.slnx"))) root = root.Parent;
        var dll = Path.Combine(root!.FullName, "SqlServerDevLens.McpHost", "bin", "Release", "net10.0", "SqlServerDevLens.McpHost.dll");
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        { Command = "dotnet", Arguments = [dll, "--desktop", "--pipe-name", pipe] }), cancellationToken: timeout.Token);
        var synthetic = await client.CallToolAsync("query_read_only", new Dictionary<string, object?> { ["sql"] = "SELECT 1 AS connection_verified" }, cancellationToken: timeout.Token);
        Assert.False(synthetic.IsError ?? false);
        Assert.Contains("connection_verified", Assert.IsType<TextContentBlock>(synthetic.Content[0]).Text);
        var rejected = await client.CallToolAsync("query_read_only", new Dictionary<string, object?> { ["sql"] = "DELETE FROM dbo.T" }, cancellationToken: timeout.Token);
        Assert.True(rejected.IsError);
        Assert.Contains("SELECT", Assert.IsType<TextContentBlock>(rejected.Content[0]).Text);
    }
}
