using System.Security.Cryptography;
using ProtoBench.Utilities.Binary;

namespace ProtoBench.Utilities.Payments;

/// <summary>Supplied-session-key AC generation: ISO/IEC 9797-1 algorithm 3, zero IV, full 8-byte MAC.</summary>
public static class ArqcCalculator
{
    public static string Calculate(string sessionKeyHex, string transactionDataHex, int paddingMethod = 2)
    {
        var key = HexBytes.Parse(sessionKeyHex);
        byte[]? data = null;
        byte[]? padded = null;
        byte[]? left = null;
        byte[]? right = null;
        try
        {
            if (key.Length != 16) throw new FormatException("The AC session key must contain exactly 16 bytes (32 hex digits).");
            if (paddingMethod is not (1 or 2)) throw new ArgumentException("Choose ISO 9797-1 padding method 1 or 2.");
            data = HexBytes.Parse(transactionDataHex);
            if (data.Length == 0) throw new FormatException("Enter the transaction data in the order required by your card profile.");
            var length = paddingMethod == 2 ? (data.Length / 8 + 1) * 8 : (data.Length + 7) / 8 * 8;
            padded = new byte[length];
            data.CopyTo(padded, 0);
            if (paddingMethod == 2) padded[data.Length] = 0x80;
            left = key[..8];
            right = key[8..];
            using var desA = DES.Create();
            using var desB = DES.Create();
            // .NET rejects weak/semi-weak DES components; propagate this as a visible validation error.
            desA.Key = left;
            desB.Key = right;
            var encrypted = desA.EncryptCbc(padded, new byte[8], PaddingMode.None);
            try
            {
                var intermediate = desB.DecryptEcb(encrypted.AsSpan(encrypted.Length - 8), PaddingMode.None);
                try { return Convert.ToHexString(desA.EncryptEcb(intermediate, PaddingMode.None)); }
                finally { CryptographicOperations.ZeroMemory(intermediate); }
            }
            finally { CryptographicOperations.ZeroMemory(encrypted); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (data is not null) CryptographicOperations.ZeroMemory(data);
            if (padded is not null) CryptographicOperations.ZeroMemory(padded);
            if (left is not null) CryptographicOperations.ZeroMemory(left);
            if (right is not null) CryptographicOperations.ZeroMemory(right);
        }
    }

    public static bool Verify(string calculatedHex, string expectedHex)
    {
        var actual = HexBytes.Parse(calculatedHex);
        var expected = HexBytes.Parse(expectedHex);
        if (actual.Length != 8 || expected.Length != 8) throw new FormatException("ARQC values must contain exactly 8 bytes.");
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
