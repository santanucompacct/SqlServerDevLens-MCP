using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Threading.Tasks;
using SqlServerDevLens.Desktop.ViewModels;

namespace SqlServerDevLens.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;
    private bool _actionInProgress;
    private ConnectionHelpWindow? _helpWindow;
    public Func<string, string, string, Task<bool>>? ConfirmActionAsync { private get; set; }
    public MainWindow()
    {
        InitializeComponent();
        Closing += async (_, e) =>
        {
            if (_shutdownComplete || DataContext is not MainViewModel model) return;
            e.Cancel = true;
            if (_actionInProgress) return;
            _actionInProgress = true;
            try
            {
                var message = model.Server.IsRunning
                    ? "Closing DevLens will stop the MCP server and disconnect all ChatGPT clients. Do you want to close the application?"
                    : "Do you want to close SQL Server DevLens?";
                if (!await ConfirmAsync("Close DevLens?", message, "Close application")) return;
                await model.ShutdownAsync();
                _shutdownComplete = true; Close();
            }
            finally { _actionInProgress = false; }
        };
    }
    private Task<bool> ConfirmAsync(string title, string message, string action) =>
        ConfirmActionAsync?.Invoke(title, message, action)
        ?? new ConfirmationWindow(title, message, action).ShowDialog<bool>(this);

    private async void StopServer_Click(object? sender, RoutedEventArgs e) => await RequestStopAsync();
    public async Task RequestStopAsync()
    {
        if (_actionInProgress || DataContext is not MainViewModel model || !model.Server.CanStop) return;
        _actionInProgress = true;
        try
        {
            if (await ConfirmAsync("Stop MCP server?",
                "This will disconnect every connected ChatGPT client. DevLens will remain open, and you can test the SQL connection again to restart the server.", "Stop server"))
                await model.Server.StopAsync();
        }
        finally { _actionInProgress = false; }
    }
    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        if (_helpWindow is not null) { _helpWindow.Activate(); return; }
        _helpWindow = new ConnectionHelpWindow();
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show(this);
    }
}
