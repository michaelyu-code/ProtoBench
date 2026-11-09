namespace ProtoBench.Core.Documents;

public sealed record LocalSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string Theme { get; init; } = "Default";
    public bool AutoScrollParserResults { get; init; } = true;
    public bool AutoFitParserResults { get; init; } = true;
    public bool VerboseParserChecks { get; init; }
    public List<string> FavoriteTools { get; init; } = [];
    public List<string> RecentFiles { get; init; } = [];
}
public interface ISettingsStore
{
    Task<LocalSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default);
}
public interface IAtomicFileWriter
{
    Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default);
}
