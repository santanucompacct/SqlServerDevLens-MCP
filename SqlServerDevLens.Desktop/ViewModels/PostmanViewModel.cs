using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlServerDevLens.Core;

namespace SqlServerDevLens.Desktop.ViewModels;

public partial class PostmanViewModel : ViewModelBase, IDisposable
{
    private readonly IPostmanClient _client;
    private readonly ICredentialStore _credentials;
    private readonly CancellationTokenSource _shutdown = new();
    private const string CredentialId = "PostmanApiKey";
    private int _revision;
    private string? _workspaceKey;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _rememberKey;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Enter your Postman API key, then load workspaces. No API requests will be executed.";
    [ObservableProperty] private PostmanChoice? _selectedWorkspace;
    [ObservableProperty] private PostmanChoice? _selectedCollection;
    public ObservableCollection<PostmanChoice> Workspaces { get; } = [];
    public ObservableCollection<PostmanChoice> Collections { get; } = [];
    public bool IsEditable => !IsBusy;
    public bool CanRememberKey => _credentials.IsAvailable;
    public string StorageHint => CanRememberKey ? "Remember key uses Windows Credential Manager. No key is saved in JSON."
        : "Secure key saving is unavailable on this OS. The key is used only for this setup session.";
    public event Func<PostmanSelection, Task>? Ready;
    public PostmanViewModel() : this(new PostmanClient(), CredentialStores.CreateDefault()) { }
    public PostmanViewModel(IPostmanClient client, ICredentialStore credentials) { _client = client; _credentials = credentials; }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEditable));
    partial void OnApiKeyChanged(string value) { _revision++; _workspaceKey = null; SelectedWorkspace = null; Workspaces.Clear(); Collections.Clear(); SelectedCollection = null; }
    partial void OnSelectedWorkspaceChanged(PostmanChoice? value) { _revision++; SelectedCollection = null; Collections.Clear(); }
    public async Task LoadSavedKeyAsync()
    {
        try
        {
            if (CanRememberKey) { ApiKey = await _credentials.ReadAsync(CredentialId, _shutdown.Token) ?? ""; RememberKey = ApiKey.Length > 0; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        { Status = "Could not load the saved key. Enter it again."; }
    }
    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy || _shutdown.IsCancellationRequested) return;
        IsBusy = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { await operation(timeout.Token); }
        catch (OperationCanceledException) { Status = "Postman setup cancelled or timed out. Load again."; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception or NotSupportedException)
        { Status = e is ArgumentException or InvalidOperationException ? e.Message : "Secure key storage failed. No plaintext key was saved. Try again or turn off Remember key."; }
        finally { IsBusy = false; }
    }
    [RelayCommand]
    private Task LoadWorkspacesAsync() => RunAsync(async token =>
    {
        var key = ApiKey; SelectedWorkspace = null; Workspaces.Clear(); _workspaceKey = null; var revision = _revision;
        Status = "Loading Postman workspaces…";
        var list = await _client.WorkspacesAsync(key, token);
        if (revision != _revision || key != ApiKey) return;
        foreach (var item in list) Workspaces.Add(item);
        _workspaceKey = key;
        Status = list.Length == 0 ? "No accessible workspaces. Check Postman account permissions." : "Select a workspace, then Load Collections.";
    });
    [RelayCommand]
    private Task LoadCollectionsAsync() => RunAsync(async token =>
    {
        var workspace = SelectedWorkspace; var key = ApiKey;
        if (workspace is null || _workspaceKey != key || !Workspaces.Contains(workspace)) throw new ArgumentException("Load workspaces and select one first.");
        SelectedCollection = null; Collections.Clear(); var revision = _revision;
        Status = "Loading collections…";
        var list = await _client.CollectionsAsync(key, workspace.Id, token);
        if (revision != _revision || key != ApiKey) return;
        foreach (var item in list) Collections.Add(item);
        Status = list.Length == 0 ? "No collections in this workspace." : "Select a collection, then Start MCP Server.";
    });
    [RelayCommand]
    private Task StartAsync() => RunAsync(async token =>
    {
        var workspace = SelectedWorkspace; var collection = SelectedCollection; var key = ApiKey; var revision = _revision;
        if (workspace is null || collection is null || _workspaceKey != key || !Collections.Contains(collection))
            throw new ArgumentException("Load and select a workspace and collection first.");
        Status = "Validating selected collection…";
        var selection = await _client.SelectAsync(key, workspace, collection, token);
        if (revision != _revision || key != ApiKey || collection != SelectedCollection) return;
        if (RememberKey)
        {
            if (!CanRememberKey) throw new InvalidOperationException("Secure key storage is unavailable on this OS.");
            await _credentials.SaveAsync(CredentialId, key, token);
        }
        else if (CanRememberKey) await _credentials.DeleteAsync(CredentialId, token);
        Status = "Postman collection validated. Changes and HTTP runs require explicit approval in DevLens.";
        if (Ready is not null) await Ready(selection);
        ApiKey = "";
    });
    [RelayCommand]
    private Task ForgetKeyAsync() => RunAsync(async token =>
    {
        if (CanRememberKey) await _credentials.DeleteAsync(CredentialId, token);
        ApiKey = ""; RememberKey = false; Status = "Saved Postman key removed.";
    });
    public void ClearSession() { ApiKey = ""; }
    public void Dispose() { _shutdown.Cancel(); ApiKey = ""; if (_client is IDisposable disposable) disposable.Dispose(); _shutdown.Dispose(); }
}
