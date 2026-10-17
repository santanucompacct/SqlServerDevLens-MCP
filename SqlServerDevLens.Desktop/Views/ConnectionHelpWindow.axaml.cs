using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;

namespace SqlServerDevLens.Desktop.Views;

public partial class ConnectionHelpWindow : Window
{
    public ConnectionHelpWindow() { InitializeComponent(); }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private async void Readme_Click(object? sender, RoutedEventArgs e) => await OpenLinkAsync(
        "https://github.com/santanucompacct/SqlServerDevLens-MCP/blob/feature/postman-setup/README.md#publishinstall-and-test-the-local-plugin");
    private async void Guide_Click(object? sender, RoutedEventArgs e) => await OpenLinkAsync(
        "https://github.com/santanucompacct/SqlServerDevLens-MCP/blob/feature/postman-setup/docs/CONNECTING-TO-CHATGPT.md");
    private async void CopyPrompt_Click(object? sender, RoutedEventArgs e)
    {
        var text = sender is Button { Tag: string name } ? this.FindControl<SelectableTextBlock>(name)?.Text : null;
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { LinkStatus.Text = "Select the prompt text and copy it manually."; return; }
            await clipboard.SetTextAsync(text);
            LinkStatus.Text = "Prompt copied. Paste it in your local ChatGPT chat.";
        }
        catch (Exception) { LinkStatus.Text = "Clipboard unavailable. Select the prompt text and copy it manually."; }
    }
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
