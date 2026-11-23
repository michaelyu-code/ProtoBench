using System.Text;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Parsing.Tests;

public sealed class Iso8583CompatibilityTests
{
    private static Iso8583Profile Profile(string type, int length, string attributes = "", string rootAttributes = "") => Iso8583Profile.FromXml($"""
        <isopackager {rootAttributes}>
          <isofield id="0" length="4" name="MTI" class="org.jpos.iso.IFA_NUMERIC"/>
          <isofield id="1" length="16" name="Bitmap" class="org.jpos.iso.IFB_BITMAP"/>
          <isofield id="3" length="{length}" name="Test field" class="org.jpos.iso.{type}" {attributes}/>
          <isofield id="4" length="2" name="Following field" class="org.jpos.iso.IF_CHAR"/>
        </isopackager>
        """, "test");

    private static Iso8583Result Parse(Iso8583Profile profile, string fieldHex, Iso8583EbcdicEncoding encoding = Iso8583EbcdicEncoding.Jpos1047)
        => Iso8583Parser.Parse(profile, Convert.FromHexString("303230303000000000000000" + fieldHex + "4F4B"), encoding);

    public static IEnumerable<object[]> Vectors()
    {
        yield return ["IFA_NUMERIC", 6, "303030303432", "000042"];
        yield return ["IF_CHAR", 6, "412042202020", "A B   "];
        yield return ["IFA_AMOUNT", 5, "4330303432", "C0042"];
        yield return ["IFB_NUMERIC", 5, "123450", "12345"];
        yield return ["IFB_FNUMERIC", 5, "12345F", "12345"];
        yield return ["IFB_HEX", 5, "ABCDEF", "ABCDE"];
        yield return ["IFB_AMOUNT", 6, "43123450", "C12345"];
        yield return ["IFB_BINARY", 3, "00FF40", "00FF40"];
        yield return ["IFA_BINARY", 3, "616230306366", "AB00CF"];
        yield return ["IFE_NUMERIC", 4, "F0F0F4F2", "0042"];
        yield return ["IFE_CHAR", 5, "C1C2C34040", "ABC  "];
        yield return ["IFE_AMOUNT", 5, "C3F0F0F4F2", "C0042"];
        yield return ["IFE_BINARY", 3, "81F0C2F0C3F0", "A0B0C0"];
        yield return ["IFAE_LLCHAR", 99, "3033C1C2C3", "ABC"];
        yield return ["IFEA_LLCHAR", 99, "F0F3414243", "ABC"];
        for (var n = 1; n <= 4; n++)
        {
            var ls = new string('L', n);
            // Decimal length 3, represented with n ASCII or EBCDIC digits.
            var asciiPrefix = Convert.ToHexString(Encoding.ASCII.GetBytes(3.ToString().PadLeft(n, '0')));
            var ebcdicPrefix = string.Concat(Enumerable.Repeat("F0", n - 1)) + "F3";
            var bcdPrefix = n <= 2 ? "03" : "0003";
            var max = (int)Math.Pow(10, n) - 1;
            yield return [$"IFA_{ls}CHAR", max, asciiPrefix + "412042", "A B"];
            yield return [$"IFA_{ls}BINARY", max, asciiPrefix + "00FF40", "00FF40"];
            yield return [$"IFE_{ls}CHAR", max, ebcdicPrefix + "C1C2C3", "ABC"];
            yield return [$"IFE_{ls}BINARY", max, ebcdicPrefix + "00FF40", "00FF40"];
            if (n >= 2)
            {
                yield return [$"IFB_{ls}CHAR", max, bcdPrefix + "412042", "A B"];
                yield return [$"IFB_{ls}BINARY", max, bcdPrefix + "00FF40", "00FF40"];
            }
            if (n is 2 or 3)
            {
                yield return [$"IFA_{ls}NUM", max, asciiPrefix + "313233", "123"];
                yield return [$"IFE_{ls}NUM", max, ebcdicPrefix + "F1F2F3", "123"];
                yield return [$"IFB_{ls}NUM", max, bcdPrefix + "1230", "123"];
                yield return [$"IFB_F{ls}NUM", max, bcdPrefix + "123F", "123"];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void FieldPackagersConsumeTheirExactWireLength(string type, int max, string hex, string expected)
    {
        var result = Parse(Profile(type, max), hex);
        Assert.True(result.IsComplete, string.Join("\n", result.Warnings));
        Assert.Equal(expected, result.Fields[2].Value);
        Assert.Equal(hex.ToUpperInvariant(), result.Fields[2].RawValue);
        Assert.Equal(12 + hex.Length / 2, result.Fields[3].Offset);
        Assert.Equal("OK", result.Fields[3].Value);
    }

    [Theory]
    [InlineData("IFB_NUMERIC", "012345", 5, "12345")]
    [InlineData("IFB_FNUMERIC", "012345", 5, "12345")]
    [InlineData("IFB_HEX", "0ABCDE", 5, "ABCDE")]
    [InlineData("IFB_AMOUNT", "43012345", 6, "C12345")]
    public void LeftPaddingIsAppliedToTheValueNotThePrefix(string type, string hex, int length, string expected)
    {
        var result = Parse(Profile(type, length, "pad=\"true\""), hex);
        Assert.True(result.IsComplete);
        Assert.Equal(expected, result.Fields[2].Value);
    }

    [Fact]
    public void TrackTwoDIsDecodedAsEquals()
    {
        var result = Parse(Profile("IFB_LLNUM", 19), "0512D450");
        Assert.True(result.IsComplete);
        Assert.Equal("12=45", result.Fields[2].Value);
    }

    [Fact]
    public void Cp500AndJpos1047AreDistinctAndDoNotRecodeBinaryFields()
    {
        var profile = Profile("IFE_CHAR", 5);
        Assert.Equal("[]^!|", Parse(profile, "4A5A5F4FBB", Iso8583EbcdicEncoding.Cp500).Fields[2].Value);
        Assert.Equal("[]^!|", Parse(profile, "ADBD5F5A4F").Fields[2].Value);
        Assert.NotEqual("[]^!|", Parse(profile, "4A5A5F4FBB").Fields[2].Value);
        Assert.Equal("4A5A5F4FBB", Parse(Profile("IFB_BINARY", 5), "4A5A5F4FBB", Iso8583EbcdicEncoding.Cp500).Fields[2].Value);
    }

    [Fact]
    public void Cp500MessageIncludesEbcdicMtiBitmapAndLengthPrefixes()
    {
        const string xml = """
            <isopackager emitBitmap="true" bitmapField="1" firstField="2" maxValidField="128">
              <isofield id="0" length="4" name="MTI" class="org.jpos.iso.IFE_NUMERIC"/>
              <isofield id="1" length="16" name="Bitmap" class="org.jpos.iso.IFE_BITMAP"/>
              <isofield id="2" length="19" name="Account" class="org.jpos.iso.IFE_LLNUM"/>
              <isofield id="3" length="5" name="Text" class="org.jpos.iso.IFE_CHAR" trim="true"/>
            </isopackager>
            """;
        var data = Convert.FromHexString("F0F2F0F0F6F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F5F1F2F3F4F54A5A5F4FBB");
        var result = Iso8583Parser.Parse(Iso8583Profile.FromXml(xml, "cp500"), data, Iso8583EbcdicEncoding.Cp500);
        Assert.True(result.IsComplete, string.Join("\n", result.Warnings));
        Assert.Equal("0200", result.Fields[0].Value);
        Assert.Equal("12345", result.Fields[2].Value);
        Assert.Equal("[]^!|", result.Fields[3].Value);
        Assert.Equal(22, result.Fields[2].ValueOffset);
        Assert.Equal("F0F5F1F2F3F4F5", result.Fields[2].RawValue);
        var partial = Iso8583Parser.Parse(Iso8583Profile.FromXml(xml, "cp500"), data[..^3], Iso8583EbcdicEncoding.Cp500);
        Assert.False(partial.IsComplete);
        Assert.Equal("[]", partial.Fields.Last().Value);
        Assert.Equal("4A5A", partial.Fields.Last().RawValue);
    }

    [Theory]
    [InlineData("IFB_LLLCHAR", "1000", 9999)]
    [InlineData("IFB_LLLCHAR", "A003", 999)]
    [InlineData("IFE_LLCHAR", "C1F3", 99)]
    public void MalformedPrefixesDoNotConsumeFollowingFields(string type, string hex, int max)
    {
        var result = Parse(Profile(type, max), hex);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Fields.Count);
        Assert.StartsWith(hex, result.RemainingHex);
    }

    [Fact]
    public void TertiaryBitmapUsesBit65AsAnIndicatorWhenXmlLengthIs24()
    {
        var profile = Iso8583Profile.FromXml("""
            <isopackager maxValidField="192">
              <isofield id="0" length="4" name="MTI" class="org.jpos.iso.IFA_NUMERIC"/>
              <isofield id="1" length="24" name="Bitmap" class="org.jpos.iso.IFB_BITMAP"/>
              <isofield id="130" length="2" name="Test" class="org.jpos.iso.IF_CHAR"/>
            </isopackager>
            """, "tertiary");
        var bytes = Encoding.ASCII.GetBytes("0800").Concat(Convert.FromHexString("800000000000000080000000000000004000000000000000")).Concat("OK"u8.ToArray()).ToArray();
        var result = Iso8583Parser.Parse(profile, bytes);
        Assert.True(result.IsComplete);
        Assert.Equal(130, result.Fields.Last().Number);
        Assert.Equal(28, result.Fields.Last().Offset);
    }
}
