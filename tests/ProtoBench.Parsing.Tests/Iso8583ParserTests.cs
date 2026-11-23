using System.Text;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Parsing.Tests;

public sealed class Iso8583ParserTests
{
    private const string Xml = """
        <!DOCTYPE isopackager SYSTEM "http://invalid.example/never-fetch.dtd">
        <isopackager>
          <isofield id="0" length="4" name="MTI" class="org.jpos.iso.IFA_NUMERIC"/>
          <isofield id="1" length="16" name="Bitmap" class="org.jpos.iso.IFA_BITMAP"/>
          <isofield id="2" length="19" name="Account" class="org.jpos.iso.IFA_LLNUM"/>
          <isofield id="3" length="6" name="Processing code" class="org.jpos.iso.IFA_NUMERIC"/>
          <isofield id="70" length="3" name="Network code" class="org.jpos.iso.IFA_NUMERIC"/>
        </isopackager>
        """;
    private static Iso8583Result Parse(string text, string xml = Xml) => Iso8583Parser.Parse(Iso8583Profile.FromXml(xml, "test"), Encoding.ASCII.GetBytes(text));

    [Fact]
    public void ParsesAsciiVariableFieldsAndRawPrefix()
    {
        var result = Parse("020060000000000000000512345000000");
        Assert.True(result.IsComplete);
        Assert.Equal("12345", result.Fields[2].Value);
        Assert.Equal("Account", result.Fields[2].Description);
        Assert.Equal(Convert.ToHexString("0512345"u8), result.Fields[2].RawValue);
        Assert.Equal(20, result.Fields[2].Offset);
        Assert.Equal("000000", result.Fields[3].Value);
    }

    [Theory]
    [InlineData("0200600000000000000005123", "123")]
    [InlineData("020060000000000000000", "")]
    public void RetainsPartialFieldAndEarlierResults(string message, string partial)
    {
        var result = Parse(message);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, w => w.Contains("Incomplete"));
        Assert.Equal("0200", result.Fields[0].Value);
        Assert.Equal(partial, result.Fields.Last().Value);
        Assert.False(result.Fields.Last().Complete);
    }

    [Fact]
    public void ReadsSecondaryBitmap()
    {
        var result = Parse("080080000000000000000400000000000000301");
        Assert.True(result.IsComplete);
        Assert.Equal(70, result.Fields.Last().Number);
        Assert.Equal("301", result.Fields.Last().Value);
    }

    [Fact]
    public void BcdUsesDigitLengthsAndPadding()
    {
        var xml = Xml.Replace("IFA_NUMERIC", "IFB_NUMERIC").Replace("IFA_BITMAP", "IFB_BITMAP").Replace("IFA_LLNUM", "IFB_LLNUM");
        var result = Iso8583Parser.Parse(Iso8583Profile.FromXml(xml, "bcd"), Convert.FromHexString("0200600000000000000005123450000000"));
        Assert.True(result.IsComplete);
        Assert.Equal("12345", result.Fields[2].Value);
        Assert.Equal("05123450", result.Fields[2].RawValue);
        Assert.Equal("000000", result.Fields[3].Value);
    }

    [Fact]
    public void UnknownPackagerDoesNotGuessFieldOffsets()
    {
        var result = Parse("020060000000000000000512345000000", Xml.Replace("org.jpos.iso.IFA_LLNUM", "custom.JavaPackager"));
        Assert.Equal(2, result.Fields.Count);
        Assert.Contains("unsupported", Assert.Single(result.Warnings));
        Assert.NotEmpty(result.RemainingHex);
    }

    [Fact]
    public void InvalidLengthAndTrailingBytesAreWarnings()
    {
        Assert.Contains(Parse("0200400000000000000020").Warnings, w => w.Contains("length"));
        Assert.Contains(Parse("02000000000000000000extra").Warnings, w => w.Contains("trailing"));
        Assert.Contains(Parse("0200000").Warnings, w => w.Contains("bitmap is truncated"));
        Assert.Contains(Parse("0200XXXXXXXXXXXXXXXX").Warnings, w => w.Contains("Invalid"));
    }

    [Theory]
    [InlineData("<isopackager/>")]
    [InlineData("<other/>")]
    public void InvalidProfilesAreRejected(string xml) => Assert.Throws<ArgumentException>(() => Iso8583Profile.FromXml(xml, "invalid"));

    [Fact]
    public void HeaderAndUnknownFieldAreRetained()
    {
        var result = Parse("AB02001000000000000000xyz", Xml.Replace("<isopackager>", "<isopackager headerLength=\"2\">"));
        Assert.Equal("AB", result.Fields[0].Value);
        Assert.Contains(result.Warnings, w => w.Contains("no XML definition"));
        Assert.Equal("78797A", result.RemainingHex);
    }

    [Theory]
    [InlineData("org.jpos.iso.IFA_LLLBINARY", "303033DEADBE")]
    [InlineData("org.jpos.iso.IFB_LLLBINARY", "0003DEADBE")]
    public void VariableBinaryUsesByteLengthAndLiteralBytes(string type, string encoded)
    {
        var xml = Xml.Replace("</isopackager>", $"<isofield id=\"55\" length=\"999\" name=\"ICC\" class=\"{type}\"/></isopackager>");
        var data = Encoding.ASCII.GetBytes("02000000000000000200").Concat(Convert.FromHexString(encoded)).ToArray();
        var result = Iso8583Parser.Parse(Iso8583Profile.FromXml(xml, "test"), data);
        Assert.True(result.IsComplete);
        Assert.Equal("DEADBE", result.Fields.Last().Value);
        Assert.Equal(encoded, result.Fields.Last().RawValue);
    }

    [Theory]
    [InlineData(false, "1230")]
    [InlineData(true, "0123")]
    public void OddBcdFixedFieldHonorsPad(bool pad, string encoded)
    {
        var xml = Xml.Replace("length=\"6\"", "length=\"3\"").Replace("name=\"Processing code\" class=\"org.jpos.iso.IFA_NUMERIC\"", $"name=\"Processing code\" pad=\"{pad.ToString().ToLowerInvariant()}\" class=\"org.jpos.iso.IFB_NUMERIC\"");
        var result = Iso8583Parser.Parse(Iso8583Profile.FromXml(xml, "test"), Encoding.ASCII.GetBytes("02002000000000000000").Concat(Convert.FromHexString(encoded)).ToArray());
        Assert.True(result.IsComplete);
        Assert.Equal("123", result.Fields.Last().Value);
    }

    [Fact]
    public void PrimaryOnlyXmlDoesNotConsumeSecondaryBytes()
    {
        var result = Parse("080080000000000000000400000000000000301", Xml.Replace("length=\"16\"", "length=\"8\""));
        Assert.Contains(result.Warnings, w => w.Contains("primary only"));
        Assert.Equal(2, result.Fields.Count);
        Assert.NotEmpty(result.RemainingHex);
    }
}
