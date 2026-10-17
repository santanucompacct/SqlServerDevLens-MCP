using System.Text.Json;

namespace SqlServerDevLens.Core;

public interface ICredentialStore
{
    bool IsAvailable { get; }
    Task SaveAsync(string profileId, string password, CancellationToken cancellationToken = default);
    Task<string?> ReadAsync(string profileId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);
}

// Fails closed on platforms without a secure credential adapter.
public sealed class UnavailableCredentialStore : ICredentialStore
{
    public bool IsAvailable => false;
    public Task SaveAsync(string profileId, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException("Secure password storage is not implemented.");
    public Task<string?> ReadAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public interface IProfileStore
{
    Task<ConnectionProfile?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonProfileStore(string path) : IProfileStore
{
    public static JsonProfileStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlServerDevLens", "connections.json"));
    public async Task<ConnectionProfile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        var profile = await JsonSerializer.DeserializeAsync<ConnectionProfile>(stream, cancellationToken: cancellationToken);
        profile?.Validate();
        return profile;
    }
    public async Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        profile.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(profile), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task DeleteAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); File.Delete(path); return Task.CompletedTask; }
}
