using Microsoft.Data.SqlClient;
using SqlServerDevLens.Core;
using Xunit;

namespace SqlServerDevLens.Tests;

public class ConnectionTests
{
    private static ConnectionProfile Profile => new("localhost", 1433, "Example", AuthenticationMode.Sql, "example");
    [Fact] public void DefaultsAndEscapingAreSafe()
    {
        var builder = new SqlConnectionStringBuilder(SqlConnectionTester.BuildConnectionString(Profile, "fake;Password=injection"));
        Assert.Equal("fake;Password=injection", builder.Password);
        Assert.Equal("Example", builder.InitialCatalog);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.False(builder.TrustServerCertificate);
        Assert.False(builder.PersistSecurityInfo);
    }
    [Fact] public void WindowsAuthDoesNotIncludeSqlCredentials()
    {
        var builder = new SqlConnectionStringBuilder(SqlConnectionTester.BuildConnectionString(Profile with { Authentication = AuthenticationMode.Windows }, "fake"));
        Assert.True(builder.IntegratedSecurity);
        Assert.Empty(builder.Password);
        Assert.Empty(builder.UserID);
    }
    [Fact] public void MissingDatabaseAndInvalidPortAreRejected()
    {
        Assert.Throws<ArgumentException>(() => (Profile with { Database = " " }).Validate());
        Assert.Throws<ArgumentException>(() => (Profile with { Port = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => SqlConnectionTester.BuildConnectionString(Profile, ""));
    }
    [Fact] public async Task ProfileRoundTripContainsNoPasswordAndCanBeForgotten()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "connections.json");
        try
        {
            var store = new JsonProfileStore(path);
            await store.SaveAsync(Profile);
            Assert.Equal(Profile, await store.LoadAsync());
            Assert.DoesNotContain("password", (await File.ReadAllTextAsync(path)).ToLowerInvariant());
            await store.DeleteAsync();
            Assert.Null(await store.LoadAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task SecureStorageFailsClosed()
    {
        var store = new UnavailableCredentialStore();
        Assert.False(store.IsAvailable);
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveAsync("test", "fake"));
    }
}
