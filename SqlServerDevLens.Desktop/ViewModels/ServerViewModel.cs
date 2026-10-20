using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;

namespace SqlServerDevLens.Desktop.ViewModels;

public partial class ServerViewModel : ViewModelBase
{
    private readonly IDesktopMcpServer _server;
    public SessionAuthorization Authorization { get; } = new();
    [ObservableProperty] private bool _autoProcedureExecution;
    partial void OnAutoProcedureExecutionChanged(bool value) => Authorization.Set("procedure_execute", value);
    [ObservableProperty] private bool _autoProcedureCreate;
    partial void OnAutoProcedureCreateChanged(bool value) => Authorization.Set("procedure_create", value);
    [ObservableProperty] private bool _autoProcedureAlter;
    partial void OnAutoProcedureAlterChanged(bool value) => Authorization.Set("procedure_alter", value);
    [ObservableProperty] private bool _autoTableCreate;
    partial void OnAutoTableCreateChanged(bool value) => Authorization.Set("table_create", value);
    [ObservableProperty] private bool _autoTableAlter;
    partial void OnAutoTableAlterChanged(bool value) => Authorization.Set("table_alter", value);
    [ObservableProperty] private bool _autoInsert;
    partial void OnAutoInsertChanged(bool value) => Authorization.Set("table_insert", value);
    [ObservableProperty] private bool _autoUpdate;
    partial void OnAutoUpdateChanged(bool value) => Authorization.Set("table_update", value);
    [ObservableProperty] private bool _autoDelete;
    partial void OnAutoDeleteChanged(bool value) => Authorization.Set("table_delete", value);
    [ObservableProperty] private bool _autoPostmanRun;
    partial void OnAutoPostmanRunChanged(bool value) => Authorization.Set("postman_run", value);
    [ObservableProperty] private bool _autoPostmanChange;
    partial void OnAutoPostmanChangeChanged(bool value) => Authorization.Set("postman_change", value);
    private void ResetAuthorization()
    {
        AutoProcedureExecution = false;
        AutoProcedureCreate = false;
        AutoProcedureAlter = false;
        AutoTableCreate = false;
        AutoTableAlter = false;
        AutoInsert = false;
        AutoUpdate = false;
        AutoDelete = false;
        AutoPostmanRun = false;
        AutoPostmanChange = false;
        Authorization.Reset();
    }

    public ObservableCollection<ServerActivity> ActivityEntries { get; } = [];
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Stopped";
    [ObservableProperty] private string _connectionSummary = "";
    [ObservableProperty] private string _connectionNotice = "";
    [ObservableProperty] private string _postmanSummary = "Postman disabled — SQL Server Only.";
    public bool CanStop => IsRunning && !IsBusy;
    public bool CanReturn => !IsRunning && !IsBusy;
    public Action<ProcedureReview, Action<bool>, System.Threading.CancellationToken>? RequestProcedureReview { get; set; }
    public ServerViewModel() : this(new DesktopMcpServer()) { }
    public ServerViewModel(IDesktopMcpServer server)
    {
        _server = server;
        Authorization.Changed += (category, enabled) => _server.Activity.Add("Session authorization", enabled ? "Enabled" : "Disabled", category + " · local operator setting; resets on Stop.");
        Authorization.Used += category => _server.Activity.Add("Session authorization", "Auto-authorized", category + " · exact validated request staged; applying is a separate single-use call.");
        _server.Activity.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            ActivityEntries.Clear();
            foreach (var entry in _server.Activity.Snapshot()) ActivityEntries.Add(entry);
        });
        _server.StoppedUnexpectedly += () => Dispatcher.UIThread.Post(() =>
        { ResetAuthorization(); IsRunning = false; Status = "Server stopped unexpectedly. Return to connection setup and test again."; });
    }
    partial void OnIsRunningChanged(bool value) { OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanReturn)); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanReturn)); }
    public async Task StartAsync(ConnectionProfile profile, string notice, string? password = null, PostmanSelection? postman = null, string? postmanKey = null)
    {
        ResetAuthorization(); IsBusy = true; Status = "Starting MCP server…";
        ConnectionSummary = $"{profile.Host}:{profile.Port} / {profile.Database}";
        ConnectionNotice = notice;
        PostmanSummary = postman is null ? "Postman disabled — SQL Server Only."
            : $"Postman: {postman.Workspace.Name} / {postman.Collection.Name} — {postman.Requests.Length} startup requests. Actions require manual approval unless their session checkbox is enabled.";
        try
        {
            var database = new SqlDatabase(profile, password, (request, decision, token) =>
            {
                if (RequestProcedureReview is null) { decision(false); return; }
                Review(request, decision, token);
            }, Authorization);
            PostmanWorkflow? postmanActions = postman is not null && !string.IsNullOrEmpty(postmanKey)
                ? new PostmanWorkflow(new PostmanActionBackend(postmanKey, postman), (request, decision, token) =>
                    { if(RequestProcedureReview is null) { decision(false); return; } Review(request, decision, token); }, authorization: Authorization, results: database.Results) : null;
            try { await _server.StartAsync(DateTimeOffset.UtcNow, database, postman, postmanActions); }
            catch { database.Dispose(); postmanActions?.Dispose(); throw; }
            IsRunning = _server.IsRunning;
            Status = IsRunning ? "Running — local MCP server is available" : "Server could not start. Test the connection again.";
            if (!IsRunning) await _server.StopAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            await _server.StopAsync(); IsRunning = false;
            Status = "Could not start the MCP server. Another DevLens window may already be running, or local permissions prevent startup.";
        }
        finally { IsBusy = false; }
    }
    private void Review(ProcedureReview request, Action<bool> decision, System.Threading.CancellationToken token)
    {
        var operation = request.TargetKind == "postman" ? "Postman approval" : "SQL approval";
        _server.Activity.Add(operation, "Awaiting approval", "Review the separate DevLens approval window.");
        RequestProcedureReview!(request, approved =>
        {
            _server.Activity.Add(operation, approved ? "Approved" : "Rejected", "Approval decision received; applying is a separate tool call.");
            decision(approved);
        }, token);
    }
    [RelayCommand]
    public async Task StopAsync()
    {
        ResetAuthorization();
        IsBusy = true;
        try { await _server.StopAsync(); IsRunning = false; Status = "Stopped — desktop sessions closed. The updated ChatGPT plugin reconnects on its next status call after DevLens starts again."; }
        finally { IsBusy = false; }
    }
}
