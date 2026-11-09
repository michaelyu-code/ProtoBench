using NonStopToolkit.Core.Documents;
using NonStopToolkit.Parsing.Abstractions;

namespace NonStopToolkit.Infrastructure.Plugins;

/// <summary>A faulty detection probe must not prevent detection by the remaining parsers.</summary>
internal sealed class PluginLogParser : ILogParser
{
    private readonly ILogParser parser;
    public string FormatId { get; }
    public string Name { get; }
    public int DetectionPriority { get; }
    public PluginLogParser(ILogParser parser)
    {
        this.parser = parser;
        FormatId = parser.FormatId;
        Name = parser.Name;
        DetectionPriority = parser.DetectionPriority;
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Parser needs a name.");
    }
    public bool CanParse(ReadOnlySpan<byte> sample)
    {
        try { return parser.CanParse(sample); }
        catch (Exception) { return false; }
    }
    public Task<ParseResult> ParseAsync(Stream input, CancellationToken token = default) => parser.ParseAsync(input, token);
}
