using System.Text;
using ProtoBench.Application.Workspaces;
using ProtoBench.Core.Documents;
using ProtoBench.Utilities.Certificates;

namespace ProtoBench.Desktop.Features.Certificates;

public sealed class CertificateWorkspace(IAtomicFileWriter writer) : ToolWorkspace
{
    public override string ToolId => "certificate-inspector";
    public override string Title => "X.509 Certificate Inspector";
    private string input = "", details = "", publicKeyPem = "";
    private RsaPublicComponents? rsa;
    private int revision;
    public string Input
    {
        get => input;
        set { if (SetProperty(ref input, value)) { revision++; ResetResults(); } }
    }
    public string Details { get => details; private set => SetProperty(ref details, value); }
    public string PublicKeyPem { get => publicKeyPem; private set => SetProperty(ref publicKeyPem, value); }
    public RsaPublicComponents? Rsa
    {
        get => rsa;
        private set
        {
            SetProperty(ref rsa, value);
            OnPropertyChanged(nameof(ModulusHex)); OnPropertyChanged(nameof(ModulusDecimal));
            OnPropertyChanged(nameof(ExponentHex)); OnPropertyChanged(nameof(ExponentDecimal));
        }
    }
    public string ModulusHex => Rsa?.ModulusHex ?? "";
    public string ModulusDecimal => Rsa?.ModulusDecimal ?? "";
    public string ExponentHex => Rsa?.ExponentHex ?? "";
    public string ExponentDecimal => Rsa?.ExponentDecimal ?? "";
    private void ResetResults() { Details = PublicKeyPem = ""; Rsa = null; }
    private void Apply(CertificateInspection result) { Details = result.Details; PublicKeyPem = result.PublicKeyPem; Rsa = result.Rsa; }
    public Task InspectAsync() => RunAsync(async token =>
    {
        var version = revision; var text = Input;
        ResetResults();
        var result = await Task.Run(() => CertificateInspector.InspectPem(text), token);
        token.ThrowIfCancellationRequested();
        if (version == revision) Apply(result);
    });
    public Task LoadAsync(string path) => RunAsync(async token =>
    {
        Input = ""; ResetResults(); var version = ++revision;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > CertificateInspector.MaximumInputBytes) throw new FormatException("Certificate input exceeds 1 MiB.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + read > CertificateInspector.MaximumInputBytes) throw new FormatException("Certificate input exceeds 1 MiB.");
            buffer.Write(chunk, 0, read);
        }
        var result = await Task.Run(() => CertificateInspector.Inspect(buffer.ToArray()), token);
        token.ThrowIfCancellationRequested();
        if (version == revision) Apply(result);
    });
    public Task SavePublicKeyAsync(string path) => RunAsync(async token =>
    {
        var pem = PublicKeyPem;
        if (pem.Length == 0) throw new InvalidOperationException("Inspect a certificate before exporting its public key.");
        await writer.WriteAsync(path, async (stream, ct) => await stream.WriteAsync(Encoding.ASCII.GetBytes(pem + "\n"), ct), token);
    });
    public void Clear() { Cancel(); Input = ""; revision++; ResetResults(); Status = "Ready"; }
    public override void Dispose() { base.Dispose(); Clear(); }
}
