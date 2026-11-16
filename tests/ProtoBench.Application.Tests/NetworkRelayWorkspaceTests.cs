using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ProtoBench.Desktop.Features.Networking;
using ProtoBench.Infrastructure.Files;

namespace ProtoBench.Application.Tests;

public sealed class NetworkRelayWorkspaceTests
{
    [Fact]
    public void TelnetPreviewReplaysOnlySelectedConnectionAndDirectionAndPreservesRawBytes()
    {
        using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter());
        var now = DateTimeOffset.UtcNow;
        var outbound = ProtoBench.Utilities.Networking.RelayDirection.ClientToServer;
        var inbound = ProtoBench.Utilities.Networking.RelayDirection.ServerToClient;
        workspace.Entries.Add(new(1, now, 1, outbound, "", new byte[] { 255, 251 }));
        workspace.Entries.Add(new(2, now, 2, outbound, "", new byte[] { 255, 250, 24 }));
        workspace.Entries.Add(new(3, now, 1, inbound, "", new byte[] { 255, 250, 24 }));
        var entry = new ProtoBench.Utilities.Networking.RelayCaptureEntry(4, now, 1, outbound, "", new byte[] { 1, 99, 97, 102, 233, 255, 255 });
        workspace.Entries.Add(entry);
        workspace.Selected = entry;
        workspace.TextEncoding = ProtoBench.Utilities.Text.TextEncodings.Options.Single(e => e.CodePage == 28591);
        var raw = workspace.SelectedText;
        Assert.False(workspace.TelnetPreview);
        Assert.Empty(workspace.SelectedTelnetCommands);
        workspace.TelnetPreview = true;
        Assert.Equal("caféÿ", workspace.SelectedText);
        Assert.Equal("IAC WILL ECHO (1)", workspace.SelectedTelnetCommands);
        Assert.Equal("01636166E9FFFF", workspace.SelectedHex);
        workspace.TextEncoding = ProtoBench.Utilities.Text.TextEncodings.Utf8;
        Assert.Contains("�", workspace.SelectedText);
        workspace.TextEncoding = ProtoBench.Utilities.Text.TextEncodings.Options.Single(e => e.CodePage == 28591);
        workspace.TelnetPreview = false;
        Assert.Equal(raw, workspace.SelectedText);
    }

    [Fact]
    public async Task ValidationIdentifiesEachRequiredFieldAndTlsCertificate()
    {
        using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter())
        {
            ListenAddress = " ", ListenPort = "", UpstreamHost = " ", UpstreamPort = "", IncomingMode = 1
        };
        var errors = workspace.ValidateConfiguration();
        Assert.Equal(5, errors.Count);
        Assert.Contains(nameof(workspace.ListenAddress), errors.Keys);
        Assert.Contains(nameof(workspace.ListenPort), errors.Keys);
        Assert.Contains(nameof(workspace.UpstreamHost), errors.Keys);
        Assert.Contains(nameof(workspace.UpstreamPort), errors.Keys);
        Assert.Contains(nameof(workspace.CertificatePath), errors.Keys);
        await workspace.StartAsync();
        Assert.False(workspace.IsRunning);
        Assert.Contains("upstream server", workspace.Error);
        workspace.ListenAddress = "127.0.0.1"; workspace.ListenPort = "9000";
        workspace.UpstreamHost = "localhost"; workspace.UpstreamPort = "443";
        workspace.IncomingMode = 2;
        Assert.Empty(workspace.ValidateConfiguration());
        workspace.CertificateName = " ";
        Assert.Contains(nameof(workspace.CertificateName), workspace.ValidateConfiguration().Keys);
        workspace.IncomingMode = 0;
        Assert.Empty(workspace.ValidateConfiguration());
    }

    [Fact]
    public void ChangingPreviewEncodingRedecodesSelectedBytes()
    {
        using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter());
        workspace.Selected = new(1, DateTimeOffset.UtcNow, 1, ProtoBench.Utilities.Networking.RelayDirection.ClientToServer, "", new byte[] { 0xE9, 0x80 });
        Assert.Equal("UTF-8", workspace.TextEncoding.Name);
        Assert.Contains("�", workspace.SelectedText);
        workspace.TextEncoding = ProtoBench.Utilities.Text.TextEncodings.Options.Single(e => e.CodePage == 28591);
        Assert.Equal("é\u0080", workspace.SelectedText);
        workspace.TextEncoding = ProtoBench.Utilities.Text.TextEncodings.Options.Single(e => e.CodePage == 1252);
        Assert.Equal("é€", workspace.SelectedText);
        Assert.Equal("E980", workspace.SelectedHex);
        Assert.True(workspace.AutoScroll);
        workspace.AutoScroll = false;
        Assert.False(workspace.AutoScroll);
    }

    [Fact]
    public async Task InvalidConfigurationDoesNotStartAndGeneratedCertificateExportsPublicOnly()
    {
        using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter());
        await workspace.StartAsync();
        Assert.False(workspace.IsRunning); Assert.True(workspace.CanConfigure); Assert.NotEmpty(workspace.Error);
        workspace.CreateTestCertificate();
        Assert.True(workspace.IncomingTls); Assert.Contains("SHA-256", workspace.CertificateInfo);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pem");
        try
        {
            await workspace.ExportTestCertificateAsync(path);
            var text = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("PRIVATE KEY", text);
            using var cert = X509Certificate2.CreateFromPem(text);
            Assert.False(cert.HasPrivateKey);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CaptureExportContainsExactReceivedBytesAndClosingTabReleasesListener()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var token = timeout.Token;
        using var upstream = new TcpListener(IPAddress.Loopback, 0); upstream.Start();
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
        var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        var workspace = new NetworkRelayWorkspace(new AtomicFileWriter())
        {
            ListenPort = port.ToString(), UpstreamHost = "127.0.0.1", UpstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port.ToString()
        };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            await workspace.StartAsync(); Assert.True(workspace.IsRunning); Assert.False(workspace.CanConfigure);
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port, token);
            using var server = await upstream.AcceptTcpClientAsync(token);
            await client.GetStream().WriteAsync(new byte[] { 0, 255, 65, 66 }, token);
            var received = new byte[4]; await server.GetStream().ReadExactlyAsync(received, token);
            workspace.RefreshCapture();
            workspace.Selected = Assert.Single(workspace.Entries, e => e.Direction == ProtoBench.Utilities.Networking.RelayDirection.ClientToServer);
            Assert.Equal("00FF4142", workspace.SelectedHex);
            await workspace.ExportCaptureAsync(path);
            var lines = await File.ReadAllLinesAsync(path, token);
            using var header = JsonDocument.Parse(lines[0]); Assert.Equal(4, header.RootElement.GetProperty("ClientBytes").GetInt64());
            Assert.Contains(lines, line => line.Contains("00FF4142"));
            workspace.ClearCapture(); Assert.Empty(workspace.Entries); Assert.Empty(workspace.SelectedHex); Assert.True(workspace.IsRunning);
            workspace.Dispose(); Assert.False(workspace.IsRunning); Assert.Empty(workspace.CertificatePassword);
            using var rebound = new TcpListener(IPAddress.Loopback, port); rebound.Start();
        }
        finally { workspace.Dispose(); File.Delete(path); }
    }

    [Fact]
    public async Task StopAllowsRestartAndAnUnavailablePortProducesAnError()
    {
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
        var port = ((IPEndPoint)reserve.LocalEndpoint).Port;
        using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter())
        {
            ListenPort = port.ToString(), UpstreamHost = "localhost", UpstreamPort = "443"
        };
        await workspace.StartAsync(); Assert.False(workspace.IsRunning); Assert.NotEmpty(workspace.Error);
        reserve.Stop();
        await workspace.StartAsync(); Assert.True(workspace.IsRunning);
        await workspace.StopAsync(); Assert.False(workspace.IsRunning); Assert.True(workspace.CanConfigure);
        await workspace.StartAsync(); Assert.True(workspace.IsRunning);
        await workspace.StopAsync();
    }

    [Fact]
    public async Task IncorrectPfxPasswordFailsBeforeBindingAndCorrectPasswordAllowsTlsStart()
    {
        using var certificate = ProtoBench.Utilities.Networking.RelayTestCertificate.Create("localhost");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pfx");
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
        var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        var pfx = certificate.Export(X509ContentType.Pkcs12, "test-password");
        try
        {
            await File.WriteAllBytesAsync(path, pfx);
            using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter())
            {
                ListenPort = port.ToString(), UpstreamHost = "localhost", UpstreamPort = "443",
                IncomingTls = true, CertificatePath = path, CertificatePassword = "incorrect"
            };
            await workspace.StartAsync(); Assert.False(workspace.IsRunning); Assert.Contains("password", workspace.Error);
            using (var available = new TcpListener(IPAddress.Loopback, port)) available.Start();
            workspace.CertificatePassword = "test-password";
            await workspace.StartAsync(); Assert.True(workspace.IsRunning);
            await workspace.StopAsync();
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(pfx); File.Delete(path); }
    }

    [Fact]
    public async Task AutoModeCreatesCertificateAndRetainsAutoSelectionWhenRegenerated()
    {
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
        var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter())
        {
            ListenPort = port.ToString(), UpstreamHost = "localhost", UpstreamPort = "443", IncomingMode = 2
        };
        Assert.True(workspace.AutoDetectIncomingTls); Assert.False(workspace.IncomingTls);
        await workspace.StartAsync(); Assert.True(workspace.IsRunning); Assert.Contains("SHA-256", workspace.CertificateInfo);
        await workspace.StopAsync(); workspace.CreateTestCertificate();
        Assert.Equal(2, workspace.IncomingMode);
        workspace.IncomingMode = 1; Assert.True(workspace.IncomingTls); Assert.False(workspace.AutoDetectIncomingTls);
        workspace.IncomingMode = 0; Assert.False(workspace.IncomingTls); Assert.False(workspace.AutoDetectIncomingTls);
    }

    [Fact]
    public void UpstreamMatchAndHostnameOptionsHaveClearIndependentDefaults()
    {
        using var workspace = new NetworkRelayWorkspace(new AtomicFileWriter());
        Assert.True(workspace.VerifyUpstreamHostname); Assert.Equal(0, workspace.UpstreamMode);
        Assert.True(workspace.VerifyUpstreamCertificateTrust); Assert.True(workspace.VerifyUpstreamCertificateValidity);
        workspace.VerifyUpstreamCertificateTrust = false;
        Assert.True(workspace.VerifyUpstreamCertificateValidity); Assert.True(workspace.VerifyUpstreamHostname);
        workspace.VerifyUpstreamCertificateValidity = false;
        workspace.UpstreamMode = 2; Assert.True(workspace.MatchIncomingTls); Assert.False(workspace.UpstreamTls);
        workspace.VerifyUpstreamHostname = false;
        workspace.UpstreamMode = 1; Assert.True(workspace.UpstreamTls); Assert.False(workspace.MatchIncomingTls);
        Assert.False(workspace.VerifyUpstreamHostname);
        Assert.False(workspace.VerifyUpstreamCertificateTrust); Assert.False(workspace.VerifyUpstreamCertificateValidity);
        workspace.UpstreamMode = 0; Assert.False(workspace.UpstreamTls); Assert.False(workspace.MatchIncomingTls);
    }
}
