using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SqlServerDevLens.Core;

namespace SqlServerDevLens.Desktop.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly IConnectionTester _tester;
    private readonly IProfileStore _profiles;
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "1433";
    [ObservableProperty] private string _database = "";
    [ObservableProperty] private bool _windowsAuthentication;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _encrypt = true;
    [ObservableProperty] private bool _trustServerCertificate;
    [ObservableProperty] private bool _rememberProfile;
    [ObservableProperty] private string _status = "Test opens and closes a connection; it does not start the MCP host.";
    [ObservableProperty] private bool _isBusy;
    public bool IsSqlAuthentication => !WindowsAuthentication;
    public bool IsEditable => !IsBusy;
    public bool CanRememberPassword => false;
    public ConnectionViewModel() : this(new SqlConnectionTester(), JsonProfileStore.CreateDefault()) { }
    public ConnectionViewModel(IConnectionTester tester, IProfileStore profiles)
    { _tester = tester; _profiles = profiles; }
    partial void OnWindowsAuthenticationChanged(bool value)
    { Password = ""; OnPropertyChanged(nameof(IsSqlAuthentication)); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEditable));

    public async Task LoadAsync()
    {
        try
        {
            var profile = await _profiles.LoadAsync();
            if (profile is null) return;
            Host = profile.Host; Port = profile.Port.ToString(); Database = profile.Database;
            WindowsAuthentication = profile.Authentication == AuthenticationMode.Windows;
            Username = profile.Username; Encrypt = profile.Encrypt;
            TrustServerCertificate = profile.TrustServerCertificate; RememberProfile = true;
            Status = "Saved profile loaded. Enter your password for SQL authentication.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Status = "Saved profile could not be loaded. Enter connection details again."; }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        try
        {
            if (!int.TryParse(Port, out var port)) { Status = "Enter a numeric TCP port between 1 and 65535."; return; }
            var profile = new ConnectionProfile(Host.Trim(), port, Database.Trim(),
                WindowsAuthentication ? AuthenticationMode.Windows : AuthenticationMode.Sql,
                WindowsAuthentication ? "" : Username.Trim(), Encrypt, TrustServerCertificate);
            Status = "Testing connection…";
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await _tester.TestAsync(profile, Password, timeout.Token);
            Status = result.Message;
            if (result.Success)
            {
                if (RememberProfile) await _profiles.SaveAsync(profile);
                else await _profiles.DeleteAsync();
                Status += RememberProfile ? " Profile saved without password." : " No profile retained.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "SQL test finished, but the profile could not be saved or removed. Check local file permissions."; }
        finally { Password = ""; IsBusy = false; }
    }

    [RelayCommand]
    private async Task ForgetProfileAsync()
    {
        try { await _profiles.DeleteAsync(); RememberProfile = false; Password = ""; Status = "Saved profile removed."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "Profile could not be removed. Check local file permissions."; }
    }
}
