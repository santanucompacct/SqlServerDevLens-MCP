using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SqlServerDevLens.Desktop.Views;

public partial class ConnectionHelpWindow : Window
{
    public ConnectionHelpWindow() { InitializeComponent(); }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private async void Readme_Click(object? sender, RoutedEventArgs e) => await OpenLinkAsync(
        "https://github.com/santanucompacct/SqlServerDevLens-MCP/blob/feature/sql-login-mcp-status/README.md#test-the-local-mcp-plugin");
    private async void Plans_Click(object? sender, RoutedEventArgs e) => await OpenLinkAsync("https://learn.chatgpt.com/docs/pricing");
    private async System.Threading.Tasks.Task OpenLinkAsync(string url)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(new Uri(url)))
                LinkStatus.Text = "Open this address in your browser: " + url;
        }
        catch (Exception) { LinkStatus.Text = "Open this address in your browser: " + url; }
    }
}
