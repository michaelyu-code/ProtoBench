using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ProtoBench.Utilities.Networking;

namespace ProtoBench.Utilities.Tests;

public sealed class TcpRelayTests
{
    private static TcpListener Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return listener;
    }

    private static SslClientAuthenticationOptions Trust(X509Certificate2 certificate, string hostname = "localhost")
    {
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = hostname, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateChainPolicy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true }
        };
        options.CertificateChainPolicy.CustomTrustStore.Add(certificate);
        return options;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RelaysExactLargeBinaryDataInBothDirectionsWithIndependentTlsLegs(bool incomingTls, bool upstreamTls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        using var incomingCert = RelayTestCertificate.Create("localhost");
        using var serverCert = RelayTestCertificate.Create("localhost");
        using var server = Listen();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var request = RandomNumberGenerator.GetBytes(100_000);
        var response = RandomNumberGenerator.GetBytes(75_123);
        var serverTask = Task.Run(async () =>
        {
            using var socket = await server.AcceptTcpClientAsync(token);
            await using Stream stream = upstreamTls ? new SslStream(socket.GetStream()) : socket.GetStream();
            if (stream is SslStream ssl) await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = serverCert, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, token);
            var data = new byte[request.Length];
            await stream.ReadExactlyAsync(data, token); Assert.Equal(request, data);
            await stream.WriteAsync(response, token);
        }, token);
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "127.0.0.1", port)
        {
            IncomingTls = incomingTls, ListenerCertificate = incomingCert,
            UpstreamTls = upstreamTls, UpstreamTrustedRoot = serverCert, TlsServerName = "localhost"
        });
        using var client = new TcpClient();
        await client.ConnectAsync(relay.LocalEndpoint, token);
        await using Stream clientStream = incomingTls ? new SslStream(client.GetStream()) : client.GetStream();
        var reply = new byte[response.Length];
        try
        {
            if (clientStream is SslStream clientSsl) await clientSsl.AuthenticateAsClientAsync(Trust(incomingCert), token);
            await clientStream.WriteAsync(request, token);
            await clientStream.ReadExactlyAsync(reply, token);
        }
        catch (Exception ex)
        {
            throw new Exception(string.Join("\n", relay.Capture.Snapshot().Entries.Select(e => e.Description)) +
                (serverTask.IsFaulted ? "\nServer: " + serverTask.Exception : ""), ex);
        }
        Assert.Equal(response, reply);
        await serverTask;
        relay.Stop(); await relay.Completion.WaitAsync(token);
        var snapshot = relay.Capture.Snapshot();
        Assert.Equal(request, snapshot.Entries.Where(e => e.Direction == RelayDirection.ClientToServer).SelectMany(e => e.Data.ToArray()).ToArray());
        Assert.Equal(response, snapshot.Entries.Where(e => e.Direction == RelayDirection.ServerToClient).SelectMany(e => e.Data.ToArray()).ToArray());
        Assert.Equal(request.Length, snapshot.ClientBytes);
        Assert.Equal(response.Length, snapshot.ServerBytes);
        Assert.DoesNotContain(snapshot.Entries, e => e.Description.StartsWith("Connection failed"));
    }

    [Fact]
    public async Task PlainTcpHalfClosePreservesResponseAfterClientFinishesSending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        using var server = Listen();
        var serverTask = Task.Run(async () =>
        {
            using var socket = await server.AcceptTcpClientAsync(token);
            var stream = socket.GetStream();
            var data = new byte[16];
            await stream.ReadExactlyAsync(data.AsMemory(0, 3), token);
            Assert.Equal(0, await stream.ReadAsync(data, token));
            await stream.WriteAsync(new byte[] { 9, 8, 7 }, token);
        }, token);
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "127.0.0.1", ((IPEndPoint)server.LocalEndpoint).Port));
        using var client = new TcpClient(); await client.ConnectAsync(relay.LocalEndpoint, token);
        var clientStream = client.GetStream();
        await clientStream.WriteAsync(new byte[] { 1, 2, 3 }, token); client.Client.Shutdown(SocketShutdown.Send);
        var response = new byte[3]; await clientStream.ReadExactlyAsync(response, token);
        Assert.Equal(new byte[] { 9, 8, 7 }, response); await serverTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsUntrustedOrWrongHostnameUpstreamCertificates(bool wrongHostname)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        using var cert = RelayTestCertificate.Create("localhost");
        using var server = Listen();
        var serverTask = Task.Run(async () =>
        {
            using var socket = await server.AcceptTcpClientAsync(token);
            using var ssl = new SslStream(socket.GetStream());
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert }, token);
                var data = new byte[1]; _ = await ssl.ReadAsync(data, token);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException) { }
        }, token);
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "127.0.0.1", ((IPEndPoint)server.LocalEndpoint).Port)
        {
            UpstreamTls = true, UpstreamTrustedRoot = wrongHostname ? cert : null,
            TlsServerName = wrongHostname ? "wrong.example" : "localhost"
        });
        using var client = new TcpClient(); await client.ConnectAsync(relay.LocalEndpoint, token);
        var data = new byte[1]; Assert.Equal(0, await client.GetStream().ReadAsync(data, token));
        await serverTask;
        Assert.Contains(relay.Capture.Snapshot().Entries, e => e.Description.Contains("AuthenticationException"));
        Assert.DoesNotContain(relay.Capture.Snapshot().Entries, e => e.Description == "Relay ready.");
    }

    [Fact]
    public async Task StopCancelsAnIncompleteTlsHandshakeAndReleasesThePort()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        using var cert = RelayTestCertificate.Create("localhost");
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "localhost", 1) { IncomingTls = true, ListenerCertificate = cert });
        using var client = new TcpClient(); await client.ConnectAsync(relay.LocalEndpoint, token);
        relay.Stop(); await relay.Completion.WaitAsync(token);
        Assert.Equal(0, relay.ActiveConnections);
        using var rebound = new TcpListener(relay.LocalEndpoint); rebound.Start();
    }

    [Fact]
    public async Task ConcurrentClientsKeepTheirConnectionIdsAndDataSeparate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        using var server = Listen();
        var echo = Task.Run(async () =>
        {
            var tasks = new List<Task>();
            for (var i = 0; i < 3; i++)
            {
                var socket = await server.AcceptTcpClientAsync(token);
                tasks.Add(Task.Run(async () =>
                {
                    using (socket)
                    {
                        var bytes = new byte[512]; await socket.GetStream().ReadExactlyAsync(bytes, token);
                        await socket.GetStream().WriteAsync(bytes, token);
                    }
                }, token));
            }
            await Task.WhenAll(tasks);
        }, token);
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "127.0.0.1", ((IPEndPoint)server.LocalEndpoint).Port));
        await Task.WhenAll(Enumerable.Range(1, 3).Select(async value =>
        {
            using var client = new TcpClient(); await client.ConnectAsync(relay.LocalEndpoint, token);
            var bytes = Enumerable.Repeat((byte)value, 512).ToArray(); await client.GetStream().WriteAsync(bytes, token);
            var reply = new byte[512]; await client.GetStream().ReadExactlyAsync(reply, token); Assert.Equal(bytes, reply);
        }));
        await echo;
        Assert.Equal(3, relay.Capture.Snapshot().Entries.Where(e => e.Direction == RelayDirection.ClientToServer).Select(e => e.ConnectionId).Distinct().Count());
    }

    [Fact]
    public void CaptureEvictsOldestEntriesAndPreservesTotalsAndExactBytes()
    {
        var buffer = new RelayCaptureBuffer(3, 4);
        buffer.Add(1, RelayDirection.ClientToServer, "", new byte[] { 0, 255, 1 });
        buffer.Add(1, RelayDirection.ServerToClient, "", new byte[] { 2, 3 });
        var snapshot = buffer.Snapshot();
        Assert.Equal(1, snapshot.EvictedEntries);
        Assert.Equal("0203", Assert.Single(snapshot.Entries).Hex);
        Assert.Equal(3, snapshot.ClientBytes); Assert.Equal(2, snapshot.ServerBytes);
        buffer.Clear(); Assert.Empty(buffer.Snapshot().Entries); Assert.Equal(3, buffer.Snapshot().ClientBytes);
        buffer.Add(2, RelayDirection.None, "one"); buffer.Add(2, RelayDirection.None, "two");
        buffer.Add(2, RelayDirection.None, "three"); buffer.Add(2, RelayDirection.None, "four");
        Assert.Equal(3, buffer.Snapshot().Entries.Count); Assert.Equal(1, buffer.Snapshot().EvictedEntries);
    }

    [Fact]
    public async Task SetupTimeoutEndsAStalledTlsClientAndListenerRemainsAvailable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        using var certificate = RelayTestCertificate.Create("localhost");
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "localhost", 1)
        {
            IncomingTls = true, ListenerCertificate = certificate, SetupTimeout = TimeSpan.FromMilliseconds(150)
        });
        using var client = new TcpClient(); await client.ConnectAsync(relay.LocalEndpoint, token);
        Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], token));
        Assert.False(relay.Completion.IsCompleted);
    }

    [Fact]
    public async Task ConnectionLimitRejectsExtraClientsAndStopClosesActiveConnections()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        using var server = Listen();
        await using var relay = TcpRelay.Start(new(IPAddress.Loopback, 0, "127.0.0.1", ((IPEndPoint)server.LocalEndpoint).Port) { MaximumConnections = 1 });
        using var first = new TcpClient(); await first.ConnectAsync(relay.LocalEndpoint, token);
        using var accepted = await server.AcceptTcpClientAsync(token);
        using var second = new TcpClient(); await second.ConnectAsync(relay.LocalEndpoint, token);
        Assert.Equal(0, await second.GetStream().ReadAsync(new byte[1], token));
        relay.Stop(); await relay.Completion.WaitAsync(token);
        Assert.Equal(0, relay.ActiveConnections);
        Assert.Contains(relay.Capture.Snapshot().Entries, e => e.Description.Contains("connection limit"));
    }

    [Fact]
    public void ValidatesOptionsAndCreatesUsablePublicTestCertificate()
    {
        using var certificate = RelayTestCertificate.Create("localhost");
        Assert.True(certificate.HasPrivateKey);
        Assert.Contains("localhost", certificate.Extensions["2.5.29.17"]!.Format(false));
        Assert.True(certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(29));
        Assert.DoesNotContain("PRIVATE KEY", certificate.ExportCertificatePem());
        Assert.Throws<FormatException>(() => RelayTestCertificate.Create("https://localhost:443"));
        Assert.Throws<ArgumentException>(() => TcpRelay.Start(new(IPAddress.Loopback, 0, "", 443)));
        Assert.Throws<ArgumentException>(() => TcpRelay.Start(new(IPAddress.Loopback, 0, "localhost", 443) { IncomingTls = true }));
        Assert.Throws<ArgumentException>(() => TcpRelay.Start(new(IPAddress.Loopback, 0, "localhost", 0)));
    }
}
