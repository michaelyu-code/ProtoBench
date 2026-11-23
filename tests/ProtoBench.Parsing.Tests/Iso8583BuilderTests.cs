using System.Text;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Parsing.Tests;

public sealed class Iso8583BuilderTests
{
    private const string Xml = """
        <isopackager>
          <isofield id="0" length="4" name="MTI" class="org.jpos.iso.IFA_NUMERIC"/>
          <isofield id="1" length="16" name="Bitmap" class="org.jpos.iso.IFA_BITMAP"/>
          <isofield id="2" length="19" name="Account" class="org.jpos.iso.IFA_LLNUM"/>
          <isofield id="3" length="6" name="Code" class="org.jpos.iso.IFA_NUMERIC"/>
          <isofield id="41" length="8" name="Terminal" class="org.jpos.iso.IF_CHAR"/>
          <isofield id="55" length="999" name="ICC" class="org.jpos.iso.IFB_LLLBINARY"/>
          <isofield id="70" length="3" name="Network" class="org.jpos.iso.IFA_NUMERIC"/>
        </isopackager>
        """;
    private static Iso8583Profile Profile(string xml = Xml) => Iso8583Profile.FromXml(xml, "builder");

    [Fact]
    public void BuildsKnownAsciiMessageWithAutomaticBitmapPrefixAndFieldOrdering()
    {
        var result = Iso8583Builder.Build(Profile(), [new(3, "42"), new(0, "0200"), new(2, "12345")]);
        Assert.Equal("020060000000000000000512345000042", result.AsciiPreview);
        Assert.Equal(Convert.ToHexString(Encoding.ASCII.GetBytes(result.AsciiPreview)), result.Hex);
        Assert.True(result.Parsed.IsComplete);
        Assert.Equal("000042", result.Parsed.Fields.Last().Value);
        Assert.Equal("0512345", Encoding.ASCII.GetString(Convert.FromHexString(result.Parsed.Fields[2].RawValue)));
    }

    [Fact]
    public void MixesAsciiAndEncodedHexAndDoesNotAddPrefixesTwice()
    {
        var result = Iso8583Builder.Build(Profile(), [new(0, "30 32 30 30", Iso8583ValueInput.Hex), new(2, "31 32 33", Iso8583ValueInput.Hex), new(41, "TERM")]);
        Assert.Equal("0200400000000080000003123TERM    ", result.AsciiPreview);
        Assert.Equal("03", Encoding.ASCII.GetString(Convert.FromHexString(result.Parsed.Fields[2].RawValue)[..2]));
    }

    [Fact]
    public void BinaryHexStaysLiteralAndAsciiPreviewEscapesControlBytes()
    {
        var result = Iso8583Builder.Build(Profile(), [new(0, "0200"), new(55, "00 ff 40", Iso8583ValueInput.Hex)]);
        Assert.EndsWith("000300FF40", result.Hex);
        Assert.EndsWith("\\x00\\x03\\x00\\xFF@", result.AsciiPreview);
        Assert.False(result.IsPrintableAscii);
        Assert.Equal("00FF40", result.Parsed.Fields.Last().Value);
    }

    [Theory]
    [InlineData(false, "05123450")]
    [InlineData(true, "05012345")]
    public void BuildsBcdFromTextOrEncodedHexWithExplicitDigitCount(bool pad, string encoded)
    {
        var profile = Profile(Xml.Replace("IFA_NUMERIC", "IFB_NUMERIC").Replace("IFA_BITMAP", "IFB_BITMAP")
            .Replace("class=\"org.jpos.iso.IFA_LLNUM\"", $"class=\"org.jpos.iso.IFB_LLNUM\" pad=\"{pad.ToString().ToLowerInvariant()}\""));
        var fromText = Iso8583Builder.Build(profile, [new(0, "0200"), new(2, "12345")]);
        var fromHex = Iso8583Builder.Build(profile, [new(0, "0200", Iso8583ValueInput.Hex), new(2, encoded[2..], Iso8583ValueInput.Hex, 5)]);
        Assert.Equal("02004000000000000000" + encoded, fromText.Hex);
        Assert.Equal(fromText.Hex, fromHex.Hex);
        Assert.Equal("12345", fromHex.Parsed.Fields.Last().Value);
    }

    [Fact]
    public void Cp500TextUsesEbcdicFieldEncodingWhileBinaryRemainsUntouched()
    {
        var profile = Profile(Xml.Replace("IFA_", "IFE_").Replace("IF_CHAR", "IFE_CHAR"));
        var result = Iso8583Builder.Build(profile, [new(0, "0200"), new(2, "12345"), new(41, "TERM[]!"), new(55, "00FF", Iso8583ValueInput.Hex)], Iso8583EbcdicEncoding.Cp500);
        Assert.StartsWith("F0F2F0F0", result.Hex);
        Assert.Contains("F0F5F1F2F3F4F5", result.Hex);
        Assert.Contains("E3C5D9D44A5A4F40", result.Hex);
        Assert.EndsWith("000200FF", result.Hex);
        Assert.Equal("TERM[]! ", result.Parsed.Fields.Single(f => f.Number == 41).Value);
    }

    [Fact]
    public void SecondaryBitmapIsGeneratedAndPrimaryOnlyProfilesRejectHighFields()
    {
        var result = Iso8583Builder.Build(Profile(), [new(0, "0800"), new(70, "301")]);
        Assert.Equal("080080000000000000000400000000000000301", result.AsciiPreview);
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(Xml.Replace("length=\"16\"", "length=\"8\"")), [new(0, "0800"), new(70, "301")]));
    }

    [Fact]
    public void HeaderLengthIsExplicitAndNoFramingIsInvented()
    {
        var profile = Profile(Xml.Replace("<isopackager>", "<isopackager headerLength=\"2\">"));
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(profile, [new(0, "0200")]));
        var result = Iso8583Builder.Build(profile, [new(0, "0200")], headerHex: "AB CD");
        Assert.StartsWith("ABCD30323030", result.Hex);
        Assert.Equal(2, result.Parsed.Fields[1].Offset);
    }

    [Theory]
    [InlineData("IFA_LCHAR", 9, "33313233")]
    [InlineData("IFA_LLLLCHAR", 9999, "30303033313233")]
    [InlineData("IFB_LLLLCHAR", 9999, "0003313233")]
    [InlineData("IFE_LLCHAR", 99, "F0F3F1F2F3")]
    [InlineData("IFAE_LLCHAR", 99, "3033F1F2F3")]
    [InlineData("IFEA_LLCHAR", 99, "F0F3313233")]
    [InlineData("IFB_FLLNUM", 99, "03123F")]
    public void PrefixStrategiesPackLengthInTheCorrectEncoding(string type, int maximum, string expected)
    {
        var xml = Xml.Replace("length=\"19\" name=\"Account\" class=\"org.jpos.iso.IFA_LLNUM\"", $"length=\"{maximum}\" name=\"Account\" class=\"org.jpos.iso.{type}\"");
        var result = Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(2, "123")]);
        Assert.Equal(expected, result.Parsed.Fields.Last().RawValue);
    }

    [Theory]
    [InlineData("IFA_AMOUNT", 6, "C42", "433030303432")]
    [InlineData("IFE_AMOUNT", 6, "C42", "C3F0F0F0F4F2")]
    [InlineData("IFB_AMOUNT", 6, "C42", "43000420")]
    [InlineData("IFB_HEX", 5, "ABCDE", "ABCDEF")]
    [InlineData("IFA_BINARY", 3, "ABC", "343134323433")]
    [InlineData("IFE_BINARY", 3, "ABC", "F4F1F4F2F4F3")]
    public void AmountPackedHexAndEncodedBinaryHaveKnownWireValues(string type, int maximum, string value, string expected)
    {
        var xml = Xml.Replace("length=\"6\" name=\"Code\" class=\"org.jpos.iso.IFA_NUMERIC\"", $"length=\"{maximum}\" name=\"Code\" class=\"org.jpos.iso.{type}\"");
        var result = Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(3, value)]);
        Assert.Equal(expected, result.Parsed.Fields.Last().RawValue);
    }

    [Theory]
    [InlineData(3, "toolong", Iso8583ValueInput.Text)]
    [InlineData(0, "2", Iso8583ValueInput.Text)]
    [InlineData(0, "ZZZZ", Iso8583ValueInput.Text)]
    [InlineData(3, "ABCDEF", Iso8583ValueInput.Hex)]
    [InlineData(41, "0", Iso8583ValueInput.Hex)]
    [InlineData(41, "00", Iso8583ValueInput.Hex)]
    [InlineData(41, "£", Iso8583ValueInput.Text)]
    public void InvalidValuesFailBeforeProvidingAMessage(int number, string value, Iso8583ValueInput input)
    {
        var fields = new List<Iso8583BuildField> { new(number, value, input) };
        if (number != 0) fields.Add(new(0, "0200"));
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(), fields));
    }

    [Fact]
    public void RejectsDuplicateUndefinedAndManuallySuppliedBitmapFields()
    {
        foreach (var fields in new Iso8583BuildField[][]
        {
            [new(2, "123")], [new(0, "0200"), new(0, "0800")],
            [new(0, "0200"), new(1, "00")], [new(0, "0200"), new(20, "123")]
        }) Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(), fields));
    }

    [Fact]
    public void CancellationStopsBuilding()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => Iso8583Builder.Build(Profile(), [new(0, "0200")], cancellationToken: cancel.Token));
    }

    [Fact]
    public void TertiaryBitmapIsGeneratedFromHighFields()
    {
        var xml = Xml.Replace("<isopackager>", "<isopackager maxValidField=\"192\">")
            .Replace("length=\"16\" name=\"Bitmap\"", "length=\"24\" name=\"Bitmap\"")
            .Replace("</isopackager>", "<isofield id=\"130\" length=\"2\" name=\"High field\" class=\"org.jpos.iso.IF_CHAR\"/></isopackager>");
        var result = Iso8583Builder.Build(Profile(xml), [new(0, "0800"), new(130, "OK")]);
        Assert.Equal("0800800000000000000080000000000000004000000000000000OK", result.AsciiPreview);
        Assert.Equal(130, result.Parsed.Fields.Last().Number);
    }

    [Fact]
    public void EmptyVariableBinaryValuesAndExactFixedBinaryValuesAreSupported()
    {
        var empty = Iso8583Builder.Build(Profile(), [new(0, "0200"), new(55, "", Iso8583ValueInput.Hex)]);
        Assert.EndsWith("0000", empty.Hex);
        Assert.Equal("", empty.Parsed.Fields.Last().Value);
        var xml = Xml.Replace("length=\"999\" name=\"ICC\" class=\"org.jpos.iso.IFB_LLLBINARY\"", "length=\"3\" name=\"ICC\" class=\"org.jpos.iso.IFB_BINARY\"");
        var result = Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(55, "00FF40", Iso8583ValueInput.Hex)]);
        Assert.EndsWith("00FF40", result.Hex);
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(55, "00FF", Iso8583ValueInput.Hex)]));
    }

    [Fact]
    public void DigitCountCannotDiscardWholeBytesAndFNibbleNeedsExplicitLength()
    {
        var xml = Xml.Replace("IFA_LLNUM", "IFB_FLLNUM");
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(2, "12345F", Iso8583ValueInput.Hex)]));
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(2, "12345F", Iso8583ValueInput.Hex, 3)]));
        Assert.Equal("12345", Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(2, "12345F", Iso8583ValueInput.Hex, 5)]).Parsed.Fields.Last().Value);
    }

    [Fact]
    public void RejectsOversizedVariablesAndUnsupportedClasses()
    {
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(), [new(0, "0200"), new(2, new string('1', 20))]));
        var xml = Xml.Replace("IFA_LLNUM", "CUSTOM_JAVA");
        Assert.Throws<FormatException>(() => Iso8583Builder.Build(Profile(xml), [new(0, "0200"), new(2, "123")]));
    }
}
