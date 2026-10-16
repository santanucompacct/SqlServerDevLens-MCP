using Microsoft.Data.SqlClient;

namespace SqlServerDevLens.Core;

public enum AuthenticationMode { Sql, Windows }

// Deliberately has no password member: this is the only persisted connection DTO.
public sealed record ConnectionProfile(string Host, int Port, string Database,
    AuthenticationMode Authentication, string Username, bool Encrypt = true,
    bool TrustServerCertificate = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.IndexOfAny([',', ';', '\\', '\r', '\n']) >= 0)
            throw new ArgumentException("Enter a host name or IP address; specify the TCP port separately (no instance name).");
        if (Port is < 1 or > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(Database)) throw new ArgumentException("One database name is required.");
        if (!Enum.IsDefined(Authentication)) throw new ArgumentException("Choose a supported authentication mode.");
        if (Authentication == AuthenticationMode.Sql && string.IsNullOrWhiteSpace(Username))
            throw new ArgumentException("Username is required for SQL authentication.");
    }
}

public interface IConnectionTester
{
    Task<ConnectionTestResult> TestAsync(ConnectionProfile profile, string? password, CancellationToken cancellationToken = default);
}
public sealed record ConnectionTestResult(bool Success, string Message);

public sealed class SqlConnectionTester : IConnectionTester
{
    public static string BuildConnectionString(ConnectionProfile profile, string? password)
    {
        profile.Validate();
        if (profile.Authentication == AuthenticationMode.Sql && string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required for SQL authentication.");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{profile.Host.Trim()},{profile.Port}", InitialCatalog = profile.Database.Trim(),
            IntegratedSecurity = profile.Authentication == AuthenticationMode.Windows,
            Encrypt = profile.Encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = profile.TrustServerCertificate, PersistSecurityInfo = false,
            ConnectTimeout = 15, ApplicationName = "SQL Server DevLens"
        };
        if (!builder.IntegratedSecurity) { builder.UserID = profile.Username.Trim(); builder.Password = password; }
        return builder.ConnectionString;
    }

    public async Task<ConnectionTestResult> TestAsync(ConnectionProfile profile, string? password, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(BuildConnectionString(profile, password));
            await connection.OpenAsync(cancellationToken);
            return new(true, "Connection opened successfully to the selected database and disposed after the test.");
        }
        catch (ArgumentException ex) { return new(false, ex.Message); }
        catch (OperationCanceledException) { return new(false, "Connection test cancelled or timed out."); }
        // Never surface provider messages: they can contain host, account or connection details.
        catch (SqlException ex)
        {
            return new(false, ex.Number switch
            {
                18456 => "SQL login failed. Check authentication, username and password.",
                4060 => "The selected database could not be opened. Check its name and login permissions.",
                -2 => "Connection timed out. Check host, port, firewall and server availability.",
                _ => $"SQL connection failed (error {ex.Number}). Check host/port, authentication, database permissions and TLS certificate configuration."
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Authentication.AuthenticationException or PlatformNotSupportedException)
        { return new(false, "Connection could not be opened. Check platform authentication support and TLS configuration."); }
    }
}
