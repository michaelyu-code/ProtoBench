using System.Net;
using System.Net.Sockets;
using System.Text;
using ProtoBench.Utilities.Networking;

namespace ProtoBench.Utilities.Tests;

public sealed class TestTelnetServerTests
{
    [Fact]
    public async Task FailedPluginFactoryClosesOnlyThatClient()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var calls = 0;
        var provider = new ProtoBench.Core.Networking.TelnetServerProvider("test.failure", "Test", "Test",
            () => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("factory failed") : new EchoSession());
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, provider: provider);
        using var first = new TcpClient();
        await first.ConnectAsync(server.LocalEndpoint, timeout.Token);
        Assert.Equal(0, await first.GetStream().ReadAsync(new byte[1], timeout.Token));
        using var second = new TcpClient();
        await second.ConnectAsync(server.LocalEndpoint, timeout.Token);
        await second.GetStream().WriteAsync("ok"u8.ToArray(), timeout.Token);
        var response = new byte[2];
        await second.GetStream().ReadExactlyAsync(response, timeout.Token);
        Assert.Equal("ok", Encoding.ASCII.GetString(response));
        Assert.Contains(server.Capture.Snapshot().Entries, e => e.Description.Contains("factory failed"));
    }

    [Fact]
    public async Task StopCancelsAndDisposesPluginSession()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = new WaitingSession();
        var provider = new ProtoBench.Core.Networking.TelnetServerProvider("test.wait", "Test", "Test", () => session);
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, provider: provider);
        using var client = new TcpClient();
        await client.ConnectAsync(server.LocalEndpoint, timeout.Token);
        await session.Started.Task.WaitAsync(timeout.Token);
        await server.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        Assert.True(session.Disposed);
        Assert.Equal(0, server.ActiveConnections);
    }

    private sealed class WaitingSession : ProtoBench.Core.Networking.ITelnetServerSession
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public async Task RunAsync(ProtoBench.Core.Networking.ITelnetServerConnection connection, CancellationToken token)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task StopReleasesClientDuringTlsHandshake()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var certificate = RelayTestCertificate.Create("localhost");
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, certificate: certificate);
        using var client = new TcpClient();
        await client.ConnectAsync(server.LocalEndpoint, timeout.Token);
        while (server.ActiveConnections == 0) await Task.Delay(10, timeout.Token);
        await server.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        Assert.Equal(0, server.ActiveConnections);
        using var rebound = new TcpListener(server.LocalEndpoint); rebound.Start();
    }

    [Theory]
    [InlineData(TelnetServerProfile.Ascii)]
    [InlineData(TelnetServerProfile.AnsiVt100)]
    [InlineData(TelnetServerProfile.TelservTn6530)]
    public async Task BuiltInProfilesRunOverTls(TelnetServerProfile profile)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var certificate = RelayTestCertificate.Create("localhost");
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, profile: profile, certificate: certificate);
        using var client = new TcpClient();
        await client.ConnectAsync(server.LocalEndpoint, timeout.Token);
        using var ssl = new System.Net.Security.SslStream(client.GetStream(), false, (_, cert, _, _) => cert?.GetCertHashString() == certificate.GetCertHashString());
        await ssl.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions { TargetHost = "localhost" }, timeout.Token);
        if (profile == TelnetServerProfile.TelservTn6530)
        {
            await ssl.ReadExactlyAsync(new byte[18], timeout.Token);
            await ssl.WriteAsync(new byte[] { 255, 252, 24 }, timeout.Token);
            Assert.Contains("TELSERV simulation", Encoding.ASCII.GetString(await ReadUntilAsync(ssl, "Service> ", timeout.Token)));
        }
        else
        {
            Assert.Contains("ProtoBench test Telnet server", Encoding.ASCII.GetString(await ReadUntilAsync(ssl, "> ", timeout.Token)));
            await ssl.WriteAsync("echo secure\r\n"u8.ToArray(), timeout.Token);
            Assert.Contains("secure", Encoding.ASCII.GetString(await ReadUntilAsync(ssl, "> ", timeout.Token)));
        }
        Assert.Contains(server.Capture.Snapshot().Entries, e => e.Description.StartsWith("TLS:"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PluginSessionsAreIndependentCapturedAndDisposed(bool tls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var certificate = tls ? RelayTestCertificate.Create("localhost") : null;
        var sessions = new System.Collections.Concurrent.ConcurrentBag<EchoSession>();
        var provider = new ProtoBench.Core.Networking.TelnetServerProvider("test.echo", "Test echo", "Test", () =>
        {
            var session = new EchoSession(); sessions.Add(session); return session;
        });
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, certificate: certificate, provider: provider);
        using var first = new TcpClient(); using var second = new TcpClient();
        foreach (var client in new[] { first, second })
        {
            await client.ConnectAsync(server.LocalEndpoint, timeout.Token);
            Stream stream = client.GetStream();
            if (tls)
            {
                var ssl = new System.Net.Security.SslStream(stream, false, (_, cert, _, _) => cert?.GetCertHashString() == certificate!.GetCertHashString());
                await ssl.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions { TargetHost = "localhost" }, timeout.Token);
                stream = ssl;
            }
            using (stream)
            {
                await stream.WriteAsync("hello"u8.ToArray(), timeout.Token);
                var response = new byte[5]; await stream.ReadExactlyAsync(response, timeout.Token);
                Assert.Equal("hello", Encoding.ASCII.GetString(response));
            }
        }
        await server.DisposeAsync();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.True(s.Disposed));
        var snapshot = server.Capture.Snapshot();
        Assert.Equal(10, snapshot.ClientBytes); Assert.Equal(10, snapshot.ServerBytes);
    }

    private sealed class EchoSession : ProtoBench.Core.Networking.ITelnetServerSession
    {
        public bool Disposed { get; private set; }
        public async Task RunAsync(ProtoBench.Core.Networking.ITelnetServerConnection connection, CancellationToken token)
        {
            var buffer = new byte[128];
            int read;
            while ((read = await connection.ReadAsync(buffer, token)) > 0)
                await connection.WriteAsync(buffer.AsMemory(0, read), token);
        }
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tn6530NegotiatesAndRunsSimulatedTaclDirectlyAndThroughRelay(bool throughRelay)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, profile: TelnetServerProfile.TelservTn6530);
        await using var relay = throughRelay ? TcpRelay.Start(new(IPAddress.Loopback, 0, "127.0.0.1", server.LocalEndpoint.Port)) : null;
        using var client = new TcpClient(); await client.ConnectAsync(relay?.LocalEndpoint ?? server.LocalEndpoint, token);
        var stream = client.GetStream();
        var negotiation = new byte[18]; await stream.ReadExactlyAsync(negotiation, token);
        Assert.Equal(new byte[] { 255, 253, 24, 255, 253, 0, 255, 251, 0, 255, 251, 1, 255, 251, 3, 255, 253, 3 }, negotiation);
        await stream.WriteAsync(new byte[] { 255, 253, 0, 255, 253, 1, 255, 253, 3, 255, 251, 0, 255, 251, 3, 255, 251, 24, 255, 251, 24 }, token);
        var request = new byte[6]; await stream.ReadExactlyAsync(request, token);
        Assert.Equal(new byte[] { 255, 250, 24, 1, 255, 240 }, request);
        byte[] terminal = [255, 250, 24, 0, .. Encoding.ASCII.GetBytes("TN6530"), 255, 240];
        foreach (var value in terminal) await stream.WriteAsync(new byte[] { value }, token);
        var menu = await ReadUntilAsync(stream, "Service> ", token);
        Assert.StartsWith("\u001BI\u001BH\u001Bb", Encoding.ASCII.GetString(menu));
        var preview = new TelnetTextPreviewDecoder(Encoding.ASCII);
        Assert.Contains("Available services: TACL, ECHO, EXIT", preview.Decode(menu).Text);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("TACL\r\n"), token);
        Assert.Contains("TACL simulation", Encoding.ASCII.GetString(await ReadUntilAsync(stream, "TACL 1> ", token)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes("SCREEN\r\n"), token);
        var screen = await ReadUntilAsync(stream, "TACL 2> ", token);
        Assert.Contains("\u001BI\u001BH\u001Bb", Encoding.ASCII.GetString(screen));
        var clean = preview.Decode(screen).Text;
        Assert.Contains("Status: READY", clean); Assert.DoesNotContain('\u001B', clean);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("FILES\r\n"), token);
        Assert.Contains("Simulated files: SAMPLE1", Encoding.ASCII.GetString(await ReadUntilAsync(stream, "TACL 3> ", token)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes("LOGOFF\r\n"), token);
        await ReadUntilAsync(stream, "Service> ", token);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("ECHO\r\n"), token);
        await ReadUntilAsync(stream, "ECHO> ", token);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("hello\r\n"), token);
        Assert.Contains("hello\r\n", Encoding.ASCII.GetString(await ReadUntilAsync(stream, "ECHO> ", token)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes("LOGOFF\r\n"), token);
        await ReadUntilAsync(stream, "Service> ", token);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("EXIT\r\n"), token);
        Assert.Contains("Simulation ended.", Encoding.ASCII.GetString(await ReadUntilAsync(stream, "Simulation ended.\r\n", token)));
        Assert.Contains(server.Capture.Snapshot().Entries, e => e.Description.Contains("Terminal type: TN6530; 6530"));
    }

    [Fact]
    public async Task DeclinedTerminalTypeUsesTextOnlyMenuAndAnsiProfileForcesAnsiSample()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, profile: TelnetServerProfile.TelservTn6530);
        using var client = new TcpClient(); await client.ConnectAsync(server.LocalEndpoint, token);
        var stream = client.GetStream();
        await stream.ReadExactlyAsync(new byte[18], token);
        await stream.WriteAsync(new byte[] { 255, 252, 24 }, token);
        var menu = Encoding.ASCII.GetString(await ReadUntilAsync(stream, "Service> ", token));
        Assert.Contains("TELSERV simulation", menu); Assert.DoesNotContain('\u001B', menu);
        await using var ansi = TestTelnetServer.Start(IPAddress.Loopback, 0, ansiGreeting: false, profile: TelnetServerProfile.AnsiVt100);
        using var ansiClient = new TcpClient(); await ansiClient.ConnectAsync(ansi.LocalEndpoint, token);
        Assert.Contains(TestTelnetServer.AnsiSample, Encoding.ASCII.GetString(await ReadUntilAsync(ansiClient.GetStream(), "> ", token)));
    }

    private static async Task<byte[]> ReadUntilAsync(Stream stream, string suffix, CancellationToken token)
    {
        var data = new List<byte>();
        var buffer = new byte[1];
        while (await stream.ReadAsync(buffer, token) != 0)
        {
            data.Add(buffer[0]);
            if (data.Count > 65536) throw new InvalidOperationException("Unexpected response size.");
            if (data.Count >= suffix.Length && Encoding.ASCII.GetString(data.TakeLast(suffix.Length).ToArray()) == suffix) break;
        }
        return data.ToArray();
    }

    [Fact]
    public async Task GreetsNegotiatesAndHandlesSplitCommandsAndCrLfWithoutDuplicatePrompts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0);
        using var client = new TcpClient(); await client.ConnectAsync(server.LocalEndpoint, token);
        var stream = client.GetStream();
        var greeting = await ReadUntilAsync(stream, "> ", token);
        Assert.Equal(new byte[] { 255, 251, 3, 255, 252, 1 }, greeting[..6]);
        var preview = new TelnetTextPreviewDecoder(Encoding.ASCII);
        var clean = preview.Decode(greeting).Text;
        Assert.Contains("Green ASCII text", clean); Assert.DoesNotContain('\u001B', clean);
        await stream.WriteAsync(new byte[] { 255, 251 }, token);
        await stream.WriteAsync(new byte[] { 24, 255, 253, 42 }, token);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("echo hel"), token);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("lo\r\n"), token);
        var response = await ReadUntilAsync(stream, "> ", token);
        Assert.Equal(new byte[] { 255, 254, 24, 255, 252, 42 }, response[..6]);
        Assert.Equal("hello\r\n> ", preview.Decode(response).Text);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("help\r\0"), token);
        Assert.Contains("Commands: help", Encoding.ASCII.GetString(await ReadUntilAsync(stream, "> ", token)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes("quit\n"), token);
        Assert.Equal("Bye.\r\n", Encoding.ASCII.GetString(await ReadUntilAsync(stream, "Bye.\r\n", token)));
        Assert.Equal(0, await stream.ReadAsync(new byte[1], token));
        Assert.Contains(server.Capture.Snapshot().Entries, e => e.Description == "Command: echo hello");
    }

    [Fact]
    public async Task RelayCanForwardToIndependentServerAndStopReleasesListenerAndClients()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, ansiGreeting: false);
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "127.0.0.1", server.LocalEndpoint.Port));
        using var client = new TcpClient(); await client.ConnectAsync(relay.LocalEndpoint, token);
        var stream = client.GetStream();
        var greeting = Encoding.ASCII.GetString(await ReadUntilAsync(stream, "> ", token));
        Assert.Contains("ProtoBench test Telnet server", greeting); Assert.DoesNotContain("Green", greeting);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("echo through relay\r\n"), token);
        Assert.Equal("through relay\r\n> ", Encoding.ASCII.GetString(await ReadUntilAsync(stream, "> ", token)));
        Assert.Contains(relay.Capture.Snapshot().Entries, e => e.Direction == RelayDirection.ServerToClient && e.Data.Length > 0);
        var port = server.LocalEndpoint.Port;
        await server.DisposeAsync();
        Assert.Equal(0, server.ActiveConnections);
        using var rebound = new TcpListener(IPAddress.Loopback, port); rebound.Start();
    }

    [Fact]
    public async Task RejectsOverlongInputAndKeepsClientsIndependent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        await using var server = TestTelnetServer.Start(IPAddress.Loopback, 0, ansiGreeting: false);
        using var first = new TcpClient(); using var second = new TcpClient();
        await first.ConnectAsync(server.LocalEndpoint, token); await second.ConnectAsync(server.LocalEndpoint, token);
        await ReadUntilAsync(first.GetStream(), "> ", token); await ReadUntilAsync(second.GetStream(), "> ", token);
        Assert.Equal(2, server.ActiveConnections);
        await first.GetStream().WriteAsync(Encoding.ASCII.GetBytes(new string('A', 1025)), token);
        Assert.Contains("exceeds 1,024", Encoding.ASCII.GetString(await ReadUntilAsync(first.GetStream(), "Closing connection.\r\n", token)));
        Assert.Equal(0, await first.GetStream().ReadAsync(new byte[1], token));
        await second.GetStream().WriteAsync(Encoding.ASCII.GetBytes("echo alive\n"), token);
        Assert.Equal("alive\r\n> ", Encoding.ASCII.GetString(await ReadUntilAsync(second.GetStream(), "> ", token)));
    }
}
