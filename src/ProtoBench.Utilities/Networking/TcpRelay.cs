using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace ProtoBench.Utilities.Networking;

public sealed record TcpRelayOptions(IPAddress ListenAddress, int ListenPort, string UpstreamHost, int UpstreamPort)
{
    public bool IncomingTls { get; init; }
    public bool UpstreamTls { get; init; }
    public string TlsServerName { get; init; } = "";
    // Caller owns these certificates and must retain them until Completion finishes.
    public X509Certificate2? ListenerCertificate { get; init; }
    public X509Certificate2? UpstreamTrustedRoot { get; init; }
    public int MaximumConnections { get; init; } = 16;
    public TimeSpan SetupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(ListenAddress);
        if (ListenPort is < 0 or > 65535 || UpstreamPort is < 1 or > 65535) throw new ArgumentException("Ports must be between 1 and 65535 (listener may use 0 for an automatic port).");
        if (string.IsNullOrWhiteSpace(UpstreamHost) || UpstreamHost.Length > 253 || Uri.CheckHostName(UpstreamHost) == UriHostNameType.Unknown)
            throw new ArgumentException("Enter an upstream hostname or IP address, without a scheme or port.");
        if (TlsServerName.Length > 253 || TlsServerName.Length > 0 && Uri.CheckHostName(TlsServerName) == UriHostNameType.Unknown) throw new ArgumentException("Enter a valid TLS server name.");
        if (IncomingTls && ListenerCertificate?.HasPrivateKey != true) throw new ArgumentException("Incoming TLS needs a server certificate with its private key.");
        if (MaximumConnections is < 1 or > 128 || SetupTimeout <= TimeSpan.Zero || SetupTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentException("Invalid connection limit or setup timeout.");
    }
}

/// <summary>A direct TCP relay. TLS is terminated independently on each configured leg.</summary>
public sealed class TcpRelay : IAsyncDisposable
{
    private readonly TcpRelayOptions options;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<long, Task> clients = new();
    private readonly ConcurrentDictionary<long, TcpClient> sockets = new();
    private long nextConnection;
    private int active, stopped, disposed;
    public RelayCaptureBuffer Capture { get; }
    public IPEndPoint LocalEndpoint { get; }
    public Task Completion { get; }
    public int ActiveConnections => Volatile.Read(ref active);

    private TcpRelay(TcpRelayOptions options, RelayCaptureBuffer capture)
    {
        options.Validate();
        this.options = options; Capture = capture;
        listener = new TcpListener(options.ListenAddress, options.ListenPort);
        listener.Start();
        LocalEndpoint = (IPEndPoint)listener.LocalEndpoint;
        if (options.UpstreamPort == LocalEndpoint.Port &&
            (options.UpstreamHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) && (IPAddress.IsLoopback(options.ListenAddress) || options.ListenAddress.Equals(IPAddress.Any) || options.ListenAddress.Equals(IPAddress.IPv6Any)) ||
             IPAddress.TryParse(options.UpstreamHost, out var address) && (address.Equals(options.ListenAddress) || IPAddress.IsLoopback(address) && IPAddress.IsLoopback(options.ListenAddress))))
        {
            listener.Stop(); stop.Dispose();
            throw new ArgumentException("The upstream endpoint points back to this listener.");
        }
        Capture.Add(0, RelayDirection.None, $"Listening on {LocalEndpoint}; upstream {options.UpstreamHost}:{options.UpstreamPort}");
        Completion = AcceptAsync();
    }

    public static TcpRelay Start(TcpRelayOptions options, RelayCaptureBuffer? capture = null) => new(options, capture ?? new());

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                if (clients.Count >= options.MaximumConnections)
                {
                    client.Dispose(); Capture.Add(0, RelayDirection.None, "Connection rejected: concurrent connection limit reached."); continue;
                }
                var id = Interlocked.Increment(ref nextConnection);
                sockets[id] = client;
                var task = HandleAsync(client, id, stop.Token);
                clients[id] = task;
                _ = task.ContinueWith(completed => { _ = completed.Exception; clients.TryRemove(id, out _); }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (stop.IsCancellationRequested && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception ex) { Capture.Add(0, RelayDirection.None, $"Listener failed: {ex.Message}"); }
        finally
        {
            stop.Cancel(); listener.Stop();
            foreach (var socket in sockets.Values) socket.Dispose();
            await Task.WhenAll(clients.Values).ConfigureAwait(false);
            Capture.Add(0, RelayDirection.None, "Listener stopped.");
        }
    }

    private async Task HandleAsync(TcpClient client, long id, CancellationToken token)
    {
        Interlocked.Increment(ref active);
        using var upstream = new TcpClient();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var abort = connection.Token.Register(() => { client.Dispose(); upstream.Dispose(); });
        SslStream? incomingSsl = null, upstreamSsl = null;
        try
        {
            client.NoDelay = upstream.NoDelay = true;
            Capture.Add(id, RelayDirection.None, $"Client connected: {client.Client.RemoteEndPoint}");
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
            setup.CancelAfter(options.SetupTimeout);
            Stream incoming = client.GetStream();
            if (options.IncomingTls)
            {
                incomingSsl = new SslStream(incoming, leaveInnerStreamOpen: true);
                await incomingSsl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = options.ListenerCertificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false
                }, setup.Token).ConfigureAwait(false);
                incoming = incomingSsl;
                Capture.Add(id, RelayDirection.None, $"Client TLS: {incomingSsl.SslProtocol} · {incomingSsl.NegotiatedCipherSuite}");
            }
            await upstream.ConnectAsync(options.UpstreamHost, options.UpstreamPort, setup.Token).ConfigureAwait(false);
            // Catch aliases of the listener after DNS resolution as well as literal self-connections.
            if (upstream.Client.RemoteEndPoint is IPEndPoint remote && remote.Port == LocalEndpoint.Port &&
                (remote.Address.Equals(LocalEndpoint.Address) || (IPAddress.IsLoopback(remote.Address) && (IPAddress.IsLoopback(LocalEndpoint.Address) || LocalEndpoint.Address.Equals(IPAddress.Any) || LocalEndpoint.Address.Equals(IPAddress.IPv6Any)))))
                throw new IOException("The upstream endpoint resolves back to this listener.");
            Stream outgoing = upstream.GetStream();
            if (options.UpstreamTls)
            {
                upstreamSsl = new SslStream(outgoing, leaveInnerStreamOpen: true);
                var authentication = new SslClientAuthenticationOptions
                {
                    TargetHost = string.IsNullOrWhiteSpace(options.TlsServerName) ? options.UpstreamHost : options.TlsServerName,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                };
                if (options.UpstreamTrustedRoot is not null)
                {
                    authentication.CertificateChainPolicy = new X509ChainPolicy
                    {
                        TrustMode = X509ChainTrustMode.CustomRootTrust,
                        RevocationMode = X509RevocationMode.NoCheck,
                        DisableCertificateDownloads = true
                    };
                    authentication.CertificateChainPolicy.CustomTrustStore.Add(options.UpstreamTrustedRoot);
                    authentication.CertificateChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
                }
                await upstreamSsl.AuthenticateAsClientAsync(authentication, setup.Token).ConfigureAwait(false);
                outgoing = upstreamSsl;
                Capture.Add(id, RelayDirection.None, $"Upstream TLS: {upstreamSsl.SslProtocol} · {upstreamSsl.NegotiatedCipherSuite}; certificate and hostname validated.");
            }
            Capture.Add(id, RelayDirection.None, "Relay ready.");
            var forward = PumpAsync(incoming, outgoing, upstream.Client, id, RelayDirection.ClientToServer, connection);
            var reverse = PumpAsync(outgoing, incoming, client.Client, id, RelayDirection.ServerToClient, connection);
            await Task.WhenAll(forward, reverse).ConfigureAwait(false);
        }
        catch (Exception ex) when (token.IsCancellationRequested && ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        catch (Exception ex) { Capture.Add(id, RelayDirection.None, $"Connection failed: {ex.GetType().Name}: {ex.GetBaseException().Message}"); }
        finally
        {
            connection.Cancel();
            try { if (incomingSsl is not null) await incomingSsl.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Capture.Add(id, RelayDirection.None, $"Client TLS cleanup: {ex.Message}"); }
            try { if (upstreamSsl is not null) await upstreamSsl.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Capture.Add(id, RelayDirection.None, $"Upstream TLS cleanup: {ex.Message}"); }
            client.Dispose(); sockets.TryRemove(id, out _);
            Interlocked.Decrement(ref active);
            Capture.Add(id, RelayDirection.None, "Connection closed.");
        }
    }

    private async Task PumpAsync(Stream source, Stream destination, Socket destinationSocket, long id, RelayDirection direction, CancellationTokenSource connection)
    {
        try
        {
            var bytes = new byte[16384];
            int read;
            while ((read = await source.ReadAsync(bytes, connection.Token).ConfigureAwait(false)) != 0)
            {
                // Capture received application bytes even if the subsequent network write fails.
                Capture.Add(id, direction, "Received", bytes.AsSpan(0, read));
                await destination.WriteAsync(bytes.AsMemory(0, read), connection.Token).ConfigureAwait(false);
            }
            if (destination is SslStream ssl) await ssl.ShutdownAsync().WaitAsync(connection.Token).ConfigureAwait(false);
            else destinationSocket.Shutdown(SocketShutdown.Send);
            Capture.Add(id, RelayDirection.None, direction == RelayDirection.ClientToServer ? "Client finished sending." : "Server finished sending.");
        }
        catch { connection.Cancel(); throw; }
    }

    /// <summary>Immediately closes listening and connected sockets. Completion drains the connection tasks.</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        stop.Cancel(); listener.Stop();
        foreach (var socket in sockets.Values) socket.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await Completion.ConfigureAwait(false);
        if (Interlocked.Exchange(ref disposed, 1) == 0) stop.Dispose();
    }
}
