using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SqlServerDevLens.Core;

public interface IProcedureBackupStore
{
    Task SaveAsync(string database, ProcedureDefinition previous, CancellationToken token);
}

public sealed class PrivateProcedureBackupStore(string? directory = null) : IProcedureBackupStore
{
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlServerDevLens", "ProcedureBackups");
    public async Task SaveAsync(string database, ProcedureDefinition previous, CancellationToken token)
    {
        try
        {
            var full = Path.GetFullPath(DirectoryPath);
            var current = new DirectoryInfo(full);
            for (var parent = current; parent is not null; parent = parent.Parent)
                if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Backup path contains a link.");
            if (OperatingSystem.IsWindows())
            {
                var user = WindowsIdentity.GetCurrent().User ?? throw new IOException("Windows identity unavailable.");
                var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(user);
                security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                if (!current.Exists) FileSystemAclExtensions.CreateDirectory(security, full);
                else current.SetAccessControl(security);
            }
            else
            {
                Directory.CreateDirectory(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var file = Path.Combine(full, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.sql");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var stream = new FileStream(file, options);
            var header = $"-- Private pre-change backup. Revision {previous.Revision}\nSET ANSI_NULLS {(previous.AnsiNulls ? "ON" : "OFF")};\nSET QUOTED_IDENTIFIER {(previous.QuotedIdentifier ? "ON" : "OFF")};\nGO\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(header + previous.Definition), token);
            await stream.FlushAsync(token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { throw new InvalidOperationException("Private backup could not be written securely. No procedure change was applied."); }
    }
}
