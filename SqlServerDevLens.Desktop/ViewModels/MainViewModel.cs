using CommunityToolkit.Mvvm.Input;
using SqlServerDevLens.Core;

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
    private bool _isPostmanStepVisible;
    private ConnectionProfile? _pendingProfile;
    private string? _pendingPassword;

    public MainViewModel() : this(new ConnectionViewModel(), new ServerViewModel()) { }
    public MainViewModel(ConnectionViewModel connection, ServerViewModel server, PostmanViewModel? postman = null)
    {
        Connection = connection; Server = server; Postman = postman ?? new PostmanViewModel();
        Connection.ConnectionSucceeded += async (profile, password) =>
        {
            if (_shuttingDown) return;
            if (_selectedMode == DevLensMode.SqlWithPostman)
            {
                _pendingProfile = profile; _pendingPassword = password;
                IsPostmanStepVisible = true;
                await Postman.LoadSavedKeyAsync();
                return;
            }
            IsServerStepVisible = true;
            await Server.StartAsync(profile, Connection.Status, password);
        };
        Postman.Ready += async selection =>
        {
            if (_shuttingDown || _pendingProfile is null) return;
            var profile = _pendingProfile; var password = _pendingPassword;
            _pendingProfile = null; _pendingPassword = null;
            IsPostmanStepVisible = false; IsServerStepVisible = true;
            await Server.StartAsync(profile, Connection.Status, password, selection, Postman.ApiKey);
        };
        ContinueCommand = new AsyncRelayCommand(async () => { await Connection.LoadAsync(); IsChoosingMode = false; });
        BackCommand = new RelayCommand(() => { Connection.Password = ""; IsChoosingMode = true; });
        BackFromPostmanCommand = new AsyncRelayCommand(async () =>
        {
            if (Postman.IsBusy) return;
            _pendingProfile = null; _pendingPassword = null; Postman.ClearSession();
            IsPostmanStepVisible = false; await Connection.LoadAsync();
        });
        ReturnToConnectionCommand = new AsyncRelayCommand(async () =>
        {
            if (!Server.CanReturn) return;
            await Server.StopAsync();
            IsServerStepVisible = false; await Connection.LoadAsync();
        });
    }

    public ConnectionViewModel Connection { get; }
    public PostmanViewModel Postman { get; }
    public IAsyncRelayCommand BackFromPostmanCommand { get; }
    public ServerViewModel Server { get; }
    public IAsyncRelayCommand ReturnToConnectionCommand { get; }
    public async System.Threading.Tasks.Task ShutdownAsync()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _pendingProfile = null; _pendingPassword = null; Postman.Dispose();
        await Server.StopAsync();
    }
    public bool IsPostmanStepVisible
    {
        get => _isPostmanStepVisible;
        private set { if (SetProperty(ref _isPostmanStepVisible, value)) OnPropertyChanged(nameof(IsConnectionStepVisible)); }
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

    public bool IsConnectionStepVisible => !IsChoosingMode && !IsServerStepVisible && !IsPostmanStepVisible;

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
