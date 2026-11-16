using System.Net;
using System.Net.Sockets;
using ProtoBench.Application.Workspaces;
using ProtoBench.Utilities.Networking;

namespace ProtoBench.Desktop.Features.Networking;

public sealed class TestTelnetServerWorkspace : ToolWorkspace
{
    public override string ToolId => "test-telnet-server";
    public override string Title => "Test Telnet Server";
    private string listenAddress = "127.0.0.1", listenPort = "2323", endpoint = "", error = "", activity = "", summary = "Server stopped.";
    private bool ansiGreeting = true, disposed;
    private long lastSequence;
    private TestTelnetServer? server;
    private TelnetServerProfile profile;
    public TelnetServerProfile Profile
    {
        get => profile;
        set
        {
            if (SetProperty(ref profile, value))
            {
                OnPropertyChanged(nameof(ProfileDescription)); OnPropertyChanged(nameof(ShowAnsiGreeting));
            }
        }
    }
    public bool ShowAnsiGreeting => Profile == TelnetServerProfile.Ascii;
    public string ProfileDescription => Profile switch
    {
        TelnetServerProfile.Ascii => "ASCII Telnet with echo, help, time and an optional ANSI sample.",
        TelnetServerProfile.AnsiVt100 => "ANSI/VT100 text session with color and title control samples. Commands: help, echo <text>, ansi, time, quit.",
        TelnetServerProfile.TelservTn6530 => "Simulated Telserv service menu and TN6530 conversational session. Negotiates terminal type, binary, echo and suppress-go-ahead. Choose TACL, then HELP or SCREEN. No real host commands, authentication or block-mode forms.",
        _ => "Select a supported server profile."
    };
    public string ListenAddress { get => listenAddress; set => SetProperty(ref listenAddress, value); }
    public string ListenPort { get => listenPort; set => SetProperty(ref listenPort, value); }
    public bool AnsiGreeting { get => ansiGreeting; set => SetProperty(ref ansiGreeting, value); }
    public string Endpoint { get => endpoint; private set => SetProperty(ref endpoint, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Activity { get => activity; private set => SetProperty(ref activity, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public bool IsRunning => server is not null;
    public bool CanConfigure => !IsRunning && !IsBusy && !disposed;
    public bool CanStop => IsRunning && !IsBusy && !disposed;

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(CanConfigure)); OnPropertyChanged(nameof(CanStop));
    }

    public void Start()
    {
        if (!CanConfigure) return;
        Error = "";
        try
        {
            if (!IPAddress.TryParse(ListenAddress.Trim(), out var address)) throw new FormatException("Enter a local listen IP address, such as 127.0.0.1.");
            if (!int.TryParse(ListenPort, out var port) || port is < 0 or > 65535) throw new FormatException("Enter a port between 1 and 65535, or 0 for an automatically assigned port.");
            server = TestTelnetServer.Start(address, port, AnsiGreeting, Profile);
            Endpoint = server.LocalEndpoint.ToString();
            Status = $"Listening on {Endpoint}";
            lastSequence = 0; Activity = "";
            Refresh();
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or FormatException)
        {
            Error = ex.Message; Status = "Unable to start server.";
        }
        NotifyState();
    }

    public void Refresh()
    {
        if (disposed || server is null) return;
        var snapshot = server.Capture.Snapshot();
        var sequence = snapshot.Entries.LastOrDefault()?.Sequence ?? 0;
        if (sequence != lastSequence)
        {
            Activity = string.Join("\n", snapshot.Entries.TakeLast(200).Select(e =>
                $"{e.Timestamp:HH:mm:ss.fff} UTC · Connection {e.ConnectionId} · {e.Description}" +
                (e.Data.Length > 0 ? $" · {e.Data.Length:N0} bytes" : "")));
            lastSequence = sequence;
        }
        Summary = $"{server.ActiveConnections} active connections · Received {snapshot.ClientBytes:N0} bytes · Sent {snapshot.ServerBytes:N0} bytes · Showing up to 200 recent activity entries.";
        if (server.Completion.IsCompleted && !IsBusy) _ = StopAsync();
    }

    public void ClearActivity()
    {
        server?.Capture.Clear(); lastSequence = 0; Activity = "";
    }

    public async Task StopAsync()
    {
        if (server is null || IsBusy) return;
        IsBusy = true; NotifyState();
        var current = server;
        try
        {
            await current.DisposeAsync(); Refresh();
            Status = "Stopped. Activity retained in this tab.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            server = null; IsBusy = false;
            Summary = "Server stopped. All client connections closed.";
            NotifyState();
        }
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true; base.Dispose();
        var current = server; server = null;
        current?.Stop();
        if (current is not null) _ = ReleaseAsync(current);
        Activity = ""; NotifyState();
    }

    private static async Task ReleaseAsync(TestTelnetServer current)
    {
        try { await current.DisposeAsync(); }
        catch { /* Closing the tab has already requested socket shutdown. */ }
        finally { current.Capture.Clear(); }
    }
}
