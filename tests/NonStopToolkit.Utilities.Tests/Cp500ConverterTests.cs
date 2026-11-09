using NonStopToolkit.Utilities.Text;

namespace NonStopToolkit.Utilities.Tests;

public sealed class Cp500ConverterTests
{
    private readonly Cp500Converter converter = new();

    [Theory]
    [InlineData("Hello", "C8 85 93 93 96")]
    [InlineData("ABC xyz 0129", "C1 C2 C3 40 A7 A8 A9 40 F0 F1 F2 F9")]
    [InlineData("[]!^|", "4A 5A 4F 5F BB")]
    [InlineData("é£¤", "51 B1 9F")]
    [InlineData("\0\t\r\n", "00 05 0D 25")]
    [InlineData(" A ", "40 C1 40")]
    [InlineData("", "")]
    public void UsesExactCp500Mappings(string text, string hex)
    {
        Assert.Equal(hex, converter.ToHex(text));
        Assert.Equal(text, converter.ToText(hex));
    }

    [Fact]
    public void AcceptsContiguousMixedCaseAndWhitespaceHex()
        => Assert.Equal("Hello", converter.ToText(" c885\t93\r\n9396 "));

    [Fact]
    public void EveryByteMatchesIndependentPythonCodecFixture()
    {
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "samples", "cp500-codepoints.txt"))
            .Select(line => (char)Convert.ToInt32(line, 16)).ToArray();
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(new string(expected), converter.Decode(bytes));
        Assert.Equal(bytes, converter.Encode(new string(expected)));
    }

    [Theory]
    [InlineData("€")]
    [InlineData("😀")]
    [InlineData("漢")]
    [InlineData("\uD800")]
    public void RejectsUnrepresentableTextWithoutReplacement(string text)
        => Assert.Throws<FormatException>(() => converter.ToHex(text));

    [Theory]
    [InlineData("C")]
    [InlineData("C 1")]
    [InlineData("0xC1")]
    [InlineData("C1,C2")]
    [InlineData("ZZ")]
    public void RejectsMalformedHex(string hex)
        => Assert.Throws<FormatException>(() => converter.ToText(hex));

    [Fact]
    public void TranscodesAsciiBytesWithoutLosingData()
    {
        var ascii = new AsciiHexConverter();
        Assert.Equal("C1 C2 C3", converter.ToHex(ascii.ToAscii("41 42 43")));
        Assert.Equal("41 42 43", ascii.ToHex(converter.ToText("C1 C2 C3")));
        Assert.Throws<FormatException>(() => ascii.ToHex(converter.ToText("51"))); // é cannot be ASCII.
    }

    [Fact]
    public void EnforcesSizeLimits()
    {
        var text = new string('A', 16384);
        Assert.Equal(text, converter.ToText(converter.ToHex(text)));
        Assert.Throws<FormatException>(() => converter.Encode(text + "A"));
        Assert.Throws<FormatException>(() => converter.Decode(new byte[16385]));
        Assert.Throws<FormatException>(() => converter.ToText(new string('0', 32770)));
        Assert.Throws<FormatException>(() => converter.ToText(new string(' ', 65537)));
    }
}
