using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SqlServerDevLens.Core;

public static class CredentialStores
{
    public static ICredentialStore CreateDefault() => OperatingSystem.IsWindows()
        ? new WindowsCredentialStore() : new UnavailableCredentialStore();
    // Bind secrets to the full connection configuration, including TLS policy.
    public static string ProfileId(ConnectionProfile profile) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profile))));
}

public sealed class WindowsCredentialStore : ICredentialStore
{
    public bool IsAvailable => OperatingSystem.IsWindows();
    private string Target(string id)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException("Windows Credential Manager requires Windows.");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) throw new ArgumentException("Invalid credential identifier.");
        return "SqlServerDevLens/" + id;
    }
    public Task SaveAsync(string profileId, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = Target(profileId);
        if (password.Length > 1280) throw new ArgumentException("Password exceeds the Windows credential size limit.");
        var pointer = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var credential = new Credential
            {
                Type = 1, TargetName = target, UserName = "SqlServerDevLens",
                BlobSize = checked((uint)password.Length * 2), Blob = pointer, Persist = 2
            };
            if (!CredWriteW(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }
        return Task.CompletedTask;
    }
    public Task<string?> ReadAsync(string profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredReadW(Target(profileId), 1, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return Task.FromResult<string?>(null);
            throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.BlobSize > 2560 || credential.BlobSize % 2 != 0)
                throw new IOException("Invalid saved credential format.");
            return Task.FromResult<string?>(Marshal.PtrToStringUni(credential.Blob, (int)credential.BlobSize / 2));
        }
        finally { CredFree(pointer); }
    }
    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDeleteW(Target(profileId), 1, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw new Win32Exception(error);
        }
        return Task.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr credential);
}
