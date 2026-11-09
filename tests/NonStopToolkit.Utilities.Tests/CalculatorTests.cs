using System.Security.Cryptography;
using NonStopToolkit.Utilities.Binary;
using NonStopToolkit.Utilities.Numeric;
using NonStopToolkit.Utilities.Text;

namespace NonStopToolkit.Utilities.Tests;

public sealed class CalculatorTests
{
    [Fact]
    public void TripleDesKeyCheckValueMatchesKnownTwoKeyVector()
    {
        Assert.Equal("08D7B4", TripleDesCalculator.KeyCheckValue("0123456789ABCDEFFEDCBA9876543210"));
        Assert.Equal("08D7B4", TripleDesCalculator.KeyCheckValue("0123456789ABCDEFFEDCBA98765432100123456789ABCDEF"));
        Assert.Equal("08D7B4", TripleDesCalculator.KeyCheckValue("0123456789ABCDEF FEDCBA9876543210"));
        // Flipping only DES parity bits leaves the effective key and KCV unchanged.
        Assert.Equal("08D7B4", TripleDesCalculator.KeyCheckValue("0022446688AACCEEFFDDBB9977553311"));
    }

    [Fact]
    public void TripleDesKeyCheckValueRejectsInvalidKeys()
    {
        Assert.Throws<FormatException>(() => TripleDesCalculator.KeyCheckValue(""));
        Assert.Throws<FormatException>(() => TripleDesCalculator.KeyCheckValue("00"));
        Assert.Throws<FormatException>(() => TripleDesCalculator.KeyCheckValue("not hex"));
        Assert.Throws<FormatException>(() => TripleDesCalculator.KeyCheckValue(new string('0', 32)));
    }

    [Theory]
    [InlineData(BitwiseOperation.And, "A5", "0F", "05")]
    [InlineData(BitwiseOperation.Or, "A5", "0F", "AF")]
    [InlineData(BitwiseOperation.Xor, "A5", "0F", "AA")]
    [InlineData(BitwiseOperation.Not, "A5", "ignored", "5A")]
    [InlineData(BitwiseOperation.ShiftLeft, "A5", "1", "4A")]
    [InlineData(BitwiseOperation.ShiftRight, "A5", "1", "52")]
    [InlineData(BitwiseOperation.RotateLeft, "A5", "1", "4B")]
    [InlineData(BitwiseOperation.RotateRight, "A5", "1", "D2")]
    [InlineData(BitwiseOperation.ShiftLeft, "FF", "8", "00")]
    [InlineData(BitwiseOperation.ShiftRight, "FF", "8", "00")]
    [InlineData(BitwiseOperation.RotateLeft, "A5", "8", "A5")]
    [InlineData(BitwiseOperation.RotateRight, "A5", "0", "A5")]
    public void BitwiseKnownResults(BitwiseOperation operation, string a, string b, string expected)
        => Assert.Equal(expected, BitwiseCalculator.Calculate(a, b, 16, 8, operation).Hex);

    [Fact]
    public void BitwiseHandlesAll64BitsAndDecimalMaximum()
    {
        var max = BitwiseCalculator.Calculate("18446744073709551615", "0", 10, 64, BitwiseOperation.Or);
        Assert.Equal(ulong.MaxValue, max.Value);
        Assert.Equal("FFFFFFFFFFFFFFFF", max.Hex);
        Assert.Equal(new string('1', 64), max.Binary);
        Assert.Equal("18446744073709551615", max.Decimal);
        Assert.Equal(0UL, BitwiseCalculator.Calculate(max.Hex, "64", 16, 64, BitwiseOperation.ShiftRight).Value);
        Assert.Equal(0UL, BitwiseCalculator.Calculate(max.Hex, "64", 16, 64, BitwiseOperation.ShiftLeft).Value);
        Assert.Equal(1UL, BitwiseCalculator.Calculate("8000000000000000", "1", 16, 64, BitwiseOperation.RotateLeft).Value);
    }

    [Theory]
    [InlineData(8, "FF", "11111111")]
    [InlineData(16, "FFFF", "1111111111111111")]
    [InlineData(32, "FFFFFFFF", "11111111111111111111111111111111")]
    public void BitwiseNotUsesChosenWidth(int width, string hex, string binary)
    {
        var result = BitwiseCalculator.Calculate("0", "", 10, width, BitwiseOperation.Not);
        Assert.Equal(hex, result.Hex);
        Assert.Equal(binary, result.Binary);
    }

    [Theory]
    [InlineData("0b1010", "0011", 2, "09")]
    [InlineData("0x0A", "03", 16, "09")]
    [InlineData("10", "3", 10, "09")]
    public void BitwiseBasesAndPrefixes(string a, string b, int radix, string expected)
        => Assert.Equal(expected, BitwiseCalculator.Calculate(a, b, radix, 8, BitwiseOperation.Xor).Hex);

    [Theory]
    [InlineData("100", "0", 16, BitwiseOperation.And)]
    [InlineData("18446744073709551616", "0", 10, BitwiseOperation.And)]
    [InlineData("-1", "0", 10, BitwiseOperation.Or)]
    [InlineData("1", "9", 10, BitwiseOperation.ShiftLeft)]
    [InlineData("1", "-1", 16, BitwiseOperation.ShiftRight)]
    [InlineData("1", "0x1", 16, BitwiseOperation.RotateLeft)]
    [InlineData("2", "1", 2, BitwiseOperation.And)]
    [InlineData("", "0", 16, BitwiseOperation.And)]
    public void BitwiseRejectsOverflowAndInvalidOperands(string a, string b, int radix, BitwiseOperation operation)
        => Assert.Throws<FormatException>(() => BitwiseCalculator.Calculate(a, b, radix, 8, operation));

    [Fact]
    public void BitwiseRejectsUnsupportedOptions()
    {
        Assert.Throws<ArgumentException>(() => BitwiseCalculator.Calculate("0", "0", 10, 7, BitwiseOperation.And));
        Assert.Throws<ArgumentException>(() => BitwiseCalculator.Calculate("0", "0", 8, 8, BitwiseOperation.And));
        Assert.Throws<ArgumentException>(() => BitwiseCalculator.Calculate("0", "0", 10, 8, (BitwiseOperation)99));
    }

    // RFC 4648 section 10 vectors.
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg==")]
    [InlineData("fooba", "Zm9vYmE=")]
    [InlineData("foobar", "Zm9vYmFy")]
    [InlineData("café", "Y2Fmw6k=")]
    public void Base64KnownVectors(string text, string encoded)
    {
        Assert.Equal(encoded, Base64Converter.Encode(text));
        Assert.Equal(text, Base64Converter.Decode(encoded));
    }

    [Fact]
    public void Base64HandlesBinaryUrlAlphabetAndWhitespace()
    {
        Assert.Equal("+/8=", Base64Converter.Encode("FBFF", true));
        Assert.Equal("-_8", Base64Converter.Encode("FBFF", true, true));
        Assert.Equal("FBFF", Base64Converter.Decode("-_8", true, true));
        Assert.Equal("FBFF", Base64Converter.Decode("-_8=", true, true));
        Assert.Equal("Hello", Base64Converter.Decode(" SGVs\nbG8=\r\n"));
        Assert.Equal("00FF", Base64Converter.Decode("AP8=", true));
        Assert.Throws<FormatException>(() => Base64Converter.Decode("AP8="));
    }

    [Theory]
    [InlineData("Zg")]
    [InlineData("Zh==")]
    [InlineData("Zm9=")]
    [InlineData("Zg===")]
    [InlineData("Z=g=")]
    [InlineData("-_8=")]
    [InlineData("Zm9v!")]
    public void Base64RejectsInvalidStandardEncoding(string input)
        => Assert.Throws<FormatException>(() => Base64Converter.Decode(input));

    [Fact]
    public void Base64ValidatesUrlAlphabetUnicodeAndLimits()
    {
        Assert.Throws<FormatException>(() => Base64Converter.Decode("+/8=", true, true));
        Assert.Throws<FormatException>(() => Base64Converter.Decode("Z", true, true));
        Assert.Throws<FormatException>(() => Base64Converter.Decode("Zh", true, true));
        Assert.Throws<FormatException>(() => Base64Converter.Encode("\uD800"));
        Assert.Throws<FormatException>(() => Base64Converter.Encode(new string('é', 8193)));
        Assert.Throws<FormatException>(() => Base64Converter.Decode(Convert.ToBase64String(new byte[16385]), true));
        Assert.Equal(new string('a', 16384), Base64Converter.Decode(Base64Converter.Encode(new string('a', 16384))));
    }

    // NIST SP 800-67 Appendix B, also published in PyCryptodome's test_DES3.py.
    [Theory]
    [InlineData("0123456789ABCDEF23456789ABCDEF01456789ABCDEF0123", "54686520717566636B2062726F776E20666F78206A756D70", "A826FD8CE53B855FCCE21C8112256FE668D5C05DD9B6B900")]
    [InlineData("9B397EBF81B1181E282F4BB8AD BADC6B", "21E81B7ADE88A259", "5C577D4D9B20C0F8")]
    public void TripleDesMatchesIndependentEcbVectors(string key, string plaintext, string ciphertext)
    {
        Assert.Equal(ciphertext, TripleDesCalculator.Transform(plaintext, key, "", false, CipherMode.ECB, PaddingMode.None));
        Assert.Equal(plaintext, TripleDesCalculator.Transform(ciphertext, key, "", true, CipherMode.ECB, PaddingMode.None));
    }

    [Fact]
    public void TripleDesCbcMatchesKnownBlockAndChaining()
    {
        const string key = "9B397EBF81B1181E282F4BB8ADBADC6B";
        // P1 XOR IV = published plaintext; P2 XOR C1 = published plaintext.
        const string plaintext = "20E81B7ADE88A2597DBF663745A862A1";
        const string ciphertext = "5C577D4D9B20C0F85C577D4D9B20C0F8";
        Assert.Equal(ciphertext, TripleDesCalculator.Transform(plaintext, key, "0100000000000000", false, CipherMode.CBC, PaddingMode.None));
        Assert.Equal(plaintext, TripleDesCalculator.Transform(ciphertext, key, "0100000000000000", true, CipherMode.CBC, PaddingMode.None));
    }

    [Theory]
    [InlineData(CipherMode.ECB, PaddingMode.PKCS7, "0102030405", "0102030405")]
    [InlineData(CipherMode.CBC, PaddingMode.PKCS7, "0000000000000000", "0000000000000000")]
    [InlineData(CipherMode.CBC, PaddingMode.Zeros, "010200", "0102000000000000")]
    public void TripleDesPaddingPreservesData(CipherMode mode, PaddingMode padding, string plaintext, string expected)
    {
        const string key = "0123456789ABCDEFFEDCBA9876543210";
        var iv = mode == CipherMode.CBC ? "0000000000000000" : "";
        var ciphertext = TripleDesCalculator.Transform(plaintext, key, iv, false, mode, padding);
        Assert.Equal(expected, TripleDesCalculator.Transform(ciphertext, key, iv, true, mode, padding));
        if (padding == PaddingMode.PKCS7 && plaintext.Length == 16) Assert.Equal(32, ciphertext.Length);
    }

    [Fact]
    public void TripleDesRejectsInvalidInputsAndPkcs7()
    {
        const string key = "0123456789ABCDEFFEDCBA9876543210";
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform("00", key, "", false, CipherMode.ECB, PaddingMode.None));
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform("0000000000000000", key, "", false, CipherMode.CBC, PaddingMode.None));
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform("0000000000000000", key, "0000000000000000", false, CipherMode.ECB, PaddingMode.None));
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform("0000000000000000", "00", "", false, CipherMode.ECB, PaddingMode.None));
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform("0000000000000000", new string('0', 32), "", false, CipherMode.ECB, PaddingMode.None));
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform("", key, "", false, CipherMode.ECB, PaddingMode.PKCS7));
        var badPaddingCiphertext = TripleDesCalculator.Transform("0000000000000000", key, "", false, CipherMode.ECB, PaddingMode.None);
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform(badPaddingCiphertext, key, "", true, CipherMode.ECB, PaddingMode.PKCS7));
        Assert.Throws<FormatException>(() => TripleDesCalculator.Transform(new string('0', 32768), key, "", false, CipherMode.ECB, PaddingMode.PKCS7));
    }
}
