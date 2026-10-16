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
/// Manages the startup selection screen. Database connectivity and the
/// MCP host will be introduced in subsequent milestones.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private DevLensMode _selectedMode = DevLensMode.SqlOnly;
    private bool _isChoosingMode = true;

    public MainViewModel()
    {
        ContinueCommand = new RelayCommand(() => IsChoosingMode = false);
        BackCommand = new RelayCommand(() => IsChoosingMode = true);
    }

    public IRelayCommand ContinueCommand { get; }

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

    public bool IsConnectionStepVisible => !IsChoosingMode;

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
