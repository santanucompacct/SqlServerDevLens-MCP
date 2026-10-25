using SqlServerDevLens.Core;
using SqlServerDevLens.Headless;
using Xunit;

namespace SqlServerDevLens.Headless.Tests;

public sealed class IsolationTests
{
    [Fact]
    public void CredentialReadAcceptsOnlyPrivateFilesAndRejectsWorldReadableSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "credential-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sql-password");
        var previous = Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY");
        try
        {
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(path, "synthetic-only\n");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead);
            Environment.SetEnvironmentVariable("CREDENTIALS_DIRECTORY", directory);
            Assert.Equal("synthetic-only", InstanceConfiguration.ReadPassword());
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.OtherRead);
                Assert.Throws<ArgumentException>(() => InstanceConfiguration.ReadPassword());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CREDENTIALS_DIRECTORY", previous);
            File.Delete(path); Directory.Delete(directory);
        }
    }
    private static InstanceConfiguration Config(string id, string db) => new(id, "localhost", 1433, db, "reader");
    [Fact]
    public void TwoCatalogsOnlyExposeTheTenReadToolsAndKeepCapturesSeparate()
    {
        var a = Config("tenant-a", "DatabaseA"); var b = Config("tenant-b", "DatabaseB");
        using var dbA = new SqlDatabase(a.Profile(), "synthetic-not-a-real-password");
        using var dbB = new SqlDatabase(b.Profile(), "different-synthetic-password");
        Assert.Equal("DatabaseA", dbA.Database); Assert.Equal("DatabaseB", dbB.Database);
        foreach (var (config, database) in new[] { (a, dbA), (b, dbB) })
        {
            var names = ReadOnlyCatalog.Create(config, database).Select(t => t.ProtocolTool.Name).ToHashSet();
            Assert.Equal(10, names.Count); Assert.True(names.SetEquals(ReadOnlyCatalog.AllowedNames));
            Assert.DoesNotContain(names, n => n.Contains("prepare") || n.Contains("apply") || n.Contains("postman") || n.Contains("execute"));
            Assert.Null(database.Authorization);
        }
        using var reservation = dbA.Results.Reserve();
        var reference = reservation.Save(new { value = new string('x', 40000) });
        Assert.Throws<ArgumentException>(() => dbB.Results.Read(reference.ResultId));
        Assert.NotEmpty(dbA.Results.Read(reference.ResultId).Json);
        dbA.Dispose();
        Assert.Throws<InvalidOperationException>(() => dbA.Results.Read(reference.ResultId));
    }
    [Theory]
    [InlineData("../tenant")]
    [InlineData("UPPERCASE")]
    [InlineData("tenant a")]
    public void UnsafeInstanceNamesFailClosed(string name) => Assert.Throws<ArgumentException>(() => Config(name, "DB").Profile());
    [Fact]
    public void ConfigPinsTlsAndRejectsSecretOrAuthorizationOverrides()
    {
        var config = Config("example", "DB");
        Assert.True(config.Profile().Encrypt); Assert.False(config.Profile().TrustServerCertificate);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "example"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "instance.json");
        try
        {
            File.WriteAllText(file, """{"instanceId":"example","host":"localhost","port":1433,"database":"DB","username":"reader","password":"do-not-accept"}""");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Throws<System.Text.Json.JsonException>(() => InstanceConfiguration.Load(file));
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(config));
            Assert.Equal(config, InstanceConfiguration.Load(file));
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead);
                Assert.Throws<ArgumentException>(() => InstanceConfiguration.Load(file));
            }
        }
        finally { File.Delete(file); Directory.Delete(dir); Directory.Delete(Path.GetDirectoryName(dir)!); }
    }
    [Theory]
    [InlineData("DELETE FROM dbo.Example WHERE id=1")]
    [InlineData("UPDATE dbo.Example SET id=2 WHERE id=1")]
    [InlineData("INSERT dbo.Example VALUES(1)")]
    [InlineData("EXEC dbo.Example")]
    [InlineData("ALTER TABLE dbo.Example ADD x int")]
    [InlineData("CREATE TABLE dbo.Example(id int)")]
    [InlineData("SELECT * FROM AnotherDatabase.dbo.Example")]
    public void SharedQueryGuardRejectsWritesAndDatabaseSwitching(string sql) => Assert.Throws<ArgumentException>(() => ReadOnlySql.Validate(sql));
}
