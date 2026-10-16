using System;
using System.ComponentModel;
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
    private readonly ICredentialStore _credentials;
    private bool _loading;
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _port = "1433";
    [ObservableProperty] private string _database = "";
    [ObservableProperty] private bool _windowsAuthentication;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _encrypt = true;
    [ObservableProperty] private bool _trustServerCertificate;
    [ObservableProperty] private bool _rememberProfile;
    [ObservableProperty] private bool _rememberPassword;
    [ObservableProperty] private string _status = "Test opens and closes a connection; it does not start the MCP host.";
    [ObservableProperty] private bool _isBusy;
    public bool IsSqlAuthentication => !WindowsAuthentication;
    public bool IsEditable => !IsBusy;
    public bool CanRememberPassword => _credentials.IsAvailable && RememberProfile && IsSqlAuthentication;
    public string PasswordStorageHint => _credentials.IsAvailable
        ? "Remember password uses Windows Credential Manager for your Windows account on this computer."
        : "Password remembering is unavailable on this OS; secure macOS/Linux storage is planned.";
    public ConnectionViewModel() : this(new SqlConnectionTester(), JsonProfileStore.CreateDefault()) { }
    public ConnectionViewModel(IConnectionTester tester, IProfileStore profiles, ICredentialStore? credentials = null)
    { _tester = tester; _profiles = profiles; _credentials = credentials ?? CredentialStores.CreateDefault(); }
    private void ClearEditedPassword() { if (!_loading) Password = ""; }
    partial void OnHostChanged(string value) => ClearEditedPassword();
    partial void OnPortChanged(string value) => ClearEditedPassword();
    partial void OnDatabaseChanged(string value) => ClearEditedPassword();
    partial void OnUsernameChanged(string value) => ClearEditedPassword();
    partial void OnEncryptChanged(bool value) => ClearEditedPassword();
    partial void OnTrustServerCertificateChanged(bool value) => ClearEditedPassword();
    partial void OnRememberProfileChanged(bool value)
    { if (!value) RememberPassword = false; OnPropertyChanged(nameof(CanRememberPassword)); }
    partial void OnWindowsAuthenticationChanged(bool value)
    { Password = ""; if (value) RememberPassword = false; OnPropertyChanged(nameof(IsSqlAuthentication)); OnPropertyChanged(nameof(CanRememberPassword)); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEditable));

    public async Task LoadAsync()
    {
        Password = ""; RememberPassword = false;
        try
        {
            _loading = true;
            var profile = await _profiles.LoadAsync();
            if (profile is null) return;
            Host = profile.Host; Port = profile.Port.ToString(); Database = profile.Database;
            WindowsAuthentication = profile.Authentication == AuthenticationMode.Windows;
            Username = profile.Username; Encrypt = profile.Encrypt;
            TrustServerCertificate = profile.TrustServerCertificate; RememberProfile = true;
            Status = "Saved profile loaded. Enter your password for SQL authentication.";
            if (_credentials.IsAvailable && IsSqlAuthentication)
            {
                Password = await _credentials.ReadAsync(CredentialStores.ProfileId(profile)) ?? "";
                RememberPassword = Password.Length > 0;
                if (RememberPassword) Status = "Saved profile and password loaded from Windows Credential Manager.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or Win32Exception)
        { Password = ""; Status = "Saved profile or secure password could not be loaded. Enter connection details/password again."; }
        finally { _loading = false; }
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
                var previous = await _profiles.LoadAsync();
                // Delete the old secret before replacing/removing its profile so it remains discoverable on failure.
                if (_credentials.IsAvailable && previous is not null)
                    await _credentials.DeleteAsync(CredentialStores.ProfileId(previous));
                if (RememberProfile) await _profiles.SaveAsync(profile);
                else await _profiles.DeleteAsync();
                if (RememberPassword && CanRememberPassword)
                {
                    await _credentials.SaveAsync(CredentialStores.ProfileId(profile), Password);
                    Status += " Profile saved; password saved securely in Windows Credential Manager.";
                }
                else Status += RememberProfile ? " Profile saved without password." : " No profile or password retained.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException)
        { Status = "SQL test finished, but profile/password changes could not be completed. Check local permissions and Windows Credential Manager; password was not saved to JSON."; }
        finally { Password = ""; IsBusy = false; }
    }

    [RelayCommand]
    private async Task ForgetProfileAsync()
    {
        try
        {
            var previous = await _profiles.LoadAsync();
            if (_credentials.IsAvailable && previous is not null)
                await _credentials.DeleteAsync(CredentialStores.ProfileId(previous));
            await _profiles.DeleteAsync(); RememberProfile = false; Password = "";
            Status = "Saved profile and its stored password removed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException)
        { Status = "Profile/password could not be removed. Check local permissions and Windows Credential Manager."; }
    }
}
