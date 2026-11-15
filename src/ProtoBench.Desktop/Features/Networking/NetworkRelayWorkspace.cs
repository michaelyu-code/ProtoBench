using System.Collections.ObjectModel;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ProtoBench.Application.Workspaces;
using ProtoBench.Core.Documents;
using ProtoBench.Utilities.Networking;

namespace ProtoBench.Desktop.Features.Networking;

public sealed class NetworkRelayWorkspace(IAtomicFileWriter writer) : ToolWorkspace
{
    public override string ToolId => "network-relay";
    public override string Title => "TCP / TLS Relay";
    private string listenAddress = "127.0.0.1", listenPort = "9000", upstreamHost = "", upstreamPort = "", tlsServerName = "";
    private string certificatePath = "", certificatePassword = "", certificateName = "localhost", upstreamRootPath = "";
    private string certificateInfo = "No incoming TLS certificate selected.", summary = "No captured traffic.", error = "";
    private bool incomingTls, autoDetectIncomingTls, upstreamTls, matchIncomingTls, running, disposed;
    private bool verifyUpstreamHostname = true;
    private bool verifyUpstreamCertificateTrust = true, verifyUpstreamCertificateValidity = true;
    private long lastSequence;
    private RelayCaptureEntry? selected;
    private X509Certificate2? generatedCertificate, activeCertificate, activeRoot;
    private TcpRelay? relay;
    private RelayCaptureBuffer capture = new();
    public ObservableCollection<RelayCaptureEntry> Entries { get; } = [];
    public string ListenAddress { get => listenAddress; set => SetProperty(ref listenAddress, value); }
    public string ListenPort { get => listenPort; set => SetProperty(ref listenPort, value); }
    public string UpstreamHost { get => upstreamHost; set => SetProperty(ref upstreamHost, value); }
    public string UpstreamPort { get => upstreamPort; set => SetProperty(ref upstreamPort, value); }
    public string TlsServerName { get => tlsServerName; set => SetProperty(ref tlsServerName, value); }
    public string CertificatePath
    {
        get => certificatePath;
        set
        {
            if (!SetProperty(ref certificatePath, value)) return;
            generatedCertificate?.Dispose(); generatedCertificate = null;
            CertificateInfo = value.Length == 0 ? "No incoming TLS certificate selected." : "PFX/P12 will be loaded when the relay starts.";
        }
    }
    public string CertificatePassword { get => certificatePassword; set => SetProperty(ref certificatePassword, value); }
    public string CertificateName { get => certificateName; set => SetProperty(ref certificateName, value); }
    public string UpstreamRootPath { get => upstreamRootPath; set => SetProperty(ref upstreamRootPath, value); }
    public bool IncomingTls { get => incomingTls; set { if (SetProperty(ref incomingTls, value)) OnPropertyChanged(nameof(IncomingMode)); } }
    public bool AutoDetectIncomingTls { get => autoDetectIncomingTls; set { if (SetProperty(ref autoDetectIncomingTls, value)) OnPropertyChanged(nameof(IncomingMode)); } }
    public int IncomingMode
    {
        get => AutoDetectIncomingTls ? 2 : IncomingTls ? 1 : 0;
        set
        {
            if (value is < 0 or > 2) return;
            var changed = incomingTls != (value == 1) || autoDetectIncomingTls != (value == 2);
            incomingTls = value == 1; autoDetectIncomingTls = value == 2;
            if (!changed) return;
            OnPropertyChanged(nameof(IncomingTls)); OnPropertyChanged(nameof(AutoDetectIncomingTls)); OnPropertyChanged();
        }
    }
    public bool UpstreamTls { get => upstreamTls; set { if (SetProperty(ref upstreamTls, value)) OnPropertyChanged(nameof(UpstreamMode)); } }
    public bool MatchIncomingTls { get => matchIncomingTls; set { if (SetProperty(ref matchIncomingTls, value)) OnPropertyChanged(nameof(UpstreamMode)); } }
    public bool VerifyUpstreamHostname { get => verifyUpstreamHostname; set => SetProperty(ref verifyUpstreamHostname, value); }
    public bool VerifyUpstreamCertificateTrust { get => verifyUpstreamCertificateTrust; set => SetProperty(ref verifyUpstreamCertificateTrust, value); }
    public bool VerifyUpstreamCertificateValidity { get => verifyUpstreamCertificateValidity; set => SetProperty(ref verifyUpstreamCertificateValidity, value); }
    public int UpstreamMode
    {
        get => MatchIncomingTls ? 2 : UpstreamTls ? 1 : 0;
        set
        {
            if (value is < 0 or > 2) return;
            var changed = upstreamTls != (value == 1) || matchIncomingTls != (value == 2);
            upstreamTls = value == 1; matchIncomingTls = value == 2;
            if (!changed) return;
            OnPropertyChanged(nameof(UpstreamTls)); OnPropertyChanged(nameof(MatchIncomingTls)); OnPropertyChanged();
        }
    }
    public bool IsRunning
    {
        get => running;
        private set { SetProperty(ref running, value); OnPropertyChanged(nameof(CanConfigure)); }
    }
    public bool CanConfigure => !IsRunning && !IsBusy && !disposed;
    public string CertificateInfo { get => certificateInfo; private set => SetProperty(ref certificateInfo, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public RelayCaptureEntry? Selected
    {
        get => selected;
        set
        {
            SetProperty(ref selected, value);
            OnPropertyChanged(nameof(SelectedHex)); OnPropertyChanged(nameof(SelectedText)); OnPropertyChanged(nameof(SelectedInfo));
        }
    }
    public string SelectedHex => Selected?.Hex ?? "";
    public string SelectedText => Selected?.Text ?? "";
    public string SelectedInfo => Selected is null ? "Select a capture entry to inspect it." : $"{Selected.Timestamp:O}\n{Selected.Summary}\n{Selected.Description}";

    public void CreateTestCertificate()
    {
        if (!CanConfigure) return;
        Error = "";
        try { CreateTestCertificateCore(); }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException) { Error = ex.Message; }
    }

    private void CreateTestCertificateCore()
    {
        var certificate = RelayTestCertificate.Create(CertificateName.Trim());
        CertificatePath = "";
        generatedCertificate?.Dispose(); generatedCertificate = certificate;
        if (!AutoDetectIncomingTls) IncomingTls = true;
        CertificateInfo = $"Test certificate for {CertificateName.Trim()} · expires {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd} UTC\nSHA-256: {Convert.ToHexString(SHA256.HashData(certificate.RawData))}\nExport the public certificate for your client's trust configuration. This test identity is discarded when you regenerate it or close this tab.";
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (stream.Length > 1024 * 1024) throw new FormatException("Certificate file exceeds 1 MiB.");
        var bytes = new byte[checked((int)stream.Length)];
        try
        {
            await stream.ReadExactlyAsync(bytes, token);
            if (stream.ReadByte() != -1) throw new FormatException("Certificate file grew beyond the input limit.");
            return bytes;
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    private async Task<X509Certificate2> LoadIncomingCertificateAsync(CancellationToken token)
    {
        byte[] bytes;
        if (generatedCertificate is not null) bytes = generatedCertificate.Export(X509ContentType.Pkcs12);
        else
        {
            if (string.IsNullOrWhiteSpace(CertificatePath)) throw new FormatException("Create a test certificate or choose a PFX/P12 for incoming TLS.");
            bytes = await ReadBoundedAsync(CertificatePath, token);
        }
        try
        {
            var cert = RelayTestCertificate.LoadForTls(bytes, generatedCertificate is null ? CertificatePassword : null);
            if (!cert.HasPrivateKey) { cert.Dispose(); throw new FormatException("The incoming TLS certificate has no private key."); }
            return cert;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task StartAsync()
    {
        if (!CanConfigure) return;
        IsBusy = true; OnPropertyChanged(nameof(CanConfigure)); Error = "";
        try
        {
            if (!IPAddress.TryParse(ListenAddress.Trim(), out var address)) throw new FormatException("Enter a local listen IP address, such as 127.0.0.1 or 0.0.0.0.");
            if (!int.TryParse(ListenPort, out var port) || port is < 1 or > 65535 || !int.TryParse(UpstreamPort, out var targetPort) || targetPort is < 1 or > 65535)
                throw new FormatException("Listen and upstream ports must be between 1 and 65535.");
            var options = new TcpRelayOptions(address, port, UpstreamHost.Trim(), targetPort)
            {
                IncomingTls = IncomingTls, AutoDetectIncomingTls = AutoDetectIncomingTls, UpstreamTls = UpstreamTls,
                MatchIncomingTls = MatchIncomingTls, VerifyUpstreamHostname = VerifyUpstreamHostname,
                VerifyUpstreamCertificateTrust = VerifyUpstreamCertificateTrust, VerifyUpstreamCertificateValidity = VerifyUpstreamCertificateValidity,
                TlsServerName = TlsServerName.Trim()
            };
            if (AutoDetectIncomingTls && generatedCertificate is null && string.IsNullOrWhiteSpace(CertificatePath)) CreateTestCertificateCore();
            if (IncomingTls || AutoDetectIncomingTls) activeCertificate = await LoadIncomingCertificateAsync(CancellationToken.None);
            if ((UpstreamTls || MatchIncomingTls) && !string.IsNullOrWhiteSpace(UpstreamRootPath))
            {
                var rootBytes = await ReadBoundedAsync(UpstreamRootPath, CancellationToken.None);
                activeRoot = X509CertificateLoader.LoadCertificate(rootBytes);
            }
            if (disposed) return;
            var nextCapture = new RelayCaptureBuffer();
            relay = TcpRelay.Start(options with { ListenerCertificate = activeCertificate, UpstreamTrustedRoot = activeRoot }, nextCapture);
            capture = nextCapture; Entries.Clear(); Selected = null; lastSequence = 0;
            IsRunning = true;
            Status = $"Listening on {relay.LocalEndpoint} → {options.UpstreamHost}:{targetPort}";
            RefreshCapture();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Net.Sockets.SocketException or CryptographicException or FormatException)
        {
            Error = ex is CryptographicException ? "Unable to load TLS certificate. Check the PFX/P12, password and private key, or the upstream CA certificate." : ex.Message;
            Status = "Unable to start relay.";
        }
        finally
        {
            if (!IsRunning) ReleaseActiveCertificates();
            IsBusy = false; OnPropertyChanged(nameof(CanConfigure));
        }
    }

    public async Task StopAsync()
    {
        if (relay is null || IsBusy) return;
        IsBusy = true; OnPropertyChanged(nameof(CanConfigure));
        var current = relay;
        try { await current.DisposeAsync(); Status = "Stopped. Captured traffic retained in this tab."; }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            relay = null; IsRunning = false; IsBusy = false;
            ReleaseActiveCertificates(); RefreshCapture(); OnPropertyChanged(nameof(CanConfigure));
        }
    }

    public void RefreshCapture()
    {
        if (disposed) return;
        var snapshot = capture.Snapshot();
        var first = snapshot.Entries.Count == 0 ? long.MaxValue : snapshot.Entries[0].Sequence;
        while (Entries.Count > 0 && Entries[0].Sequence < first) Entries.RemoveAt(0);
        if (Selected is not null && !snapshot.Entries.Any(e => e.Sequence == Selected.Sequence)) Selected = null;
        foreach (var entry in snapshot.Entries.Where(e => e.Sequence > lastSequence))
        {
            Entries.Add(entry); lastSequence = entry.Sequence;
            if (entry.Description.StartsWith("Connection failed:", StringComparison.Ordinal)) Error = entry.Description;
        }
        Summary = $"{relay?.ActiveConnections ?? 0} active connections · Client → Server: {snapshot.ClientBytes:N0} bytes · Server → Client: {snapshot.ServerBytes:N0} bytes\n{snapshot.Entries.Count:N0} retained entries · {snapshot.EvictedEntries:N0} older entries evicted (limit: 1,024 entries / 4 MiB).";
        if (IsRunning && relay?.Completion.IsCompleted == true) _ = StopAsync();
    }

    public void ClearCapture() { capture.Clear(); Entries.Clear(); Selected = null; RefreshCapture(); }

    public async Task ExportCaptureAsync(string path)
    {
        var snapshot = capture.Snapshot();
        await writer.WriteAsync(path, async (stream, token) =>
        {
            await using var output = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            await output.WriteLineAsync(JsonSerializer.Serialize(new { Type = "Capture", snapshot.EvictedEntries, snapshot.ClientBytes, snapshot.ServerBytes }).AsMemory(), token);
            foreach (var entry in snapshot.Entries)
                await output.WriteLineAsync(JsonSerializer.Serialize(new { entry.Sequence, entry.Timestamp, entry.ConnectionId,
                    Direction = entry.Direction.ToString(), entry.Description, ByteCount = entry.Data.Length, Hex = entry.Hex }).AsMemory(), token);
            await output.FlushAsync(token);
        });
    }

    public async Task ExportTestCertificateAsync(string path)
    {
        if (generatedCertificate is null) throw new InvalidOperationException("Create a test certificate first. This exports only its public certificate.");
        var bytes = Encoding.ASCII.GetBytes(generatedCertificate.ExportCertificatePem() + "\n");
        await writer.WriteAsync(path, async (stream, token) => await stream.WriteAsync(bytes, token));
    }

    private void ReleaseActiveCertificates()
    {
        activeCertificate?.Dispose(); activeCertificate = null;
        activeRoot?.Dispose(); activeRoot = null;
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true; base.Dispose();
        var current = relay; relay = null;
        current?.Stop();
        var certificate = activeCertificate; var root = activeRoot;
        activeCertificate = activeRoot = null;
        if (current is null) { certificate?.Dispose(); root?.Dispose(); }
        else _ = ReleaseAfterStopAsync(current, certificate, root);
        generatedCertificate?.Dispose(); generatedCertificate = null;
        CertificatePassword = CertificatePath = "";
        capture.Clear(); Entries.Clear(); Selected = null; IsRunning = false;
    }

    private static async Task ReleaseAfterStopAsync(TcpRelay current, X509Certificate2? certificate, X509Certificate2? root)
    {
        try { await current.DisposeAsync(); }
        catch { /* Socket shutdown has already been requested; release key material in every case. */ }
        finally { certificate?.Dispose(); root?.Dispose(); current.Capture.Clear(); }
    }
}
