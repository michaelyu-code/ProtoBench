using NonStopToolkit.Core.Documents;
namespace NonStopToolkit.Parsing.Abstractions;

public interface ILogParser
{
    string FormatId
    {
        get;
    }
    string Name
    {
        get;
    }
    int DetectionPriority
    {
        get;
    }
    bool CanParse(ReadOnlySpan<byte> sample);
    Task<ParseResult> ParseAsync(Stream input, CancellationToken cancellationToken = default);
}
