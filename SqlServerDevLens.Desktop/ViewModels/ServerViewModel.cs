using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;

namespace SqlServerDevLens.Desktop.ViewModels;

public partial class ServerViewModel : ViewModelBase
{
    private readonly IDesktopMcpServer _server;
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
        _server.StoppedUnexpectedly += () => Dispatcher.UIThread.Post(() =>
        { IsRunning = false; Status = "Server stopped unexpectedly. Return to connection setup and test again."; });
    }
    partial void OnIsRunningChanged(bool value) { OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanReturn)); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanReturn)); }
    public async Task StartAsync(ConnectionProfile profile, string notice, string? password = null, PostmanSelection? postman = null, string? postmanKey = null)
    {
        IsBusy = true; Status = "Starting MCP server…";
        ConnectionSummary = $"{profile.Host}:{profile.Port} / {profile.Database}";
        ConnectionNotice = notice;
        PostmanSummary = postman is null ? "Postman disabled — SQL Server Only."
            : $"Postman: {postman.Workspace.Name} / {postman.Collection.Name} — {postman.Requests.Length} startup requests. Changes and HTTP runs require explicit approval in DevLens.";
        try
        {
            var database = new SqlDatabase(profile, password, (request, decision, token) =>
            {
                if (RequestProcedureReview is null) { decision(false); return; }
                RequestProcedureReview(request, decision, token);
            });
            PostmanWorkflow? postmanActions = postman is not null && !string.IsNullOrEmpty(postmanKey)
                ? new PostmanWorkflow(new PostmanActionBackend(postmanKey, postman), (request, decision, token) =>
                    { if(RequestProcedureReview is null) { decision(false); return; } RequestProcedureReview(request, decision, token); }) : null;
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
    [RelayCommand]
    public async Task StopAsync()
    {
        IsBusy = true;
        try { await _server.StopAsync(); IsRunning = false; Status = "Stopped — desktop sessions closed. The updated ChatGPT plugin reconnects on its next status call after DevLens starts again."; }
        finally { IsBusy = false; }
    }
}
