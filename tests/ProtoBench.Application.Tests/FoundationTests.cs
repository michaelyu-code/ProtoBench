using ProtoBench.Application.Tools;
using ProtoBench.Application.Workspaces;
using ProtoBench.Infrastructure.Files;
using ProtoBench.Infrastructure.Configuration;
using ProtoBench.Core.Documents;
namespace ProtoBench.Application.Tests;

public sealed class FoundationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ProtoBench-tests-" + Guid.NewGuid());
    private static ToolDescriptor Tool(string id = "converter") => new(id, "Converter", "Test", ToolCategory.Converters, "");
    [Fact]
    public void ToolRegistrationAndLookup()
    {
        var tool = Tool();
        var registry = new ToolRegistry([tool]);
        Assert.Same(tool, registry.Get("CONVERTER"));
        Assert.Single(registry.Tools);
        Assert.Throws<KeyNotFoundException>(() => registry.Get("missing"));
        Assert.Throws<ArgumentException>(() => new ToolRegistry([tool, Tool("CONVERTER")]));
        Assert.Throws<ArgumentException>(() => new ToolRegistry([Tool("")]));
    }
    private sealed class Workspace : ToolWorkspace
    {
        public override string ToolId => "test"; public override string Title => "Test"; public bool Disposed
        {
            get; private set;
        }
        public override void Dispose()
        {
            base.Dispose();
            Disposed = true;
        }
    }
    private sealed class Factory : IToolWorkspaceFactory
    {
        public string ToolId => "test"; public ToolWorkspace Create() => new Workspace();
    }
    [Fact]
    public void WorkspacesAreIndependentAndDisposedOnClose()
    {
        using var manager = new WorkspaceManager([new Factory()]);
        var first = (Workspace)manager.Open("test");
        var second = manager.Open("test");
        Assert.NotEqual(first.InstanceId, second.InstanceId);
        manager.Close(first);
        Assert.True(first.Disposed);
        Assert.Single(manager.Workspaces);
    }
    [Fact]
    public async Task AtomicWriterPreservesExistingFileOnFailure()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "output.dat");
        await File.WriteAllTextAsync(path, "original");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AtomicFileWriter().WriteAsync(path, async (s, ct) => { await s.WriteAsync(new byte[] { 1, 2 }, ct); throw new InvalidOperationException(); }));
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(directory));
        await new AtomicFileWriter().WriteAsync(path, async (s, ct) => await s.WriteAsync(new byte[] { 3, 4 }, ct));
        Assert.Equal(new byte[] { 3, 4 }, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task SettingsRoundTrip()
    {
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"), new AtomicFileWriter());
        Assert.Empty((await store.LoadAsync()).FavoriteTools);
        Assert.True((await store.LoadAsync()).AutoScrollParserResults);
        await store.SaveAsync(new LocalSettings { FavoriteTools = ["converter"], RecentFiles = ["sample.txt"], Theme = "Dark", AutoScrollParserResults = false, AutoFitParserResults = false });
        var settings = await store.LoadAsync();
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("converter", Assert.Single(settings.FavoriteTools));
        Assert.False(settings.AutoScrollParserResults);
        Assert.False(settings.AutoFitParserResults);
    }
    [Fact]
    public async Task ExistingSettingsEnableParserAutoScrollByDefault()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"SchemaVersion":1,"Theme":"Dark","FavoriteTools":[],"RecentFiles":[]}""");
        var settings = await new JsonSettingsStore(path, new AtomicFileWriter()).LoadAsync();
        Assert.True(settings.AutoScrollParserResults);
        Assert.True(settings.AutoFitParserResults);
        Assert.Equal("Dark", settings.Theme);
    }
    [Fact]
    public void DefinitionLoaderIsolatesInvalidAndDuplicateFiles()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "a.json"), "good");
        File.WriteAllText(Path.Combine(directory, "b.json"), "good");
        File.WriteAllText(Path.Combine(directory, "c.json"), "bad");
        var result = DefinitionLoader.Load([directory], text => text == "bad" ? throw new ArgumentException("Invalid") : text, text => text);
        Assert.Single(result.Definitions);
        Assert.Equal(2, result.Diagnostics.Count);
    }
    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
