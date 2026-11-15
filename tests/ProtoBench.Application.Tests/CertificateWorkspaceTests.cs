using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ProtoBench.Desktop.Features.Certificates;
using ProtoBench.Infrastructure.Files;

namespace ProtoBench.Application.Tests;

public sealed class CertificateWorkspaceTests
{
    [Fact]
    public async Task EditingClearingAndFailedInspectionRemovePreviousResults()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=workspace.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var workspace = new CertificateWorkspace(new AtomicFileWriter());
        workspace.Input = certificate.ExportCertificatePem();
        await workspace.InspectAsync();
        Assert.NotNull(workspace.Rsa);
        Assert.NotEmpty(workspace.PublicKeyPem);
        workspace.Input = "bad input";
        Assert.Empty(workspace.Details);
        Assert.Empty(workspace.ModulusHex);
        Assert.Empty(workspace.PublicKeyPem);
        await workspace.InspectAsync();
        Assert.NotEqual("Completed", workspace.Status);
        Assert.Empty(workspace.Details);
        workspace.Input = certificate.ExportCertificatePem();
        var pending = workspace.InspectAsync();
        workspace.Clear();
        await pending;
        Assert.Empty(workspace.Input);
        Assert.Empty(workspace.Details);
        Assert.Null(workspace.Rsa);
    }

    [Fact]
    public async Task LoadDerAndExportPemRoundTrip()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=file.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var inputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cer");
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pem");
        try
        {
            await File.WriteAllBytesAsync(inputPath, certificate.RawData);
            using var workspace = new CertificateWorkspace(new AtomicFileWriter());
            await workspace.LoadAsync(inputPath);
            Assert.Equal("Completed", workspace.Status);
            await workspace.SavePublicKeyAsync(outputPath);
            using var imported = RSA.Create();
            imported.ImportFromPem(await File.ReadAllTextAsync(outputPath));
            Assert.Equal(rsa.ExportSubjectPublicKeyInfo(), imported.ExportSubjectPublicKeyInfo());
            await workspace.LoadAsync(inputPath + ".missing");
            Assert.Empty(workspace.PublicKeyPem);
            Assert.Empty(workspace.Details);
        }
        finally { File.Delete(inputPath); File.Delete(outputPath); }
    }
}
