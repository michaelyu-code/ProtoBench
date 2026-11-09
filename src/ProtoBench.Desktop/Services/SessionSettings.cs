using ProtoBench.Core.Documents;
namespace ProtoBench.Desktop.Services;

public sealed class SessionSettings(ISettingsStore store)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public LocalSettings Current { get; private set; } = new();
    public event Action? Changed;
    public async Task LoadAsync()
    {
        Current = await store.LoadAsync();
        Changed?.Invoke();
    }
    private async Task UpdateAsync(Func<LocalSettings, LocalSettings> update)
    {
        await gate.WaitAsync();
        try
        {
            var next = update(Current);
            await store.SaveAsync(next);
            Current = next;
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public Task ToggleFavoriteAsync(string id) => UpdateAsync(s => s with { FavoriteTools = s.FavoriteTools.Contains(id) ? s.FavoriteTools.Where(f => f != id).ToList() : [.. s.FavoriteTools, id] });
    public Task AddRecentAsync(string path) => UpdateAsync(s => s with { RecentFiles = new[] { path }.Concat(s.RecentFiles.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase))).Take(15).ToList() });
    public Task SetThemeAsync(string theme) => UpdateAsync(s => s with { Theme = theme });
    public Task SetParserAutoScrollAsync(bool enabled) => UpdateAsync(s => s with { AutoScrollParserResults = enabled });
    public Task SetParserAutoFitAsync(bool enabled) => UpdateAsync(s => s with { AutoFitParserResults = enabled });
    public Task SetVerboseParserChecksAsync(bool enabled) => UpdateAsync(s => s with { VerboseParserChecks = enabled });
}
