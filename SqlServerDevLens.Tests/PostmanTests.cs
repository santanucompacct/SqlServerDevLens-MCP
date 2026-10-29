using System.Text.Json;
using ModelContextProtocol.Protocol;
using SqlServerDevLens.Core;
using SqlServerDevLens.Desktop.ViewModels;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public sealed class PostmanSimulationFactAttribute : FactAttribute
{
    public PostmanSimulationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DEVLENS_POSTMAN_SIMULATION_URL") is null)
            Skip = "Requires the local Postman CLI simulation; never uses a real key or workspace.";
    }
}
public class PostmanTests
{
    [Fact]
    public async Task MetadataToolsUseDesktopSnapshotAndClearOnStop()
    {
        var pipe = "dvp-" + Guid.NewGuid().ToString("N");
        await using var server = new DesktopMcpServer(pipe);
        var selection = new PostmanSelection(new("workspace", "Workspace"), new("collection", "Collection"),
            [new("Read", "GET")], ["token"], DateTimeOffset.UtcNow);
        await server.StartAsync(DateTimeOffset.UtcNow, postman: selection);
        var adapter = new DesktopStatusClient(pipe);
        var status = await adapter.GetStatusAsync();
        Assert.Contains("\"postmanConfigured\":true", ((TextContentBlock)status.Content[0]).Text);
        var result = await adapter.CallAsync("list_postman_requests", null);
        Assert.NotEqual(true, result.IsError); Assert.Contains("Read", ((TextContentBlock)result.Content[0]).Text);
        await server.StopAsync(); await server.StartAsync(DateTimeOffset.UtcNow);
        result = await adapter.CallAsync("get_postman_selection", null);
        Assert.True(result.IsError);
        Assert.DoesNotContain("\"postmanConfigured\":true", ((TextContentBlock)(await adapter.GetStatusAsync()).Content[0]).Text);
    }
    [PostmanSimulationFact]
    public async Task RealHttpClientAgainstPostmanSimulation()
    {
        using var client = new PostmanClient(new Uri(Environment.GetEnvironmentVariable("DEVLENS_POSTMAN_SIMULATION_URL")!));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var workspace = Assert.Single(await client.WorkspacesAsync("fixture-valid", timeout.Token));
        var collection = Assert.Single(await client.CollectionsAsync("fixture-valid", workspace.Id, timeout.Token));
        Assert.Equal("123-collection-1", collection.Id);
        var selection = await client.SelectAsync("fixture-valid", workspace, collection, timeout.Token);
        Assert.Equal(new[] {"GET", "POST"}, selection.Requests.Select(x => x.Method));
        Assert.Equal(new[] {"baseUrl", "token"}, selection.VariableNames);
        Assert.Equal("Folder / Read", selection.Requests[0].Name);
        var serialized = JsonSerializer.Serialize(selection);
        Assert.DoesNotContain("SECRET-", serialized); Assert.DoesNotContain("private.invalid", serialized);
        Assert.DoesNotContain("fixture-valid", serialized); Assert.DoesNotContain("SCRIPTS-MUST-NOT-RUN", serialized);
        foreach (var key in new[] {"unauthorized", "forbidden", "rate-limit", "server-error", "redirect", "malformed"})
        {
            var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => client.WorkspacesAsync("fixture-" + key, timeout.Token));
            Assert.DoesNotContain("SECRET-", error.Message); Assert.DoesNotContain("fixture-", error.Message);
        }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.WorkspacesAsync("fixture-hang", cancel.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CollectionsAsync("fixture-valid", "../escape", timeout.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SelectAsync("fixture-valid", workspace, new("other", "Other"), timeout.Token));
    }

    [Fact]
    public async Task SetupRequiresFreshSelectionsAndSecurePersistenceBeforeReady()
    {
        var client = new FakeClient(); var credentials = new Credentials();
        using var model = new PostmanViewModel(client, credentials); var ready = 0;
        model.Ready += _ => { ready++; return Task.CompletedTask; };
        model.ApiKey = "fake-key"; model.RememberKey = true;
        await model.StartCommand.ExecuteAsync(null); Assert.Equal(0, ready);
        await Select(model);
        credentials.FailSave = true;
        await model.StartCommand.ExecuteAsync(null); Assert.Equal(0, ready); Assert.Contains("storage failed", model.Status);
        credentials.FailSave = false;
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, ready); Assert.Equal("fake-key", credentials.Key); Assert.Empty(model.ApiKey);
        await model.LoadSavedKeyAsync(); Assert.Equal("fake-key", model.ApiKey);
        await Select(model);
        model.ApiKey = "changed"; Assert.Empty(model.Workspaces); Assert.Null(model.SelectedCollection);
        await model.StartCommand.ExecuteAsync(null); Assert.Equal(1, ready);
        await Select(model); model.SelectedWorkspace = null; Assert.Empty(model.Collections);
        await model.ForgetKeyCommand.ExecuteAsync(null); Assert.Null(credentials.Key); Assert.Empty(model.ApiKey);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupModePreservesSqlOnlyAndGatesCombinedMode(bool combined)
    {
        var runtime = new Server(); var credentials = new Credentials();
        using var postman = new PostmanViewModel(new FakeClient(), credentials);
        var connection = new ConnectionViewModel(new Tester(), new Profiles(), new UnavailableCredentialStore())
            { Host = "localhost", Database = "Example", WindowsAuthentication = true };
        var model = new MainViewModel(connection, new ServerViewModel(runtime), postman);
        model.IsSqlWithPostmanSelected = combined;
        await model.ContinueCommand.ExecuteAsync(null); await connection.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal(combined, model.IsPostmanStepVisible); Assert.Equal(!combined, runtime.IsRunning);
        if (combined)
        {
            postman.ApiKey = "fake-key"; await Select(postman); await postman.StartCommand.ExecuteAsync(null);
            Assert.True(runtime.IsRunning); Assert.NotNull(runtime.Selection); Assert.True(model.IsServerStepVisible);
        }
        else Assert.Null(runtime.Selection);
        await runtime.StopAsync();
    }
    [Fact]
    public async Task BackFromPostmanDiscardsSqlSessionAndRequiresAnotherSqlTest()
    {
        var runtime = new Server(); using var postman = new PostmanViewModel(new FakeClient(), new Credentials());
        var connection = new ConnectionViewModel(new Tester(), new Profiles(), new UnavailableCredentialStore())
            { Host = "localhost", Database = "Example", WindowsAuthentication = true };
        var model = new MainViewModel(connection, new ServerViewModel(runtime), postman) { IsSqlWithPostmanSelected = true };
        await model.ContinueCommand.ExecuteAsync(null); await connection.TestConnectionCommand.ExecuteAsync(null);
        postman.ApiKey = "fake-key"; await Select(postman);
        await model.BackFromPostmanCommand.ExecuteAsync(null);
        Assert.True(model.IsConnectionStepVisible); Assert.Empty(postman.ApiKey); Assert.False(runtime.IsRunning);
        await postman.StartCommand.ExecuteAsync(null); Assert.False(runtime.IsRunning);
    }
    [Fact]
    public void EndpointRejectsUntrustedOrigins() => Assert.Throws<ArgumentException>(() => new PostmanClient(new Uri("https://evil.invalid/")));
    private static async Task Select(PostmanViewModel model)
    {
        await model.LoadWorkspacesCommand.ExecuteAsync(null); model.SelectedWorkspace = model.Workspaces[0];
        await model.LoadCollectionsCommand.ExecuteAsync(null); model.SelectedCollection = model.Collections[0];
    }
    private sealed class FakeClient : IPostmanClient
    {
        public Task<PostmanChoice[]> WorkspacesAsync(string key, CancellationToken token) => Task.FromResult<PostmanChoice[]>([new("workspace", "Workspace")]);
        public Task<PostmanChoice[]> CollectionsAsync(string key, string id, CancellationToken token) => Task.FromResult<PostmanChoice[]>([new("collection", "Collection")]);
        public Task<PostmanSelection> SelectAsync(string key, PostmanChoice workspace, PostmanChoice collection, CancellationToken token) =>
            Task.FromResult(new PostmanSelection(workspace, collection, [new("Request", "GET")], ["token"], DateTimeOffset.UtcNow));
    }
    private sealed class Credentials : ICredentialStore
    {
        public bool IsAvailable => true; public string? Key; public bool FailSave;
        public Task SaveAsync(string id, string key, CancellationToken token = default) { if (FailSave) throw new IOException(); Key = key; return Task.CompletedTask; }
        public Task<string?> ReadAsync(string id, CancellationToken token = default) => Task.FromResult(Key);
        public Task DeleteAsync(string id, CancellationToken token = default) { Key = null; return Task.CompletedTask; }
    }
    private sealed class Tester : IConnectionTester
    { public Task<ConnectionTestResult> TestAsync(ConnectionProfile p, string? password, CancellationToken cancellationToken = default) => Task.FromResult(new ConnectionTestResult(true, "SQL test succeeded")); }
    private sealed class Profiles : IProfileStore
    {
        public Task<ConnectionProfile?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<ConnectionProfile?>(null);
        public Task SaveAsync(ConnectionProfile p, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Server : IDesktopMcpServer
    {
        public ServerActivityLog Activity { get; } = new();
        public bool IsRunning { get; private set; } public PostmanSelection? Selection;
        public event Action? StoppedUnexpectedly { add {} remove {} }
        public Task StartAsync(DateTimeOffset at, ISqlDatabase? db = null, PostmanSelection? postman = null, PostmanWorkflow? postmanActions = null) { Selection = postman; IsRunning = true; db?.Dispose(); return Task.CompletedTask; }
        public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
