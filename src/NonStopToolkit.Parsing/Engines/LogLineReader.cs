using System.Runtime.CompilerServices;

namespace NonStopToolkit.Parsing.Engines;

internal sealed record LogLine(byte[] Bytes, long Offset, long Number, bool Oversized);

/// <summary>Bounded byte reader preserving CR, LF, CRLF and absolute 64-bit source offsets.</summary>
internal static class LogLineReader
{
    public const int MaxRecordBytes = 4 * 1024 * 1024;

    public static async IAsyncEnumerable<LogLine> ReadAsync(Stream input,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        using var line = new MemoryStream();
        long offset = 0, start = 0, number = 1;
        bool cr = false, oversized = false;
        while (true)
        {
            var count = await input.ReadAsync(buffer, token);
            if (count == 0) break;
            int segment = 0;
            for (int i = 0; i < count; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                var b = buffer[i];
                if (cr)
                {
                    if (b == 10)
                    {
                        Append(buffer, segment, i - segment + 1);
                        offset++;
                        yield return Finish();
                        segment = i + 1;
                        cr = false;
                        continue;
                    }
                    Append(buffer, segment, i - segment);
                    yield return Finish();
                    segment = i;
                    cr = false;
                }
                offset++;
                if (b == 13) cr = true;
                else if (b == 10)
                {
                    Append(buffer, segment, i - segment + 1);
                    yield return Finish();
                    segment = i + 1;
                }
            }
            Append(buffer, segment, count - segment);
        }
        if (offset > start) yield return Finish();

        void Append(byte[] data, int from, int length)
        {
            if (oversized) return;
            if (line.Length + length > MaxRecordBytes)
            {
                oversized = true;
                line.SetLength(0);
            }
            else line.Write(data, from, length);
        }
        LogLine Finish()
        {
            var result = new LogLine(line.ToArray(), start, number++, oversized);
            line.SetLength(0);
            start = offset;
            oversized = false;
            return result;
        }
    }
}
