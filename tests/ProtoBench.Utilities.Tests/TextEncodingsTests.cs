using ProtoBench.Utilities.Text;

namespace ProtoBench.Utilities.Tests;

public sealed class TextEncodingsTests
{
    [Theory]
    [InlineData(28591, "café", "63 61 66 E9")]
    [InlineData(1252, "€", "80")]
    [InlineData(437, "é", "82")]
    [InlineData(65001, "é😀", "C3 A9 F0 9F 98 80")]
    [InlineData(1200, "é", "E9 00")]
    [InlineData(1201, "é", "00 E9")]
    [InlineData(20127, " A\r\n", "20 41 0D 0A")]
    public void EncodesExactBytesAndRoundTrips(int codePage, string text, string hex)
    {
        var encoding = TextEncodings.Options.Single(e => e.CodePage == codePage);
        Assert.Equal(hex, TextEncodings.ToHex(text, encoding));
        Assert.Equal(text, TextEncodings.FromHex(hex, encoding));
    }

    [Fact]
    public void Latin1AndWindows1252HaveDistinctMappings()
    {
        var latin1 = TextEncodings.Options.Single(e => e.CodePage == 28591);
        var windows = TextEncodings.Options.Single(e => e.CodePage == 1252);
        Assert.Equal("\u0080", TextEncodings.FromHex("80", latin1));
        Assert.Equal("€", TextEncodings.FromHex("80", windows));
        Assert.Throws<FormatException>(() => TextEncodings.ToHex("€", latin1));
        Assert.Throws<FormatException>(() => TextEncodings.ToHex("é", TextEncodings.Ascii));
        Assert.Throws<FormatException>(() => TextEncodings.FromHex("FF", TextEncodings.Ascii));
    }

    [Theory]
    [InlineData(65001, "C3")]
    [InlineData(65001, "FF")]
    [InlineData(1200, "41")]
    [InlineData(1201, "D8 00")]
    public void ConverterRejectsInvalidBytesButPreviewReplacesThem(int codePage, string hex)
    {
        var encoding = TextEncodings.Options.Single(e => e.CodePage == codePage);
        Assert.Throws<FormatException>(() => TextEncodings.FromHex(hex, encoding));
        Assert.NotEmpty(encoding.GetEncoding(strict: false).GetString(Convert.FromHexString(hex.Replace(" ", ""))));
    }

    [Fact]
    public void EnforcesEncodedByteLimit()
    {
        Assert.Throws<FormatException>(() => TextEncodings.ToHex(new string('é', 8193), TextEncodings.Utf8));
        Assert.Throws<FormatException>(() => TextEncodings.ToHex(new string('A', 16385), TextEncodings.Ascii));
        Assert.Equal(16384, TextEncodings.FromHex(TextEncodings.ToHex(new string('A', 16384), TextEncodings.Ascii), TextEncodings.Ascii).Length);
    }
}
