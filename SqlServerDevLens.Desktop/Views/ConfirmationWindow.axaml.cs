using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SqlServerDevLens.Desktop.Views;

public partial class ConfirmationWindow : Window
{
    public ConfirmationWindow() { InitializeComponent(); }
    public ConfirmationWindow(string title, string message, string action) : this()
    {
        Title = title; Heading.Text = title; Message.Text = message; ConfirmAction.Content = action;
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);
}
