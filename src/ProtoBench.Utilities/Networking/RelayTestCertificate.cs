using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProtoBench.Utilities.Networking;

public static class RelayTestCertificate
{
    // Schannel cannot use EphemeralKeySet. Without PersistKeySet, .NET removes
    // the temporary user key container when the certificate's handles are released.
    // This does not install a certificate in a trusted certificate store.
    public static X509Certificate2 LoadForTls(byte[] pfx, string? password, bool exportable = false) =>
        X509CertificateLoader.LoadPkcs12(pfx, password,
            (OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet) |
            (exportable ? X509KeyStorageFlags.Exportable : X509KeyStorageFlags.DefaultKeySet));

    public static X509Certificate2 Create(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname) || hostname.Length > 253 || Uri.CheckHostName(hostname) == UriHostNameType.Unknown)
            throw new FormatException("Enter the hostname or IP address your client will use to connect to the relay.");
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(new X500DistinguishedName("CN=ProtoBench test relay"), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(hostname, out var address)) names.AddIpAddress(address);
        else names.AddDnsName(hostname);
        if (hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            names.AddIpAddress(IPAddress.Loopback); names.AddIpAddress(IPAddress.IPv6Loopback);
        }
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(30));
        // Detach from the temporary RSA object's lifetime using TLS-compatible key storage.
        var pfx = certificate.Export(X509ContentType.Pkcs12);
        try { return LoadForTls(pfx, null, exportable: true); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
}
