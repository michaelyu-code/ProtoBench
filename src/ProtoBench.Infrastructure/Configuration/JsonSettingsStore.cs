using System.Text.Json;
using ProtoBench.Core.Documents;
namespace ProtoBench.Infrastructure.Configuration;

public static class AppPaths
{
    public static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProtoBench");
    public static string LogDirectory => Path.Combine(UserDirectory, "logs");
}
public sealed class JsonSettingsStore(string path, IAtomicFileWriter writer) : ISettingsStore
{
    public async Task<LocalSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return new();
        await using var stream = File.OpenRead(path);
        if (stream.Length > 1048576)
            throw new InvalidDataException("Settings exceed the 1 MiB limit.");
        var settings = await JsonSerializer.DeserializeAsync<LocalSettings>(stream, cancellationToken: cancellationToken) ?? throw new InvalidDataException("Settings are empty.");
        if (settings.SchemaVersion != 1 || settings.FavoriteTools is null || settings.RecentFiles is null || settings.Theme is not ("Default" or "Light" or "Dark"))
            throw new InvalidDataException("Invalid settings or unsupported settings version.");
        return settings;
    }
    public Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default) => writer.WriteAsync(path,
        (stream, token) => JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, token), cancellationToken);
}
