using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ProtoBench.Utilities.Certificates;

public sealed record RsaPublicComponents(int KeySize, string ModulusHex, string ModulusDecimal, string ExponentHex, string ExponentDecimal);
public sealed record CertificateInspection(string Details, string PublicKeyPem, RsaPublicComponents? Rsa);

/// <summary>Inspects a single public certificate without accessing stores or building a trust chain.</summary>
public static class CertificateInspector
{
    public const int MaximumInputBytes = 1024 * 1024;

    public static CertificateInspection InspectPem(string pem, DateTimeOffset? now = null)
    {
        if (pem.Length > MaximumInputBytes) throw new FormatException("Certificate input exceeds 1 MiB.");
        var text = pem.AsSpan().Trim().TrimStart('\uFEFF').Trim();
        if (!PemEncoding.TryFind(text, out var fields) || !text[fields.Label].SequenceEqual("CERTIFICATE") ||
            fields.Location.Start.GetOffset(text.Length) != 0 || fields.Location.End.GetOffset(text.Length) != text.Length)
            throw new FormatException("Enter exactly one PEM CERTIFICATE block. Bundles, private keys and other PEM blocks are not supported.");
        return InspectDer(Convert.FromBase64String(text[fields.Base64Data].ToString()), now);
    }

    public static CertificateInspection Inspect(ReadOnlySpan<byte> data, DateTimeOffset? now = null)
    {
        if (data.Length == 0 || data.Length > MaximumInputBytes) throw new FormatException("Enter a certificate of at most 1 MiB.");
        // DER certificates begin with SEQUENCE. Everything else must be a UTF-8 PEM certificate.
        return data[0] == 0x30 ? InspectDer(data.ToArray(), now) : InspectPem(new UTF8Encoding(false, true).GetString(data), now);
    }

    private static CertificateInspection InspectDer(byte[] der, DateTimeOffset? now)
    {
        try
        {
            var reader = new AsnReader(der, AsnEncodingRules.DER);
            reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            using var cert = X509CertificateLoader.LoadCertificate(der);
            var current = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
            var start = cert.NotBefore.ToUniversalTime();
            var end = cert.NotAfter.ToUniversalTime();
            var validity = current < start ? "Not yet valid" : current > end ? "Expired" : "Within validity dates";
            var details = new StringBuilder()
                .AppendLine($"Subject: {cert.Subject}")
                .AppendLine($"Issuer: {cert.Issuer}")
                .AppendLine($"Version: {cert.Version}")
                .AppendLine($"Serial number (hex): {cert.SerialNumber}")
                .AppendLine($"Not before (UTC): {start:O}")
                .AppendLine($"Not after (UTC): {end:O}")
                .AppendLine($"Date status: {validity}")
                .AppendLine("Trust: Not evaluated (no chain, signature, hostname or revocation validation)")
                .AppendLine($"Signature algorithm: {cert.SignatureAlgorithm.FriendlyName} ({cert.SignatureAlgorithm.Value})")
                .AppendLine($"Public key algorithm: {cert.PublicKey.Oid.FriendlyName} ({cert.PublicKey.Oid.Value})")
                .AppendLine($"SHA-256 fingerprint: {Convert.ToHexString(SHA256.HashData(cert.RawData))}")
                .AppendLine($"SHA-1 fingerprint (identifier only): {Convert.ToHexString(SHA1.HashData(cert.RawData))}");
            RsaPublicComponents? components = null;
            using var rsa = cert.GetRSAPublicKey();
            using var ec = cert.GetECDsaPublicKey();
            using var dsa = cert.GetDSAPublicKey();
            var keySize = rsa?.KeySize ?? ec?.KeySize ?? dsa?.KeySize;
            if (keySize.HasValue) details.AppendLine($"Public key size: {keySize} bits");
            if (rsa is not null)
            {
                var key = rsa.ExportParameters(false);
                components = new(rsa.KeySize, Convert.ToHexString(key.Modulus!), UnsignedDecimal(key.Modulus!),
                    Convert.ToHexString(key.Exponent!), UnsignedDecimal(key.Exponent!));
            }
            else details.AppendLine("RSA components: Not applicable to this public key algorithm.");
            foreach (var extension in cert.Extensions)
            {
                details.AppendLine().AppendLine($"{extension.Oid?.FriendlyName} ({extension.Oid?.Value}) · Critical: {extension.Critical}");
                // Preserve inspection of the rest of the certificate if an extension cannot be decoded.
                try { details.AppendLine(extension.Format(true)); }
                catch (CryptographicException) { details.AppendLine("Unable to decode extension."); }
                details.AppendLine($"Raw extension (hex): {Convert.ToHexString(extension.RawData)}");
            }
            return new(details.ToString().TrimEnd(), PemEncoding.WriteString("PUBLIC KEY", cert.PublicKey.ExportSubjectPublicKeyInfo()), components);
        }
        catch (Exception ex) when (ex is CryptographicException or AsnContentException)
        {
            throw new FormatException("Unable to read a single X.509 certificate. Use PEM or DER; PFX/PKCS#12, PKCS#7, CSRs and private keys are not supported.", ex);
        }
    }

    private static string UnsignedDecimal(byte[] value) => new BigInteger(value, isUnsigned: true, isBigEndian: true).ToString(CultureInfo.InvariantCulture);
}
