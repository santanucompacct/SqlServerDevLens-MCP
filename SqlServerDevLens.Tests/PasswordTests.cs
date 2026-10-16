using SqlServerDevLens.Core;
using SqlServerDevLens.Desktop.ViewModels;
using Xunit;

namespace SqlServerDevLens.Tests;

public class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows Credential Manager requires Windows."; }
}

public class PasswordTests
{
    [WindowsFact]
    public async Task NativeCredentialRoundTripUpdateAndDelete()
    {
        var store = new WindowsCredentialStore();
        var id = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(await store.ReadAsync(id));
            await store.SaveAsync(id, "fake-π-password");
            Assert.Equal("fake-π-password", await store.ReadAsync(id));
            await store.SaveAsync(id, "replacement");
            Assert.Equal("replacement", await store.ReadAsync(id));
            await store.DeleteAsync(id);
            Assert.Null(await store.ReadAsync(id));
            await store.DeleteAsync(id);
        }
        finally { await store.DeleteAsync(id); }
    }

    [Fact]
    public async Task OptInReloadAndForgetManageBothStores()
    {
        var profiles = new MemoryProfiles(); var credentials = new MemoryCredentials();
        var vm = Create(profiles, credentials);
        vm.RememberPassword = true;
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Empty(vm.Password);
        Assert.Single(credentials.Values);
        var loaded = new ConnectionViewModel(new Tester(), profiles, credentials);
        await loaded.LoadAsync();
        Assert.Equal("fake", loaded.Password);
        Assert.True(loaded.RememberPassword);
        loaded.Host = "other-host";
        Assert.Empty(loaded.Password);
        await loaded.ForgetProfileCommand.ExecuteAsync(null);
        Assert.Null(profiles.Profile);
        Assert.Empty(credentials.Values);
    }

    [Fact]
    public async Task OptOutRemovesPreviouslyRememberedPassword()
    {
        var profiles = new MemoryProfiles(); var credentials = new MemoryCredentials();
        var vm = Create(profiles, credentials); vm.RememberPassword = true;
        await vm.TestConnectionCommand.ExecuteAsync(null);
        await vm.LoadAsync(); vm.RememberPassword = false;
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Empty(credentials.Values);
        Assert.NotNull(profiles.Profile);
    }

    [Fact]
    public async Task FailedSqlTestDoesNotPersistPassword()
    {
        var profiles = new MemoryProfiles(); var credentials = new MemoryCredentials();
        var vm = new ConnectionViewModel(new Tester(false), profiles, credentials)
        { Host = "localhost", Database = "Example", Username = "user", RememberProfile = true, RememberPassword = true, Password = "fake" };
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Null(profiles.Profile); Assert.Empty(credentials.Values); Assert.Empty(vm.Password);
    }

    [Fact]
    public async Task CredentialFailureNeverClaimsPasswordSaved()
    {
        var profiles = new MemoryProfiles(); var credentials = new MemoryCredentials { FailWrite = true };
        var vm = Create(profiles, credentials); vm.RememberPassword = true;
        await vm.TestConnectionCommand.ExecuteAsync(null);
        Assert.Contains("could not be completed", vm.Status);
        Assert.Empty(credentials.Values); Assert.Empty(vm.Password);
    }
    private static ConnectionViewModel Create(MemoryProfiles profiles, MemoryCredentials credentials) => new(new Tester(), profiles, credentials)
    { Host = "localhost", Database = "Example", Username = "user", RememberProfile = true, Password = "fake" };
    private sealed class Tester(bool success = true) : IConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(ConnectionProfile profile, string? password, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectionTestResult(success, success ? "Connected." : "Failed."));
    }
    private sealed class MemoryProfiles : IProfileStore
    {
        public ConnectionProfile? Profile;
        public Task<ConnectionProfile?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Profile);
        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) { Profile = profile; return Task.CompletedTask; }
        public Task DeleteAsync(CancellationToken cancellationToken = default) { Profile = null; return Task.CompletedTask; }
    }
    private sealed class MemoryCredentials : ICredentialStore
    {
        public bool IsAvailable => true;
        public bool FailWrite;
        public Dictionary<string, string> Values = new();
        public Task<string?> ReadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(Values.GetValueOrDefault(profileId));
        public Task SaveAsync(string profileId, string password, CancellationToken cancellationToken = default)
        { if (FailWrite) throw new IOException(); Values[profileId] = password; return Task.CompletedTask; }
        public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default) { Values.Remove(profileId); return Task.CompletedTask; }
    }
}
