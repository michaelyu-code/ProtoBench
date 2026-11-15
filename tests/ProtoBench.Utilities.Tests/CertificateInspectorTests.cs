using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProtoBench.Utilities.Certificates;

namespace ProtoBench.Utilities.Tests;

public sealed class CertificateInspectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

    private static X509Certificate2 CreateRsa(RSA rsa)
    {
        var request = new CertificateRequest("CN=inspector.example, O=ProtoBench", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("inspector.example");
        san.AddIpAddress(IPAddress.Parse("192.0.2.1"));
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        return request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(1));
    }

    [Fact]
    public void RsaCertificateExtractsExactUnsignedComponentsAndExportsPublicOnlyKey()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = CreateRsa(rsa);
        var result = CertificateInspector.Inspect(certificate.RawData, Now);
        var components = Assert.IsType<RsaPublicComponents>(result.Rsa);
        var expected = rsa.ExportParameters(false);
        Assert.Equal(2048, components.KeySize);
        Assert.Equal(Convert.ToHexString(expected.Modulus!), components.ModulusHex);
        Assert.Equal("010001", components.ExponentHex);
        Assert.Equal("65537", components.ExponentDecimal);
        Assert.Equal(new System.Numerics.BigInteger(expected.Modulus!, true, true).ToString(), components.ModulusDecimal);
        Assert.DoesNotContain("-", components.ModulusDecimal);
        using var imported = RSA.Create();
        imported.ImportFromPem(result.PublicKeyPem);
        Assert.Equal(expected.Modulus, imported.ExportParameters(false).Modulus);
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", result.PublicKeyPem);
        Assert.Contains("inspector.example", result.Details);
        Assert.Contains("192.0.2.1", result.Details);
        Assert.Contains("2.5.29.15", result.Details);
        Assert.Contains("2.5.29.19", result.Details);
        Assert.Contains("Within validity dates", result.Details);
        Assert.Contains("Trust: Not evaluated", result.Details);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(certificate.RawData)), result.Details);
    }

    [Fact]
    public void PemAndDerMatchAndDatesAreEvaluatedWithoutTrustValidation()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = CreateRsa(rsa);
        var pem = certificate.ExportCertificatePem();
        Assert.Equal(CertificateInspector.Inspect(certificate.RawData, Now), CertificateInspector.InspectPem("\uFEFF\n" + pem + "\n", Now));
        Assert.Equal(CertificateInspector.InspectPem(pem, Now), CertificateInspector.Inspect(Encoding.UTF8.GetBytes(pem), Now));
        Assert.Contains("Expired", CertificateInspector.InspectPem(pem, Now.AddDays(2)).Details);
        Assert.Contains("Not yet valid", CertificateInspector.InspectPem(pem, Now.AddDays(-2)).Details);
    }

    [Fact]
    public void EcCertificateIsInspectableAndExportsEcPublicKeyWithoutRsaComponents()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ec.example", ec, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(1));
        var result = CertificateInspector.Inspect(certificate.RawData, Now);
        Assert.Null(result.Rsa);
        Assert.Contains("256 bits", result.Details);
        Assert.Contains("Not applicable", result.Details);
        using var imported = ECDsa.Create();
        imported.ImportFromPem(result.PublicKeyPem);
        Assert.Equal(ec.ExportSubjectPublicKeyInfo(), imported.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void RejectsBundlesTrailingBytesPrivateKeysAndPfx()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = CreateRsa(rsa);
        var pem = certificate.ExportCertificatePem();
        Assert.Throws<FormatException>(() => CertificateInspector.InspectPem(pem + "\n" + pem));
        Assert.Throws<FormatException>(() => CertificateInspector.InspectPem("garbage\n" + pem));
        Assert.Throws<FormatException>(() => CertificateInspector.InspectPem(rsa.ExportRSAPrivateKeyPem()));
        Assert.Throws<FormatException>(() => CertificateInspector.InspectPem(pem + rsa.ExportRSAPrivateKeyPem()));
        Assert.Throws<FormatException>(() => CertificateInspector.Inspect(certificate.RawData.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<FormatException>(() => CertificateInspector.Inspect(certificate.Export(X509ContentType.Pfx)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a certificate")]
    [InlineData("-----BEGIN CERTIFICATE-----\n!!!!\n-----END CERTIFICATE-----")]
    [InlineData("-----BEGIN CERTIFICATE-----\nMAA=\n-----END CERTIFICATE-----")]
    public void RejectsMalformedInput(string text) => Assert.Throws<FormatException>(() => CertificateInspector.InspectPem(text));

    [Fact]
    public void RejectsOversizeAndEmptyBinaryInput()
    {
        Assert.Throws<FormatException>(() => CertificateInspector.Inspect(new byte[CertificateInspector.MaximumInputBytes + 1]));
        Assert.Throws<FormatException>(() => CertificateInspector.InspectPem(new string(' ', CertificateInspector.MaximumInputBytes + 1)));
        Assert.Throws<FormatException>(() => CertificateInspector.Inspect(Array.Empty<byte>()));
    }
}
