using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ProtoBench.Utilities.Networking;

/// <summary>A small, unauthenticated ASCII Telnet endpoint for testing clients and relays.</summary>
public sealed class TestTelnetServer : IAsyncDisposable
{
    public const int MaximumConnections = 8, MaximumLineLength = 1024;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<long, TcpClient> sockets = new();
    private readonly ConcurrentDictionary<long, Task> clients = new();
    private readonly bool ansiGreeting;
    public TelnetServerProfile Profile { get; }
    private long nextConnection;
    private int stopped, disposed;
    public RelayCaptureBuffer Capture { get; } = new();
    public IPEndPoint LocalEndpoint { get; }
    public Task Completion { get; }
    public int ActiveConnections => sockets.Count;
    public const string AnsiSample = "\u001B[32mGreen ASCII text\u001B[0m\r\n\u001B]0;ProtoBench Telnet test\aPlain text after title control.\r\n";

    private TestTelnetServer(IPAddress address, int port, bool ansiGreeting, TelnetServerProfile profile)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (!Enum.IsDefined(profile)) throw new ArgumentException("Select a supported server profile.", nameof(profile));
        Profile = profile;
        this.ansiGreeting = ansiGreeting;
        listener = new(address, port);
        try { listener.Start(); }
        catch { listener.Stop(); stop.Dispose(); throw; }
        LocalEndpoint = (IPEndPoint)listener.LocalEndpoint;
        Capture.Add(0, RelayDirection.None, $"Listening on {LocalEndpoint} (plain TCP Telnet; profile: {Profile}).");
        Completion = AcceptAsync();
    }

    public static TestTelnetServer Start(IPAddress address, int port = 2323, bool ansiGreeting = true,
        TelnetServerProfile profile = TelnetServerProfile.Ascii) => new(address, port, ansiGreeting, profile);

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                if (sockets.Count >= MaximumConnections)
                {
                    client.Dispose(); Capture.Add(0, RelayDirection.None, "Connection rejected: 8-client limit reached."); continue;
                }
                var id = Interlocked.Increment(ref nextConnection);
                sockets[id] = client;
                var task = HandleAsync(client, id, stop.Token);
                clients[id] = task;
                _ = task.ContinueWith(completed => { _ = completed.Exception; clients.TryRemove(id, out _); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (stop.IsCancellationRequested && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception ex) { Capture.Add(0, RelayDirection.None, $"Listener failed: {ex.Message}"); }
        finally
        {
            Stop();
            await Task.WhenAll(clients.Values).ConfigureAwait(false);
            Capture.Add(0, RelayDirection.None, "Server stopped.");
        }
    }

    private async Task HandleAsync(TcpClient client, long id, CancellationToken token)
    {
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            Capture.Add(id, RelayDirection.None, $"Connected: {client.Client.RemoteEndPoint}.");
            async Task SendBytes(byte[] bytes)
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                Capture.Add(id, RelayDirection.ServerToClient, "Sent", bytes);
            }
            Task Send(string text) => SendBytes(Encoding.ASCII.GetBytes(text));
            if (Profile == TelnetServerProfile.TelservTn6530)
            {
                await RunTn6530Async(stream, id, SendBytes, token).ConfigureAwait(false); return;
            }
            // Announce suppress-go-ahead; leave character echo to the client.
            await SendBytes([255, 251, 3, 255, 252, 1]).ConfigureAwait(false);
            await Send("ProtoBench test Telnet server\r\nNo login required. Type help for commands.\r\n").ConfigureAwait(false);
            if (ansiGreeting || Profile == TelnetServerProfile.AnsiVt100) await Send(AnsiSample).ConfigureAwait(false);
            await Send("> ").ConfigureAwait(false);
            var replies = new List<byte>();
            var suppressGoAhead = true;
            var decoder = new TelnetPreviewDecoder((command, option) =>
            {
                void Reply(byte verb) { replies.Add(255); replies.Add(verb); replies.Add(option); }
                if (command == 253)
                {
                    if (option != 3) Reply(252); // WONT unsupported options.
                    else if (!suppressGoAhead) { suppressGoAhead = true; Reply(251); }
                }
                else if (command == 254 && option == 3 && suppressGoAhead) { suppressGoAhead = false; Reply(252); }
                else if (command == 251) Reply(254); // DONT peer options.
            });
            var line = new StringBuilder();
            var afterCr = false;
            var buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(TimeSpan.FromMinutes(5));
                var read = await stream.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                if (read == 0) break;
                Capture.Add(id, RelayDirection.ClientToServer, "Received", buffer.AsSpan(0, read));
                replies.Clear();
                var decoded = decoder.Decode(buffer.AsSpan(0, read));
                if (replies.Count > 0) await SendBytes(replies.ToArray()).ConfigureAwait(false);
                foreach (var value in decoded.Data)
                {
                    if (afterCr && value is 0 or 10) { afterCr = false; continue; }
                    afterCr = value == 13;
                    if (value is 10 or 13)
                    {
                        var input = line.ToString(); line.Clear();
                        Capture.Add(id, RelayDirection.None, $"Command: {input}");
                        var command = input.Trim();
                        if (command.Equals("quit", StringComparison.OrdinalIgnoreCase))
                        {
                            await Send("Bye.\r\n").ConfigureAwait(false); return;
                        }
                        var response = command.ToLowerInvariant() switch
                        {
                            "help" => "Commands: help, echo <text>, ansi, time, quit\r\nOther input is echoed back.\r\n",
                            "ansi" => AnsiSample,
                            "time" => $"UTC: {DateTimeOffset.UtcNow:O}\r\n",
                            "" => "",
                            _ => command.StartsWith("echo ", StringComparison.OrdinalIgnoreCase) ? command[5..] + "\r\n" : "You said: " + input + "\r\n"
                        };
                        await Send(response + "> ").ConfigureAwait(false);
                    }
                    else if (value is 8 or 127) { if (line.Length > 0) line.Length--; }
                    else if (value is 9 or >= 32 and <= 126)
                    {
                        if (line.Length >= MaximumLineLength)
                        {
                            await Send("\r\nInput exceeds 1,024 characters. Closing connection.\r\n").ConfigureAwait(false); return;
                        }
                        line.Append((char)value);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Capture.Add(id, RelayDirection.None, "Client idle timeout (5 minutes)."); }
        catch (Exception ex) when (token.IsCancellationRequested && ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        catch (Exception ex) { Capture.Add(id, RelayDirection.None, $"Client failed: {ex.Message}"); }
        finally
        {
            client.Dispose(); sockets.TryRemove(id, out _);
            Capture.Add(id, RelayDirection.None, "Connection closed.");
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        stop.Cancel(); listener.Stop();
        foreach (var socket in sockets.Values) socket.Dispose();
    }

    private async Task RunTn6530Async(NetworkStream stream, long id, Func<byte[], Task> send, CancellationToken token)
    {
        var session = new Tn6530TestSession(message => Capture.Add(id, RelayDirection.None, message));
        await send(Tn6530TestSession.Negotiation).ConfigureAwait(false);
        var buffer = new byte[4096];
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(token);
        setup.CancelAfter(TimeSpan.FromSeconds(30));
        while (!token.IsCancellationRequested)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(TimeSpan.FromMinutes(5));
            int read;
            try { read = await stream.ReadAsync(buffer, session.IsReady ? idle.Token : setup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!session.IsReady && !token.IsCancellationRequested)
            {
                throw new TimeoutException("TN6530 terminal negotiation timed out after 30 seconds.");
            }
            if (read == 0) break;
            Capture.Add(id, RelayDirection.ClientToServer, "Received", buffer.AsSpan(0, read));
            var response = session.Process(buffer.AsSpan(0, read));
            if (response.Length > 0) await send(response).ConfigureAwait(false);
            if (session.CloseRequested) break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop(); await Completion.ConfigureAwait(false);
        if (Interlocked.Exchange(ref disposed, 1) == 0) stop.Dispose();
    }
}
