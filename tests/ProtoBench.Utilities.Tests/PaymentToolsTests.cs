using ProtoBench.Utilities.Payments;

namespace ProtoBench.Utilities.Tests;

public sealed class PaymentToolsTests
{
    [Theory]
    [InlineData("9F33", "E0F8C8")]
    [InlineData("95", "8000000000")]
    [InlineData("9B", "E800")]
    [InlineData("82", "5800")]
    public void EditingEveryEmvBitPreservesOtherBitsAndRoundTrips(string tag, string value)
    {
        var original = EmvDecoder.DecodeBits(tag, value);
        foreach (var bit in original)
        {
            var changed = EmvDecoder.SetBit(tag, value, bit.Byte, bit.Bit, !bit.IsSet);
            var decoded = EmvDecoder.DecodeBits(tag, changed);
            Assert.Single(decoded.Where((b, i) => b.IsSet != original[i].IsSet));
            Assert.Contains(decoded, b => b.Byte == bit.Byte && b.Bit == bit.Bit && b.IsSet != bit.IsSet);
            Assert.Equal(value, EmvDecoder.SetBit(tag, changed, bit.Byte, bit.Bit, bit.IsSet));
            Assert.Equal(changed, EmvDecoder.SetBit(tag, changed, bit.Byte, bit.Bit, !bit.IsSet));
        }
    }

    [Fact]
    public void EditingEmvBitsUsesExpectedHexMasksAndValidatesPositions()
    {
        Assert.Equal("60F8C8", EmvDecoder.SetBit("9F33", "e0 f8 c8", 1, 8, false));
        Assert.Equal("000001", EmvDecoder.SetBit("9F33", "000000", 3, 1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => EmvDecoder.SetBit("82", "0000", 0, 1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => EmvDecoder.SetBit("82", "0000", 3, 1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => EmvDecoder.SetBit("82", "0000", 1, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => EmvDecoder.SetBit("82", "0000", 1, 9, true));
        Assert.Throws<FormatException>(() => EmvDecoder.SetBit("82", "00", 1, 1, true));
    }

    [Theory]
    [InlineData("79927398713", true)]
    [InlineData("79927398714", false)]
    [InlineData("4111 1111-1111 1111", true)]
    [InlineData("0000", true)]
    [InlineData("18", true)]
    [InlineData("19", false)]
    public void LuhnKnownChecksums(string input, bool valid) => Assert.Equal(valid, Luhn.Validate(input));

    [Theory]
    [InlineData("7992739871", 3)]
    [InlineData("411111111111111", 1)]
    [InlineData("0", 0)]
    [InlineData("0001", 8)]
    public void LuhnKnownCheckDigits(string input, int expected) => Assert.Equal(expected, Luhn.CheckDigit(input));

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("４１１１１１１１１１１１１１１１")]
    [InlineData("4111\t111111111111")]
    [InlineData("12345678901234567890")]
    public void LuhnRejectsInvalidNumbers(string input) => Assert.Throws<FormatException>(() => Luhn.Validate(input));

    [Fact]
    public void LuhnRejectsOverlongPrefix() => Assert.Throws<FormatException>(() => Luhn.CheckDigit("1234567890123456789"));

    [Fact]
    public void BitmapKnownPrimary()
    {
        var result = Iso8583Bitmap.Decode("30 20 00 00 00 80 00 00");
        Assert.Equal(new[] { 3, 4, 11, 41 }, result.Fields);
        Assert.False(result.HasSecondary);
        Assert.Equal("3020000000800000", Iso8583Bitmap.Encode("41,3,4,11,3").Hex);
    }

    [Fact]
    public void BitmapKnownSecondaryAndEdges()
    {
        const string hex = "C0000000000000018000000000000001";
        var result = Iso8583Bitmap.Decode(hex);
        Assert.Equal(new[] { 2, 64, 65, 128 }, result.Fields);
        Assert.True(result.HasSecondary);
        Assert.Equal(hex, Iso8583Bitmap.Encode("2 64 65 128").Hex);
        Assert.Equal(hex, Iso8583Bitmap.Decode(result.Binary, true).Hex);
        Assert.Equal("0000000000000000", Iso8583Bitmap.Encode("").Hex);
    }

    [Theory]
    [InlineData("8000000000000000")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("0123")]
    [InlineData("GG00000000000000")]
    public void BitmapRejectsInvalidLengthAndIndicator(string input) => Assert.Throws<FormatException>(() => Iso8583Bitmap.Decode(input));

    [Theory]
    [InlineData("1")]
    [InlineData("129")]
    [InlineData("-2")]
    [InlineData("+2")]
    [InlineData("2x")]
    public void BitmapRejectsInvalidFields(string input) => Assert.Throws<FormatException>(() => Iso8583Bitmap.Encode(input));

    [Fact]
    public void BitmapRejectsInvalidBinary() => Assert.Throws<FormatException>(() => Iso8583Bitmap.Decode(new string('2', 64), true));

    [Fact]
    public void TerminalCapabilitiesKnownBits()
    {
        var bits = EmvDecoder.DecodeBits("9f33", "E0F8C8");
        Assert.Equal(24, bits.Count);
        Assert.Equal(11, bits.Count(b => b.IsSet));
        Assert.Contains(bits, b => b.Byte == 1 && b.Bit == 6 && b.IsSet && b.Meaning == "IC with contacts");
        Assert.Contains(bits, b => b.Byte == 3 && b.Bit == 4 && b.IsSet && b.Meaning == "CDA");
        Assert.DoesNotContain(bits, b => b.IsSet && b.Meaning == "RFU");
    }

    [Theory]
    [InlineData("95", "8000000000", 1, 8, "Offline data authentication was not performed")]
    [InlineData("95", "0000040000", 3, 3, "Online PIN entered")]
    [InlineData("9B", "0400", 1, 3, "Script processing was performed")]
    [InlineData("82", "0100", 1, 1, "CDA supported")]
    public void EmvKnownBitPositions(string tag, string value, int byteNumber, int bit, string meaning)
    {
        var set = Assert.Single(EmvDecoder.DecodeBits(tag, value), b => b.IsSet);
        Assert.Equal((byteNumber, bit, meaning), (set.Byte, set.Bit, set.Meaning));
    }

    [Fact]
    public void EmvReportsReservedAndWrongLengths()
    {
        Assert.Contains("reserved bit is set", EmvDecoder.FormatBits("82", "8000"));
        Assert.Throws<FormatException>(() => EmvDecoder.DecodeBits("9F33", "E0F8"));
        Assert.Throws<FormatException>(() => EmvDecoder.DecodeBits("FFFF", "00"));
    }

    [Fact]
    public void TlvNestedOffsetsAndUnknownTags()
    {
        var template = Assert.Single(EmvDecoder.ParseTlv("770B9F3303E0F8C8DF0102ABCD"));
        Assert.Equal("77", template.Tag);
        Assert.Equal(0, template.Offset);
        Assert.Equal(2, template.Children.Count);
        Assert.Equal(2, template.Children[0].Offset);
        Assert.Equal("DF01", template.Children[1].Tag);
        Assert.Equal(8, template.Children[1].Offset);
        Assert.Equal("ABCD", template.Children[1].Value);
        Assert.Contains("IC with contacts", EmvDecoder.FormatTlv("77069F3303E0F8C8"));
    }

    [Fact]
    public void TlvLongFormAndDuplicateTags()
    {
        var nodes = EmvDecoder.ParseTlv("5A8180" + new string('0', 256) + "5A00");
        Assert.Equal(2, nodes.Count);
        Assert.Equal(256, nodes[0].Value.Length);
        Assert.Equal("", nodes[1].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("9F")]
    [InlineData("9F3303E0")]
    [InlineData("77800000")]
    [InlineData("77039F3300")]
    [InlineData("77039F3301FF")]
    [InlineData("5A82FF")]
    [InlineData("9F8000")]
    [InlineData("FF00")]
    public void TlvRejectsMalformedData(string input) => Assert.Throws<FormatException>(() => EmvDecoder.FormatTlv(input));

    [Fact]
    public void TlvRejectsResourceExhaustion()
    {
        Assert.Throws<FormatException>(() => EmvDecoder.ParseTlv(string.Concat(Enumerable.Repeat("5A00", 2049))));
        var nested = "5A00";
        for (var i = 0; i < 18; i++) nested = "77" + (nested.Length / 2).ToString("X2") + nested;
        Assert.Throws<FormatException>(() => EmvDecoder.ParseTlv(nested));
    }

    // Published PyEMV AC example: https://github.com/knovichikhin/pyemv/blob/master/pyemv/ac.py
    [Fact]
    public void ArqcMatchesPublishedSessionKeyVector()
    {
        const string key = "29B33180E567CE38EA4CBC9D753B0E61";
        var actual = ArqcCalculator.Calculate(key, "0123456789ABCDEF0123456789ABCDEF");
        Assert.Equal("FA624250B008B59A", actual);
        Assert.True(ArqcCalculator.Verify(actual, "fa 62 42 50 b0 08 b5 9a"));
        Assert.False(ArqcCalculator.Verify(actual, "0000000000000000"));
        Assert.Throws<FormatException>(() => ArqcCalculator.Verify(actual, "00"));
    }

    // ANSI X9.19 retail MAC known-answer example, method 1, ASCII "Now is the time for all " (24 bytes).
    [Fact]
    public void RetailMacMatchesPublishedAlignedMethod1Vector()
    {
        Assert.Equal("A1C72E74EA3FA9B6", ArqcCalculator.Calculate("0123456789ABCDEFFEDCBA9876543210",
            "4E6F77206973207468652074696D6520666F7220616C6C20", 1));
    }

    [Fact]
    public void ArqcPaddingMethod2AlwaysAddsBlockWhenAligned()
    {
        const string key = "29B33180E567CE38EA4CBC9D753B0E61";
        const string data = "0123456789ABCDEF";
        Assert.Equal(ArqcCalculator.Calculate(key, data, 2), ArqcCalculator.Calculate(key, data + "8000000000000000", 1));
        Assert.NotEqual(ArqcCalculator.Calculate(key, data, 1), ArqcCalculator.Calculate(key, data, 2));
    }

    [Theory]
    [InlineData("00", "0123456789ABCDEF")]
    [InlineData("29B33180E567CE38EA4CBC9D753B0E61", "")]
    [InlineData("29B33180E567CE38EA4CBC9D753B0E61", "F")]
    public void ArqcRejectsInvalidInput(string key, string data) => Assert.Throws<FormatException>(() => ArqcCalculator.Calculate(key, data));

    [Fact]
    public void ArqcRejectsWeakKeyAndInvalidPadding()
    {
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => ArqcCalculator.Calculate("01010101010101010101010101010101", "00"));
        Assert.Throws<ArgumentException>(() => ArqcCalculator.Calculate("29B33180E567CE38EA4CBC9D753B0E61", "00", 3));
    }
}
