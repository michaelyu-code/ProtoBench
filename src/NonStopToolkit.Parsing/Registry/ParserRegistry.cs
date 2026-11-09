using NonStopToolkit.Parsing.Abstractions;
namespace NonStopToolkit.Parsing.Registry;

public interface IParserRegistry
{
    IReadOnlyList<ILogParser> Parsers
    {
        get;
    }
    ILogParser Get(string id);
    IReadOnlyList<ILogParser> Detect(ReadOnlySpan<byte> sample);
}
public sealed class ParserRegistry : IParserRegistry
{
    public IReadOnlyList<ILogParser> Parsers
    {
        get;
    }
    public ParserRegistry(IEnumerable<ILogParser> parsers)
    {
        var list = parsers.OrderByDescending(p => p.DetectionPriority).ToArray();
        if (list.Any(p => string.IsNullOrWhiteSpace(p.FormatId)) || list.Select(p => p.FormatId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length)
            throw new ArgumentException("Empty or duplicate parser ID.");
        Parsers = Array.AsReadOnly(list);
    }
    public ILogParser Get(string id) => Parsers.FirstOrDefault(p => p.FormatId.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"Unknown parser '{id}'.");
    public IReadOnlyList<ILogParser> Detect(ReadOnlySpan<byte> sample)
    {
        var matches = new List<ILogParser>();
        foreach (var parser in Parsers)
            if (parser.CanParse(sample))
                matches.Add(parser);
        return matches;
    }
}
