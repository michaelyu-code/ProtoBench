using NonStopToolkit.Utilities.Text;

namespace NonStopToolkit.Utilities.Tests;

public sealed class AsciiHexConverterTests
{
    private readonly AsciiHexConverter converter = new();

    [Theory]
    [InlineData("Hello", "48 65 6C 6C 6F")]
    [InlineData(" A \r\n", "20 41 20 0D 0A")]
    [InlineData("\0\t\u007F", "00 09 7F")]
    [InlineData("", "")]
    public void EncodesExactBytesAndPreservesWhitespace(string ascii, string expected)
        => Assert.Equal(expected, converter.ToHex(ascii));

    [Theory]
    [InlineData("48656c6C6f", "Hello")]
    [InlineData(" 48 65\t6C\r\n6C 6F ", "Hello")]
    [InlineData("2041200D0A", " A \r\n")]
    [InlineData("00097F", "\0\t\u007F")]
    [InlineData("", "")]
    [InlineData(" \r\n\t", "")]
    public void DecodesBytePairs(string hex, string expected)
        => Assert.Equal(expected, converter.ToAscii(hex));

    [Fact]
    public void RoundTripsEntireAsciiRange()
    {
        var ascii = new string(Enumerable.Range(0, 128).Select(i => (char)i).ToArray());
        Assert.Equal(ascii, converter.ToAscii(converter.ToHex(ascii)));
    }

    [Theory]
    [InlineData("café")]
    [InlineData("\u0080")]
    [InlineData("😀")]
    public void RejectsNonAsciiText(string ascii)
        => Assert.Throws<FormatException>(() => converter.ToHex(ascii));

    [Theory]
    [InlineData("80")]
    [InlineData("FF")]
    [InlineData("C3 A9")]
    [InlineData("4")]
    [InlineData("414")]
    [InlineData("4 1")]
    [InlineData("0x41")]
    [InlineData("41,42")]
    [InlineData("GG")]
    [InlineData("４１")]
    public void RejectsInvalidHexAndNonAsciiBytes(string hex)
        => Assert.Throws<FormatException>(() => converter.ToAscii(hex));

    [Fact]
    public void EnforcesLimitsInBothDirections()
    {
        var ascii = new string('A', AsciiHexConverter.MaxBytes);
        Assert.Equal(ascii, converter.ToAscii(converter.ToHex(ascii)));
        Assert.Throws<FormatException>(() => converter.ToHex(ascii + "A"));
        Assert.Throws<FormatException>(() => converter.ToAscii(new string('0', AsciiHexConverter.MaxBytes * 2 + 2)));
        Assert.Throws<FormatException>(() => converter.ToAscii(new string(' ', AsciiHexConverter.MaxBytes * 4 + 1)));
    }
}
