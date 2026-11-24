using System.Text;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Utilities.Tests;

public sealed class TextCompareFileTests
{
    [Fact]
    public void BomEncodingAndMixedEndingsRoundTripWithoutConversion()
    {
        const string text = "a\r\nb\nc\rd🙂";
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true) })
        {
            var bytes = TextCompareFile.Encode(text, encoding);
            var decoded = TextCompareFile.Decode(bytes);
            Assert.Equal(text, decoded.Text);
            Assert.Equal("\r\n", decoded.NewLine);
            Assert.True(decoded.MixedLineEndings);
            Assert.Equal(bytes, TextCompareFile.Encode(decoded.Text, decoded.Encoding));
        }
    }

    [Fact]
    public void MalformedUtf8IsRejectedAndExplicitLatin1IsSupported()
    {
        Assert.Throws<DecoderFallbackException>(() => TextCompareFile.Decode([0xff]));
        var strictLatin1 = Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        Assert.Equal("ÿ", TextCompareFile.Decode([0xff], strictLatin1).Text);
        Assert.Throws<EncoderFallbackException>(() => TextCompareFile.Encode("🙂", strictLatin1));
    }

    [Fact]
    public void LineEndingDifferencesAreNotSilentlyIgnored()
    {
        var document = TextCompareDocument.Create("same\r\n", "same\n", new());
        Assert.Single(document.Changes);
    }
}
