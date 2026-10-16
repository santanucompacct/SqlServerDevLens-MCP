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
    public bool CanStop => IsRunning && !IsBusy;
    public bool CanReturn => !IsRunning && !IsBusy;
    public ServerViewModel() : this(new DesktopMcpServer()) { }
    public ServerViewModel(IDesktopMcpServer server)
    {
        _server = server;
        _server.StoppedUnexpectedly += () => Dispatcher.UIThread.Post(() =>
        { IsRunning = false; Status = "Server stopped unexpectedly. Return to connection setup and test again."; });
    }
    partial void OnIsRunningChanged(bool value) { OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanReturn)); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(CanReturn)); }
    public async Task StartAsync(ConnectionProfile profile, string notice)
    {
        IsBusy = true; Status = "Starting MCP server…";
        ConnectionSummary = $"{profile.Host}:{profile.Port} / {profile.Database}";
        ConnectionNotice = notice;
        try
        {
            await _server.StartAsync(DateTimeOffset.UtcNow);
            IsRunning = _server.IsRunning;
            Status = IsRunning ? "Running — local MCP server is available" : "Server could not start. Test the connection again.";
            if (!IsRunning) await _server.StopAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
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
        try { await _server.StopAsync(); IsRunning = false; Status = "Stopped — MCP clients have been disconnected."; }
        finally { IsBusy = false; }
    }
}
