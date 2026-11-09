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
    void IDisposable.Dispose() { }
}

public enum ToolInputKind { Text, Multiline, Secret, Choice }
public sealed record ToolInput(string Id, string Label, ToolInputKind Kind = ToolInputKind.Text,
    string DefaultValue = "", bool Required = false, IReadOnlyList<string>? Choices = null,
    string Description = "");

/// <summary>CreateSession must return a new session for every independent workspace tab.</summary>
public sealed record PluginTool(string Id, string Name, string Description, IReadOnlyList<ToolInput> Inputs,
    Func<IPluginToolSession> CreateSession, string ActionLabel = "Run", string Help = "", string Icon = "\uE713");

public interface IPluginToolSession : IDisposable
{
    Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken);
}

public sealed record PluginToolResult(string Text, IReadOnlyList<ParsedField>? Fields = null);
