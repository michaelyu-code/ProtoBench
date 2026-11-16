using System.Text;

namespace ProtoBench.Utilities.Networking;

public sealed record TelnetTextPreviewResult(string Text, string Commands);

/// <summary>Readable text from one Telnet stream, with ANSI/VT control sequences removed.</summary>
public sealed class TelnetTextPreviewDecoder(Encoding encoding)
{
    private enum State { Text, Escape, EscapeIntermediate, Csi, ControlString, StringEscape }
    private readonly TelnetPreviewDecoder telnet = new();
    private readonly Decoder decoder = encoding.GetDecoder();
    private State state;
    private bool osc;

    public TelnetTextPreviewResult Decode(ReadOnlySpan<byte> bytes, bool includeOutput = true)
    {
        // Earlier chunks must feed both the character decoder and terminal filter,
        // even though their displayed text and command trace are discarded.
        var result = telnet.Decode(bytes, includeOutput, includeData: true);
        var chars = new char[encoding.GetMaxCharCount(result.Data.Length)];
        var count = decoder.GetChars(result.Data, chars, flush: false);
        var text = includeOutput ? new StringBuilder(count) : null;
        foreach (var value in chars.AsSpan(0, count))
        {
            // CAN/SUB cancel an unfinished terminal sequence. ESC starts a new one,
            // except inside strings, where ESC-backslash is the terminator.
            if (value is '\u0018' or '\u001A') { state = State.Text; continue; }
            if (value == '\u001B' && state is not (State.ControlString or State.StringEscape))
            {
                state = State.Escape; continue;
            }
            switch (state)
            {
                case State.Text:
                    if (value == '\u009B') state = State.Csi;
                    else if (value is '\u0090' or '\u0098' or '\u009D' or '\u009E' or '\u009F')
                    {
                        osc = value == '\u009D'; state = State.ControlString;
                    }
                    else if (value is '\r' or '\n' or '\t') text?.Append(value);
                    else if (!char.IsControl(value)) text?.Append(value);
                    break;
                case State.Escape:
                    if (value is '[' or 'O') state = State.Csi;
                    else if (value is ']' or 'P' or 'X' or '^' or '_')
                    {
                        osc = value == ']'; state = State.ControlString;
                    }
                    else if (value is >= '\u0020' and <= '\u002F') state = State.EscapeIntermediate;
                    else if (value >= '\u0030') state = State.Text;
                    break;
                case State.EscapeIntermediate:
                    if (value >= '\u0030') state = State.Text;
                    break;
                case State.Csi:
                    if (value is >= '\u0040' and <= '\u007E') state = State.Text;
                    else if (value > '\u007E') { state = State.Text; if (!char.IsControl(value)) text?.Append(value); }
                    break;
                case State.ControlString:
                    if (value == '\u009C' || (osc && value == '\u0007')) state = State.Text;
                    else if (value == '\u001B') state = State.StringEscape;
                    break;
                case State.StringEscape:
                    if (value is '\\' or '\u009C' || (osc && value == '\u0007')) state = State.Text;
                    else if (value != '\u001B') state = State.ControlString;
                    break;
            }
        }
        return new(text?.ToString() ?? "", result.Commands);
    }
}
