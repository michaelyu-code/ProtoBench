using System.Text;
using System.Text.RegularExpressions;
using NonStopToolkit.Core.Documents;
using NonStopToolkit.Parsing.Abstractions;

namespace NonStopToolkit.Parsing.Engines;

public sealed partial class JsonDefinedParser
{
    public async Task ParseToAsync(Stream input, ILogParseSink sink, CancellationToken cancellationToken = default)
    {
        await foreach (var line in LogLineReader.ReadAsync(input, cancellationToken))
        {
            sink.ReportProgress(input.CanSeek ? input.Position : line.Offset + line.Bytes.Length);
            if (line.Oversized)
            {
                sink.AddDiagnostic(new("parse.line-limit", "Record exceeds 4 MiB and was skipped; parsing continued.", Line: line.Number));
                continue;
            }
            try
            {
                var length = line.Bytes.Length;
                while (length > 0 && line.Bytes[length - 1] is 10 or 13) length--;
                var text = encoding.GetString(line.Bytes, 0, length);
                var bom = line.Offset == 0 && text.StartsWith('\uFEFF') ? 3 : 0;
                if (bom != 0) text = text[1..];
                sink.AddRecord(new ParsedRecord(line.Number, text, Extract(text, line.Offset + bom)));
            }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException or EncoderFallbackException or RegexMatchTimeoutException)
            {
                sink.AddDiagnostic(new("parse.record", ex is RegexMatchTimeoutException ? "Regex exceeded the 100 ms record limit."
                    : ex is DecoderFallbackException ? "Record contains invalid encoded bytes." : ex.Message, Line: line.Number));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
