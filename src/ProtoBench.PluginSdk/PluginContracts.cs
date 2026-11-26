using ProtoBench.Core.Fields;
using ProtoBench.Generation.Abstractions;
using ProtoBench.Parsing.Abstractions;

namespace ProtoBench.PluginSdk;

public static class PluginApi { public const int Version = 2; }

/// <summary>One public, parameterless entry type per package. Return stable, thread-safe exports.</summary>
public interface IToolkitPlugin : IDisposable
{
    IEnumerable<PluginTool> Tools => [];
    IEnumerable<ILogParser> Parsers => [];
    IEnumerable<IFileGenerator> Generators => [];
    IEnumerable<IFieldDecoder> FieldDecoders => [];
    IEnumerable<ProtoBench.Core.Networking.TelnetServerProvider> TelnetServers => [];
    void IDisposable.Dispose() { }
}

public enum ToolInputKind { Text, Multiline, Secret, Choice, Integer, FieldValues }

/// <summary>Choose an existing host menu, or keep the default Custom Tools menu.
/// Values are stable identifiers: never renumber or reuse a retired category's value.
/// Unknown or retired categories fall back to Custom Tools in the host.</summary>
public enum PluginToolCategory
{
    CustomTools = 0, Parsers = 1, Converters = 2, Generators = 3,
    Comparisons = 4, Utilities = 5, Payments = 6, Calculators = 7
}

/// <summary>A self-contained HTML screen, including its CSS and JavaScript. Use the
/// window.chrome.webview message bridge to execute the tool's per-tab session.</summary>
public sealed record PluginWebView(string Html);
public sealed record ToolInput(string Id, string Label, ToolInputKind Kind = ToolInputKind.Text,
    string DefaultValue = "", bool Required = false, IReadOnlyList<string>? Choices = null,
    string Description = "")
{
    /// <summary>Place this input in a collapsed optional-inputs group.</summary>
    public bool IsAdvanced { get; init; }
    public int? Minimum { get; init; }
    public int? Maximum { get; init; }
}

/// <summary>CreateSession must return a new session for every independent workspace tab.</summary>
public sealed record PluginTool(string Id, string Name, string Description, IReadOnlyList<ToolInput> Inputs,
    Func<IPluginToolSession> CreateSession, string ActionLabel = "Run", string Help = "", string Icon = "\uE713")
{
    /// <summary>Sidebar and discovery category. The host marks plugin tools as custom in every category.</summary>
    public PluginToolCategory Category { get; init; } = PluginToolCategory.CustomTools;
    /// <summary>Optional HTML screen instead of the standard form. A native custom view takes precedence.</summary>
    public PluginWebView? WebView { get; init; }
}

public interface IPluginToolSession : IDisposable
{
    Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken);
}

public sealed record PluginToolResult(string Text, IReadOnlyList<ParsedField>? Fields = null)
{
    /// <summary>Optional structured presentation. Supply at most one of Search and Decoding.
    /// Text remains the plugin's raw/copyable output. Existing constructor and deconstruction are unchanged.</summary>
    public ToolSearchResult? Search { get; init; }
    public ToolDecodeResult? Decoding { get; init; }
}
