using System.Security.Cryptography;

namespace ProtoBench.Utilities.Binary;

public static class TripleDesCalculator
{
    /// <summary>Encrypt-zero KCV: first three bytes of EDE(key, 0000000000000000), ECB without padding.</summary>
    public static string KeyCheckValue(string keyHex) =>
        Transform("0000000000000000", keyHex, "", false, CipherMode.ECB, PaddingMode.None)[..6];

    public static string Transform(string inputHex, string keyHex, string ivHex, bool decrypt, CipherMode mode, PaddingMode padding)
    {
        if (mode is not (CipherMode.ECB or CipherMode.CBC)) throw new ArgumentException("Select ECB or CBC mode.");
        if (padding is not (PaddingMode.None or PaddingMode.PKCS7 or PaddingMode.Zeros)) throw new ArgumentException("Select None, PKCS7 or zero padding.");
        byte[]? key = null, expandedKey = null, iv = null, input = null, output = null;
        try
        {
            key = HexBytes.Parse(keyHex);
            if (key.Length is not (16 or 24)) throw new FormatException("The 3DES key must contain 16 or 24 bytes (32 or 48 hex digits).");
            iv = HexBytes.Parse(ivHex);
            if (mode == CipherMode.CBC && iv.Length != 8) throw new FormatException("CBC requires an explicit 8-byte IV (16 hex digits).");
            if (mode == CipherMode.ECB && iv.Length != 0) throw new FormatException("ECB does not use an IV. Leave the IV empty.");
            input = HexBytes.Parse(inputHex);
            if (input.Length == 0) throw new FormatException("Enter at least one byte of hex data.");
            if (!decrypt && padding == PaddingMode.PKCS7 && input.Length == HexBytes.MaxBytes)
                throw new FormatException("PKCS7 plaintext is limited to 16,383 bytes so the padded ciphertext fits the 16,384-byte input limit.");
            if ((decrypt || padding == PaddingMode.None) && input.Length % 8 != 0)
                throw new FormatException("Ciphertext and unpadded input must contain complete 8-byte blocks.");
            using var cipher = TripleDES.Create();
            if (TripleDES.IsWeakKey(key)) throw new FormatException("The supplied key is a weak 3DES key. Choose a key with distinct adjacent DES components.");
            // Windows one-shot providers require the explicit 24-byte EDE representation.
            if (key.Length == 16)
            {
                expandedKey = new byte[24];
                key.CopyTo(expandedKey, 0);
                key.AsSpan(0, 8).CopyTo(expandedKey.AsSpan(16));
            }
            cipher.Key = expandedKey ?? key;
            output = (mode, decrypt) switch
            {
                (CipherMode.CBC, true) => cipher.DecryptCbc(input, iv, padding),
                (CipherMode.CBC, false) => cipher.EncryptCbc(input, iv, padding),
                (_, true) => cipher.DecryptEcb(input, padding),
                _ => cipher.EncryptEcb(input, padding)
            };
            return Convert.ToHexString(output);
        }
        catch (CryptographicException ex)
        {
            throw new FormatException("3DES failed. Check the key, cipher mode, IV and padding; PKCS7 decryption also validates padding bytes.", ex);
        }
        finally
        {
            foreach (var bytes in new[] { key, expandedKey, iv, input, output })
                if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
