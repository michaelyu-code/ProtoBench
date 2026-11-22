using System.Security.Cryptography;
using System.Text;

namespace ProtoBench.Utilities.Binary;

public sealed record TripleDesCalculation(string Output, string Details);

public static partial class TripleDesCalculator
{
    /// <summary>Provider-verified output with block-level EDE, padding and chaining details.</summary>
    public static TripleDesCalculation TransformDetailed(string inputHex, string keyHex, string ivHex,
        bool decrypt, CipherMode mode, PaddingMode padding, bool includeKeyValues = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Keep validation, weak-key handling and the authoritative result in the normal path.
        var result = Transform(inputHex, keyHex, ivHex, decrypt, mode, padding);
        byte[]? key = null, input = null, output = null, blocks = null, chain = null, mixed = null;
        try
        {
            key = HexBytes.Parse(keyHex);
            input = HexBytes.Parse(inputHex);
            output = HexBytes.Parse(result);
            chain = mode == CipherMode.CBC ? HexBytes.Parse(ivHex) : new byte[8];
            mixed = new byte[8];
            var report = new StringBuilder();
            report.AppendLine($"3DES EDE · {(decrypt ? "Decrypt" : "Encrypt")} · {mode} · {padding}");
            report.AppendLine($"Key: {key.Length} bytes · {(key.Length == 16 ? "two-key K1/K2/K1 (K3 = K1)" : "three-key K1/K2/K3")}");
            for (var part = 0; part < 3; part++)
            {
                var offset = part == 2 && key.Length == 16 ? 0 : part * 8;
                report.AppendLine($"K{part + 1}: {(includeKeyValues ? Convert.ToHexString(key.AsSpan(offset, 8)) : "[hidden]")}");
            }
            report.AppendLine(mode == CipherMode.CBC ? $"IV: {Convert.ToHexString(chain)}" : "IV: not used in ECB");
            report.AppendLine("E = DES encryption; D = DES decryption. Functions below apply from the inside out.");
            report.AppendLine("X is the plaintext block in ECB, or plaintext XOR chain in CBC.");
            report.AppendLine("This trace shows 3DES block boundaries and chaining, not the internal DES rounds.");

            if (decrypt)
            {
                // Obtain the plaintext before padding removal for an exact per-block trace.
                blocks = HexBytes.Parse(Transform(inputHex, keyHex, ivHex, true, mode, PaddingMode.None));
                var removed = blocks.Length - output.Length;
                report.AppendLine(padding == PaddingMode.PKCS7
                    ? $"Padding: validate and remove {removed} byte(s): {Convert.ToHexString(blocks.AsSpan(output.Length))}"
                    : padding == PaddingMode.Zeros ? "Padding: zero bytes are retained; they may be data." : "Padding: none.");
            }
            else
            {
                var added = padding == PaddingMode.PKCS7 ? 8 - input.Length % 8
                    : padding == PaddingMode.Zeros ? (8 - input.Length % 8) % 8 : 0;
                blocks = new byte[input.Length + added];
                input.CopyTo(blocks, 0);
                if (padding == PaddingMode.PKCS7) blocks.AsSpan(input.Length).Fill((byte)added);
                report.AppendLine($"Padding: add {added} byte(s){(added > 0 ? ": " + Convert.ToHexString(blocks.AsSpan(input.Length)) : ".")}");
                report.AppendLine($"Padded plaintext: {Convert.ToHexString(blocks)}");
            }

            for (var offset = 0; offset < blocks.Length; offset += 8)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plain = blocks.AsSpan(offset, 8);
                var cipher = decrypt ? input.AsSpan(offset, 8) : output.AsSpan(offset, 8);
                report.AppendLine().AppendLine($"Block {offset / 8 + 1}");
                if (mode == CipherMode.CBC)
                {
                    for (var i = 0; i < 8; i++) mixed[i] = (byte)(plain[i] ^ chain[i]);
                    report.AppendLine($"Chain ({(offset == 0 ? "IV" : "previous ciphertext")}): {Convert.ToHexString(chain)}");
                }
                else plain.CopyTo(mixed);

                if (decrypt)
                {
                    report.AppendLine($"Ciphertext: {Convert.ToHexString(cipher)}");
                    report.AppendLine($"D_K1(E_K2(D_K3(C))) = {Convert.ToHexString(mixed)}");
                    if (mode == CipherMode.CBC) report.AppendLine($"3DES result XOR chain = {Convert.ToHexString(plain)}");
                    else report.AppendLine($"Plaintext block: {Convert.ToHexString(plain)}");
                }
                else
                {
                    report.AppendLine($"Plaintext block: {Convert.ToHexString(plain)}");
                    if (mode == CipherMode.CBC) report.AppendLine($"Plaintext XOR chain = {Convert.ToHexString(mixed)}");
                    report.AppendLine($"E_K3(D_K2(E_K1(X))) = {Convert.ToHexString(cipher)}");
                }
                if (mode == CipherMode.CBC) cipher.CopyTo(chain);
            }
            report.AppendLine().AppendLine($"Final {(decrypt ? "plaintext" : "ciphertext")}: {result}");
            return new(result, report.ToString().TrimEnd());
        }
        finally
        {
            foreach (var bytes in new[] { key, input, output, blocks, chain, mixed })
                if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
