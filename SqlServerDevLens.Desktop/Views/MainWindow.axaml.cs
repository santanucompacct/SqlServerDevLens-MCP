using Avalonia.Controls;
using SqlServerDevLens.Desktop.ViewModels;

namespace SqlServerDevLens.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;
    private bool _closing;
    public MainWindow()
    {
        InitializeComponent();
        Closing += async (_, e) =>
        {
            if (_shutdownComplete || DataContext is not MainViewModel model) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            await model.ShutdownAsync();
            _shutdownComplete = true; Close();
        };
    }
}
