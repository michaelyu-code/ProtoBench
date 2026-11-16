namespace ProtoBench.Utilities.Networking;

public sealed record TelnetPreviewResult(byte[] Data, string Commands);

/// <summary>Passive RFC 854 command decoding for one connection and direction.</summary>
public sealed class TelnetPreviewDecoder
{
    private enum State { Data, Iac, Option, SubOption, SubData, SubIac }
    private State state;
    private byte command, subOption;
    private readonly List<byte> subData = [];
    private long subLength;
    private const int MaxPayloadPreview = 64, MaxCommands = 256;

    // Replaying earlier chunks keeps command state without allocating their output.
    public TelnetPreviewResult Decode(ReadOnlySpan<byte> bytes, bool includeOutput = true)
    {
        var data = includeOutput ? new List<byte>() : null;
        var commands = includeOutput ? new List<string>() : null;
        var omitted = 0;
        void Report(string text)
        {
            if (commands is null) return;
            if (commands.Count < MaxCommands) commands.Add(text);
            else omitted++;
        }
        void StartCommand(byte value)
        {
            command = value;
            if (value is >= 251 and <= 254) state = State.Option;
            else if (value == 250) state = State.SubOption;
            else { if (commands is not null) Report($"IAC {CommandName(value)}"); state = State.Data; }
        }
        void SubByte(byte value)
        {
            if (subData.Count < MaxPayloadPreview) subData.Add(value);
            subLength++;
        }
        foreach (var value in bytes)
        {
            switch (state)
            {
                case State.Data:
                    if (value == 255) state = State.Iac;
                    else data?.Add(value);
                    break;
                case State.Iac:
                    if (value == 255) { data?.Add(255); state = State.Data; }
                    else StartCommand(value);
                    break;
                case State.Option:
                    if (commands is not null) Report($"IAC {CommandName(command)} {OptionName(value)}");
                    state = State.Data;
                    break;
                case State.SubOption:
                    subOption = value; subData.Clear(); subLength = 0; state = State.SubData;
                    break;
                case State.SubData:
                    if (value == 255) state = State.SubIac;
                    else SubByte(value);
                    break;
                case State.SubIac:
                    if (value == 255) { SubByte(255); state = State.SubData; }
                    else if (value == 240)
                    {
                        if (commands is not null) Report($"IAC SB {OptionName(subOption)} · {subLength} payload bytes: {Convert.ToHexString(subData.ToArray())}" +
                            (subLength > MaxPayloadPreview ? "… (preview limited to 64 bytes)" : "") + " · IAC SE");
                        state = State.Data;
                    }
                    else
                    {
                        if (commands is not null) Report($"Malformed SB {OptionName(subOption)}: unexpected IAC {CommandName(value)} before IAC SE.");
                        StartCommand(value);
                    }
                    break;
            }
        }
        if (commands is not null)
        {
            if (omitted > 0) commands.Add($"{omitted} additional commands omitted from preview.");
            if (state != State.Data)
                commands.Add("Incomplete Telnet command; waiting for a later capture entry (or truncated traffic).");
        }
        return new(data?.ToArray() ?? [], commands is null ? "" : string.Join("\n", commands));
    }

    private static string CommandName(byte value) => value switch
    {
        240 => "SE", 241 => "NOP", 242 => "DM", 243 => "BRK", 244 => "IP", 245 => "AO",
        246 => "AYT", 247 => "EC", 248 => "EL", 249 => "GA", 250 => "SB", 251 => "WILL",
        252 => "WONT", 253 => "DO", 254 => "DONT", _ => $"command 0x{value:X2}"
    };

    private static string OptionName(byte value)
    {
        var name = value switch
        {
            0 => "BINARY", 1 => "ECHO", 3 => "SUPPRESS-GO-AHEAD", 5 => "STATUS", 6 => "TIMING-MARK",
            24 => "TERMINAL-TYPE", 25 => "END-OF-RECORD", 31 => "NAWS", 32 => "TERMINAL-SPEED",
            33 => "REMOTE-FLOW-CONTROL", 34 => "LINEMODE", 36 => "ENVIRON", 37 => "AUTHENTICATION",
            38 => "ENCRYPT", 39 => "NEW-ENVIRON", 40 => "TN3270E", 42 => "CHARSET", 44 => "COM-PORT-CONTROL",
            46 => "START-TLS", 255 => "EXTENDED-OPTIONS-LIST", _ => "Unknown option"
        };
        return $"{name} ({value})";
    }
}
