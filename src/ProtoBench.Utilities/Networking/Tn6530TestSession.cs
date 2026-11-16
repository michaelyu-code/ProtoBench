using System.Text;

namespace ProtoBench.Utilities.Networking;

/// <summary>Telserv-style service menu and conversational 6530 test data; no host commands execute.</summary>
internal sealed class Tn6530TestSession
{
    private readonly TelnetPreviewDecoder decoder;
    private readonly List<byte> output = [];
    private readonly StringBuilder line = new();
    private readonly HashSet<byte> local = [], remote = [];
    private readonly HashSet<byte> localPending = [0, 1, 3], remotePending = [0, 3, 24];
    private bool terminalRequested, ready, tn6530, inTacl, inEcho, afterCr;
    private int prompt = 1;
    public bool CloseRequested { get; private set; }
    public bool IsReady => ready;
    public static byte[] Negotiation => [255, 253, 24, 255, 253, 0, 255, 251, 0, 255, 251, 1, 255, 251, 3, 255, 253, 3];

    public Tn6530TestSession(Action<string> log)
    {
        decoder = new TelnetPreviewDecoder((command, option) =>
        {
            void Reply(byte verb) => output.AddRange([255, verb, option]);
            switch (command)
            {
                case 253: // DO: enable only offered server options.
                    if (option is 0 or 1 or 3)
                    {
                        var pending = localPending.Remove(option);
                        if (local.Add(option) && !pending) Reply(251);
                    }
                    else Reply(252);
                    break;
                case 254:
                    localPending.Remove(option);
                    if (local.Remove(option)) Reply(252);
                    break;
                case 251: // WILL: accept binary, suppress-go-ahead and terminal type.
                    if (option is 0 or 3 or 24)
                    {
                        var pending = remotePending.Remove(option);
                        if (remote.Add(option) && !pending) Reply(253);
                        if (option == 24 && !terminalRequested)
                        {
                            terminalRequested = true;
                            output.AddRange([255, 250, 24, 1, 255, 240]); // TERMINAL-TYPE SEND.
                        }
                    }
                    else Reply(254);
                    break;
                case 252:
                    remotePending.Remove(option);
                    if (remote.Remove(option)) Reply(254);
                    if (option == 24 && !ready)
                    {
                        log("Terminal type declined; using text-only conversational simulation.");
                        ready = true; Menu();
                    }
                    break;
            }
        }, (option, payload) =>
        {
            if (option != 24 || !terminalRequested || !remote.Contains(24) || ready) return;
            var bytes = payload.Span;
            if (bytes.Length is < 2 or > 41 || bytes[0] != 0) return; // TERMINAL-TYPE IS, up to 40 chars.
            foreach (var value in bytes[1..]) if (value is < 32 or > 126) return;
            var terminal = Encoding.ASCII.GetString(bytes[1..]);
            tn6530 = terminal.ToUpperInvariant() is "TN6530" or "TN653X" or "6530" or "653X" or "TN6530-8";
            log($"Terminal type: {terminal}; {(tn6530 ? "6530" : "text-only")} conversational simulation.");
            ready = true; Menu();
        });
    }

    private void Send(string text) => output.AddRange(Encoding.ASCII.GetBytes(text));
    private string Prompt => inTacl ? $"TACL {prompt}> " : inEcho ? "ECHO> " : "Service> ";
    private void Screen()
    {
        // 6530 Programmer's Guide: clear memory, home cursor, unlock keyboard.
        if (tn6530) Send("\u001BI\u001BH\u001Bb");
    }
    private void Menu()
    {
        inTacl = inEcho = false; Screen();
        Send("ProtoBench NonStop test system\r\nTELSERV simulation - no real host services\r\n" +
            "Available services: TACL, ECHO, EXIT\r\nService> ");
    }

    public byte[] Process(ReadOnlySpan<byte> input)
    {
        output.Clear();
        var decoded = decoder.Decode(input);
        foreach (var value in decoded.Data)
        {
            if (CloseRequested) break;
            if (!ready) continue; // Wait for terminal negotiation, as real terminal clients do.
            if (afterCr && value is 0 or 10) { afterCr = false; continue; }
            afterCr = value == 13;
            if (value is 10 or 13)
            {
                Send("\r\n");
                var command = line.ToString().Trim(); line.Clear();
                Run(command);
            }
            else if (value is 8 or 127)
            {
                if (line.Length > 0) { line.Length--; if (local.Contains(1)) Send("\b \b"); }
            }
            else if (value is >= 32 and <= 126)
            {
                if (line.Length >= TestTelnetServer.MaximumLineLength)
                {
                    Send("\r\nInput exceeds 1,024 characters. Closing connection.\r\n"); CloseRequested = true; break;
                }
                line.Append((char)value);
                if (local.Contains(1)) output.Add(value);
            }
        }
        return output.ToArray();
    }

    private void Run(string command)
    {
        var lower = command.ToLowerInvariant();
        if (inEcho)
        {
            if (lower is "logoff" or "exit") { Menu(); return; }
            if (command.Length > 0) Send(command + "\r\n");
            Send(Prompt); return;
        }
        if (!inTacl)
        {
            if (lower is "exit" or "quit") { Send("Simulation ended.\r\n"); CloseRequested = true; return; }
            if (lower == "tacl")
            {
                inTacl = true; prompt = 1; Screen();
                Send("ProtoBench TACL simulation\r\nType HELP for available simulated commands. No login required.\r\n");
            }
            else if (lower == "echo") { inEcho = true; Send("Echo test service. Type LOGOFF to return to the menu.\r\n"); }
            else if (lower.Length > 0 && lower != "help") Send("Unknown service. Choose TACL, ECHO or EXIT.\r\n");
            Send(Prompt); return;
        }
        if (lower is "exit" or "logoff") { Menu(); return; }
        if (lower is "screen" or "clear")
        {
            Screen(); Send("ProtoBench 6530 conversational sample\r\nSystem: \\TEST    Volume: $DATA.SAMPLE\r\nStatus: READY\r\n");
        }
        else if (lower == "help") Send("Simulated commands: HELP, STATUS, TIME, VOLUME, FILES, ECHO <text>, SCREEN, LOGOFF\r\n");
        else if (lower == "status") Send("TEST SYSTEM READY - simulated NonStop session; no real processes.\r\n");
        else if (lower == "time") Send($"UTC: {DateTimeOffset.UtcNow:O}\r\n");
        else if (lower == "volume") Send("Current simulated volume: \\TEST.$DATA.SAMPLE\r\n");
        else if (lower == "files") Send("Simulated files: SAMPLE1  SAMPLE2  README\r\n");
        else if (lower.StartsWith("echo ", StringComparison.Ordinal)) Send(command[5..] + "\r\n");
        else if (lower.Length > 0) Send("Unsupported simulated command. Type HELP.\r\n");
        prompt++; Send(Prompt);
    }
}
