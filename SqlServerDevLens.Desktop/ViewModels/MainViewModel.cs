using CommunityToolkit.Mvvm.Input;

namespace SqlServerDevLens.Desktop.ViewModels;

/// <summary>
/// The operating modes available on the application startup screen.
/// </summary>
public enum DevLensMode
{
    SqlOnly,
    SqlWithPostman
}

/// <summary>
/// Manages startup mode selection and navigation to connection setup.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private DevLensMode _selectedMode = DevLensMode.SqlOnly;
    private bool _isChoosingMode = true;
    private bool _isServerStepVisible;
    private bool _shuttingDown;

    public MainViewModel() : this(new ConnectionViewModel(), new ServerViewModel()) { }
    public MainViewModel(ConnectionViewModel connection, ServerViewModel server)
    {
        Connection = connection; Server = server;
        Connection.ConnectionSucceeded += async (profile, password) =>
        {
            if (_shuttingDown) return;
            IsServerStepVisible = true;
            await Server.StartAsync(profile, Connection.Status, password);
        };
        ContinueCommand = new AsyncRelayCommand(async () => { await Connection.LoadAsync(); IsChoosingMode = false; });
        BackCommand = new RelayCommand(() => { Connection.Password = ""; IsChoosingMode = true; });
        ReturnToConnectionCommand = new AsyncRelayCommand(async () =>
        {
            if (!Server.CanReturn) return;
            await Server.StopAsync();
            IsServerStepVisible = false; await Connection.LoadAsync();
        });
    }

    public ConnectionViewModel Connection { get; }
    public ServerViewModel Server { get; }
    public IAsyncRelayCommand ReturnToConnectionCommand { get; }
    public async System.Threading.Tasks.Task ShutdownAsync()
    {
        _shuttingDown = true;
        await Server.StopAsync();
    }
    public bool IsServerStepVisible
    {
        get => _isServerStepVisible;
        private set { if (SetProperty(ref _isServerStepVisible, value)) OnPropertyChanged(nameof(IsConnectionStepVisible)); }
    }
    public IAsyncRelayCommand ContinueCommand { get; }

    public IRelayCommand BackCommand { get; }

    public bool IsSqlOnlySelected
    {
        get => _selectedMode == DevLensMode.SqlOnly;
        set
        {
            if (value)
                SelectMode(DevLensMode.SqlOnly);
        }
    }

    public bool IsSqlWithPostmanSelected
    {
        get => _selectedMode == DevLensMode.SqlWithPostman;
        set
        {
            if (value)
                SelectMode(DevLensMode.SqlWithPostman);
        }
    }

    public string SelectedModeLabel => _selectedMode == DevLensMode.SqlOnly
        ? "SQL Server Only"
        : "SQL Server + Postman";

    public string SelectedModeDescription => _selectedMode == DevLensMode.SqlOnly
        ? "Database exploration and read-only inspection tools."
        : "Database exploration with optional API testing and Postman integration.";

    public bool IsChoosingMode
    {
        get => _isChoosingMode;
        private set
        {
            if (SetProperty(ref _isChoosingMode, value))
                OnPropertyChanged(nameof(IsConnectionStepVisible));
        }
    }

    public bool IsConnectionStepVisible => !IsChoosingMode && !IsServerStepVisible;

    private void SelectMode(DevLensMode mode)
    {
        if (_selectedMode == mode)
            return;

        _selectedMode = mode;
        OnPropertyChanged(nameof(IsSqlOnlySelected));
        OnPropertyChanged(nameof(IsSqlWithPostmanSelected));
        OnPropertyChanged(nameof(SelectedModeLabel));
        OnPropertyChanged(nameof(SelectedModeDescription));
    }
}
