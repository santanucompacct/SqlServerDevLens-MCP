using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using SqlServerDevLens.Desktop.ViewModels;
using SqlServerDevLens.Desktop.Views;
using SqlServerDevLens.Core;
using SqlServerDevLens.Runtime;
using Xunit;

namespace SqlServerDevLens.Tests;

public class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SqlServerDevLens.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class ConfirmationTests
{
    [Theory]
    [InlineData(false, "procedure")]
    [InlineData(true, "procedure")]
    [InlineData(false, "table")]
    [InlineData(true, "table")]
    public Task ProcedureReviewRequiresHumanDecisionAndShowsExactSql(bool approve, string targetKind) => RunOnUi(async () =>
    {
        var owner = new Window(); owner.Show();
        var review = new ProcedureReview("fake-request", "FakeDatabase", "dbo", "Example", "alter",
            "CREATE PROC dbo.Example AS SELECT 1", "ALTER PROC dbo.Example AS SELECT 2",
            "-SELECT 1\n+SELECT 2", "", DateTimeOffset.UtcNow.AddMinutes(5), targetKind);
        var dialog = new ProcedureReviewWindow(review);
        var decision = dialog.ShowDialog<bool>(owner);
        Assert.True(dialog.FindControl<Button>("RejectAction")!.IsDefault);
        Assert.Equal(review.Diff, dialog.FindControl<TextBox>("ReviewText")!.Text);
        Assert.Equal(review.Before, dialog.FindControl<TextBox>("BeforeText")!.Text);
        Assert.Equal(review.Proposed, dialog.FindControl<TextBox>("AfterText")!.Text);
        if (targetKind == "table")
        {
            Assert.Equal("Current schema metadata", dialog.FindControl<TabItem>("BeforeTab")!.Header);
            Assert.Contains("NOT table rows", dialog.FindControl<TextBlock>("Scope")!.Text);
        }
        dialog.FindControl<Button>(approve ? "ApproveAction" : "RejectAction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(approve, await decision); owner.Close();
    });

    private static async Task RunOnUi(Func<Task> action)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await session.Dispatch(async () => { await action(); return true; }, timeout.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StopRequiresExplicitConfirmation(bool confirm) => RunOnUi(async () =>
    {
        var runtime = new FakeServer();
        var model = new MainViewModel(new ConnectionViewModel(), new ServerViewModel(runtime));
        await model.Server.StartAsync(new ConnectionProfile("localhost", 1433, "Example", AuthenticationMode.Windows, ""), "Test succeeded.");
        var promptCount = 0;
        var window = new MainWindow { DataContext = model, ConfirmActionAsync = (_, message, _) =>
        { promptCount++; Assert.Contains("disconnect every", message); return Task.FromResult(confirm); } };
        window.Show();
        await window.RequestStopAsync();
        Assert.Equal(1, promptCount);
        Assert.Equal(!confirm, runtime.IsRunning);
        Assert.True(window.IsVisible);
        window.ConfirmActionAsync = (_, _, _) => Task.FromResult(true);
        window.Close();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CloseRequiresConfirmationAndStopsOnlyOnce(bool confirm) => RunOnUi(async () =>
    {
        var runtime = new FakeServer();
        var model = new MainViewModel(new ConnectionViewModel(), new ServerViewModel(runtime));
        await model.Server.StartAsync(new ConnectionProfile("localhost", 1433, "Example", AuthenticationMode.Windows, ""), "Test succeeded.");
        var promptCount = 0;
        var closed = false;
        var window = new MainWindow { DataContext = model, ConfirmActionAsync = (_, message, _) =>
        { promptCount++; Assert.Contains("Closing DevLens", message); return Task.FromResult(confirm); } };
        window.Closed += (_, _) => closed = true;
        window.Show(); window.Close();
        Assert.Equal(1, promptCount);
        Assert.Equal(confirm, closed);
        Assert.Equal(!confirm, runtime.IsRunning);
        Assert.Equal(confirm ? 1 : 0, runtime.StopCount);
        if (!confirm) { window.ConfirmActionAsync = (_, _, _) => Task.FromResult(true); window.Close(); }
    });

    [Fact]
    public Task RepeatedStopAndCloseCannotOpenOverlappingPrompts() => RunOnUi(async () =>
    {
        var model = new MainViewModel(new ConnectionViewModel(), new ServerViewModel(new FakeServer()));
        await model.Server.StartAsync(new ConnectionProfile("localhost", 1433, "Example", AuthenticationMode.Windows, ""), "Test succeeded.");
        var answer = new TaskCompletionSource<bool>(); var promptCount = 0;
        var window = new MainWindow { DataContext = model, ConfirmActionAsync = (_, _, _) =>
        { promptCount++; return answer.Task; } };
        window.Show();
        var first = window.RequestStopAsync();
        await window.RequestStopAsync(); window.Close();
        Assert.Equal(1, promptCount);
        answer.SetResult(false); await first;
        Assert.True(model.Server.IsRunning); Assert.True(window.IsVisible);
        window.ConfirmActionAsync = (_, _, _) => Task.FromResult(true); window.Close();
    });

    [Theory]
    [InlineData("CancelAction", false)]
    [InlineData("ConfirmAction", true)]
    public Task DialogButtonsReturnTheExpectedDecision(string buttonName, bool expected) => RunOnUi(async () =>
    {
        var owner = new Window(); owner.Show();
        var dialog = new ConfirmationWindow("Stop MCP server?", "Disconnect all clients?", "Stop server");
        var decision = dialog.ShowDialog<bool>(owner);
        Assert.True(dialog.FindControl<Button>("CancelAction")!.IsDefault);
        dialog.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(expected, await decision); owner.Close();
    });

    [Fact]
    public Task ClosingDialogCancelsAndHelpWindowLoads() => RunOnUi(async () =>
    {
        var owner = new Window(); owner.Show();
        var dialog = new ConfirmationWindow("Stop?", "Disconnect?", "Stop");
        var decision = dialog.ShowDialog<bool>(owner); dialog.Close();
        Assert.False(await decision);
        var help = new ConnectionHelpWindow(); help.Show(owner);
        Assert.True(help.IsVisible); help.Close(); owner.Close();
    });

    private sealed class FakeServer : IDesktopMcpServer
    {
        public bool IsRunning { get; private set; }
        public int StopCount;
        public event Action? StoppedUnexpectedly { add { } remove { } }
        public Task StartAsync(DateTimeOffset sqlTestedAt, SqlServerDevLens.Core.ISqlDatabase? database = null) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync() { IsRunning = false; StopCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
