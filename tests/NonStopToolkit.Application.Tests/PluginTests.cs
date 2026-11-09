using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Infrastructure.Plugins;
using NonStopToolkit.Parsing.Definitions;
using NonStopToolkit.Parsing.Engines;
using NonStopToolkit.PluginSdk;

namespace NonStopToolkit.Application.Tests;

public sealed class PluginTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "toolkit-plugin-test-" + Guid.NewGuid().ToString("N"));
    private PluginPackageManager Packages => new(Path.Combine(directory, "installed"));
    public PluginTests() => Directory.CreateDirectory(directory);

    private string MakePackage(string? extraPath = null, int api = 1)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".nstplugin");
        var manifest = new PluginManifest(1, api, "example.toolkit", "Example", "1.0.0", "ExamplePlugin.dll", "ExamplePlugin.ExamplePlugin", ["ExamplePlugin.dll"]);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("plugin.json").Open())) writer.Write(JsonSerializer.Serialize(manifest));
        archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "ExamplePlugin.dll"), "ExamplePlugin.dll");
        if (extraPath is not null) using (var writer = new StreamWriter(archive.CreateEntry(extraPath).Open())) writer.Write("unexpected");
        return path;
    }

    [Fact]
    public async Task ImportedDllUsesHostContractsAndRunsToolParserAndDecoder()
    {
        var packages = Packages;
        packages.Import(MakePackage());
        using var catalog = PluginCatalog.Load(packages, [], [], []);
        Assert.Empty(catalog.Diagnostics);
        var plugin = Assert.Single(catalog.Plugins);
        Assert.NotEqual(System.Runtime.Loader.AssemblyLoadContext.Default,
            System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(plugin.Instance.GetType().Assembly));
        using var workspace = new PluginToolWorkspace(Assert.Single(plugin.Tools));
        workspace.Inputs["text"] = "abc";
        await workspace.ExecuteAsync();
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", workspace.Output);
        var parser = Assert.Single(plugin.Parsers);
        Assert.True(parser.CanParse("EX:hello"u8));
        var parsed = await parser.ParseAsync(new MemoryStream("EX:hello\r\nEX:world\n"u8.ToArray()));
        Assert.Equal("world", parsed.Records[1].Fields[0].Value);
        Assert.Equal(13, parsed.Records[1].Fields[0].Source.ByteOffset);
        using var generated = new MemoryStream();
        var generation = await Assert.Single(plugin.Generators).GenerateAsync(new Dictionary<string, string> { ["value"] = "demo" }, generated);
        Assert.True(generation.Success);
        Assert.Equal("demo    \r\n", Encoding.ASCII.GetString(generated.ToArray()));
        var profile = MessageProfile.FromJson("""
        {"SchemaVersion":1,"Id":"test","Name":"Test","SpecVersion":"1",
         "Fields":[{"Id":"mti","Name":"MTI","Labels":["Msg-type"]},
                   {"Id":"39","Name":"Response","Labels":["Resp code"],"Description":"Spec description","Decoder":"example.response"}],
         "Messages":{"0210":{"Name":"Response","Role":"reply","Replies":[],"Fields":[]}},
         "TransactionTypes":{},"Accounts":{},"SourceAccountRequiredFor":[],"DestinationAccountRequiredFor":[]}
        """);
        const string log = "TEST RECORD 1 FROM 17Jan 14:14:58.83 OUT ON DEMO SEQ: 00001\n Msg-type=0210 Resp code=00";
        var result = await new RecordMessageParser(profile, plugin.FieldDecoders).ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(log)));
        var field = result.Records[0].Fields.Single(f => f.DataElement == "39");
        Assert.Equal("Spec description", field.Description);
        Assert.Contains("Approved", Assert.Single(field.Children!).Value);
        Assert.Equal("Synthetic response-code specification\n\nCode  Meaning\n────  ────────────────────\n00    Approved (synthetic)\n05    Declined (synthetic)\n\nOther values are unmapped.", field.Specification);
        Assert.Equal("00", field.Value);
        var without = await new RecordMessageParser(profile).ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(log)));
        Assert.Contains(without.Records[0].Findings, f => f.RuleId == "decode.unavailable");
        Assert.Equal("00", without.Records[0].Fields.Single(f => f.DataElement == "39").Value);
        Assert.Null(without.Records[0].Fields.Single(f => f.DataElement == "39").Specification);
    }

    [Fact]
    public void LooseDllImportUsesTheAdjacentManifestAndOnlyDeclaredFiles()
    {
        var sourceRoot = Path.Combine(directory, "loose");
        Directory.CreateDirectory(sourceRoot);
        var dll = Path.Combine(sourceRoot, "ExamplePlugin.dll");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ExamplePlugin.dll"), dll);
        var manifest = new PluginManifest(1, 1, "example.toolkit", "Example", "1.0.0", "ExamplePlugin.dll", "ExamplePlugin.ExamplePlugin", ["ExamplePlugin.dll"]);
        File.WriteAllText(Path.Combine(sourceRoot, "plugin.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(sourceRoot, "unrelated.txt"), "do not import");
        var packages = Packages;
        Assert.Equal("example.toolkit", packages.Inspect(dll).Id);
        packages.Import(dll);
        Assert.False(File.Exists(Path.Combine(packages.Root, "example.toolkit", "unrelated.txt")));
        using var catalog = PluginCatalog.Load(packages, [], [], []);
        Assert.Single(catalog.Plugins);
    }

    [Fact]
    public void DisabledPackagesDoNotExecuteAndDuplicateImportsDoNotOverwrite()
    {
        var packages = Packages;
        var source = MakePackage();
        packages.Import(source);
        Assert.Throws<InvalidDataException>(() => packages.Import(source));
        packages.SetEnabled("example.toolkit", false);
        using (var catalog = PluginCatalog.Load(packages, [], [], [])) Assert.Empty(catalog.Plugins);
        packages.SetEnabled("example.toolkit", true);
        using var enabled = PluginCatalog.Load(packages, [], [], []);
        Assert.Single(enabled.Plugins);
    }

    [Fact]
    public void ConflictingExportsRejectEntirePackageWithoutBreakingHost()
    {
        var packages = Packages;
        packages.Import(MakePackage());
        using var catalog = PluginCatalog.Load(packages, ["EXAMPLE.SHA256"], [], []);
        Assert.Empty(catalog.Plugins);
        Assert.Contains(catalog.Diagnostics, d => d.Contains("conflicting tool ID"));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("C:/outside.dll")]
    [InlineData("undeclared.dll")]
    [InlineData("ExamplePlugin.dll")]
    public void InvalidArchivesCannotEscapeOrPartiallyInstall(string path)
    {
        var packages = Packages;
        Assert.Throws<InvalidDataException>(() => packages.Import(MakePackage(path)));
        Assert.Empty(packages.Installed());
        Assert.False(File.Exists(Path.Combine(directory, "outside.dll")));
        Assert.Empty(Directory.EnumerateDirectories(packages.Root));
    }

    [Fact]
    public void IncompatibleApiIsRejectedBeforeLoading()
    {
        var packages = Packages;
        Assert.Throws<InvalidDataException>(() => packages.Inspect(MakePackage(api: 999)));
    }

    [Fact]
    public async Task TabsHaveIndependentSessionsAndDisposeAfterCancellationCompletes()
    {
        var sessions = new List<SlowSession>();
        var definition = new PluginTool("test", "Test", "", [], () => { var session = new SlowSession(); sessions.Add(session); return session; });
        var first = new PluginToolWorkspace(definition);
        using var second = new PluginToolWorkspace(definition);
        Assert.NotSame(first.Session, second.Session);
        var operation = first.ExecuteAsync();
        await sessions[0].Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        Assert.False(sessions[0].Disposed);
        sessions[0].Release.SetResult();
        await operation;
        Assert.True(sessions[0].Disposed);
        Assert.Equal("", first.Output);
        Assert.False(sessions[1].Disposed);
    }

    private sealed class SlowSession : IPluginToolSession
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public async Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
        {
            Started.SetResult();
            await Release.Task;
            token.ThrowIfCancellationRequested();
            return new("done");
        }
        public void Dispose() => Disposed = true;
    }

    public void Dispose()
    {
        // Loaded Windows DLLs can stay locked until collectible contexts are collected.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var full = Path.GetFullPath(directory);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(full) == temp && Path.GetFileName(full).StartsWith("toolkit-plugin-test-", StringComparison.Ordinal))
        {
            try { Directory.Delete(full, recursive: true); } catch (IOException) { }
        }
    }
}
