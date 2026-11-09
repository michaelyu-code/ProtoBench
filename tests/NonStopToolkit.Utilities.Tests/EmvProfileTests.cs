using NonStopToolkit.Utilities.Payments;

namespace NonStopToolkit.Utilities.Tests;

public sealed class EmvProfileTests
{
    [Fact]
    public void CatalogueHasUniqueTagsAndExplicitProfilePairs()
    {
        Assert.Equal(13, EmvDecoder.Definitions.Count);
        Assert.Equal(19, EmvDecoder.Profiles.Count);
        Assert.Equal(13, EmvDecoder.Definitions.Select(d => d.Tag).Distinct().Count());
        Assert.Equal(19, EmvDecoder.Profiles.Select(d => (d.Tag, d.ProfileId)).Distinct().Count());
        Assert.Equal(3, EmvDecoder.ProfilesFor("9f6e").Count);
        foreach (var profile in EmvDecoder.Profiles)
        {
            Assert.NotEmpty(profile.ProfileName);
            Assert.NotEmpty(profile.Reference);
            Assert.All(profile.Bytes, row => Assert.Equal(8, row.Length));
        }
    }

    [Theory]
    [InlineData("9F40", "0000800000", 3, 8, "Numeric keys")]
    [InlineData("9F40", "0000100000", 3, 5, "Function keys")]
    [InlineData("9F40", "0080000000", 2, 8, "Cash deposit")]
    [InlineData("9F40", "0000000200", 4, 2, "ISO 8859 code table 10")]
    [InlineData("9F40", "0000000001", 5, 1, "ISO 8859 code table 1")]
    [InlineData("9F07", "8000", 1, 8, "Domestic cash")]
    [InlineData("9F07", "0080", 2, 8, "Domestic cashback")]
    public void AdditionalTagsUseKnownBitPositions(string tag, string value, int byteNumber, int bit, string meaning)
    {
        var selected = Assert.Single(EmvDecoder.DecodeBits(tag, value), b => b.IsSet);
        Assert.Equal((byteNumber, bit, meaning), (selected.Byte, selected.Bit, selected.Meaning));
    }

    [Theory]
    [InlineData("20700000", "Standard card")]
    [InlineData("21000000", "Mini-card")]
    [InlineData("22000000", "Non-card form factor")]
    [InlineData("23820000", "Consumer mobile phone")]
    [InlineData("24000000", "Wrist-worn device")]
    public void VisaFfiVersionOneDecodesFormFactor(string value, string meaning)
    {
        var fields = EmvDecoder.DecodeFields("9F6E", value, "visa-ffi-v1");
        Assert.Contains(fields, f => f.Name == "FFI version" && f.Value == "1");
        Assert.Contains(fields, f => f.Name == "Form factor" && f.Meaning == meaning);
        Assert.Contains(fields, f => f.Name == "Technology" && f.Meaning.Contains("ISO 14443"));
    }

    [Fact]
    public void VisaFfiFeatureBitsAndUnknownVersionsAreNotConfused()
    {
        var features = EmvDecoder.DecodeBits("9F6E", "23820000", "visa-ffi-v1");
        Assert.Contains(features, b => b.Byte == 2 && b.Bit == 8 && b.IsSet && b.Meaning == "Passcode capable");
        Assert.Contains(features, b => b.Byte == 2 && b.Bit == 2 && b.IsSet && b.Meaning == "Biometric verification capable");
        var unknown = EmvDecoder.DecodeFields("9F6E", "03820000", "visa-ffi-v1");
        Assert.Contains(unknown, f => f.Name == "FFI version" && f.Meaning.Contains("Unsupported"));
        Assert.DoesNotContain(unknown, f => f.Name == "Form factor");
        Assert.DoesNotContain(EmvDecoder.DecodeBits("9F6E", "03820000", "visa-ffi-v1"), b => b.Meaning == "Passcode capable");
        Assert.Contains(EmvDecoder.DecodeFields("9F6E", "3F000002", "visa-ffi-v1"), f => f.Name == "Form factor" && f.Meaning.Contains("RFU"));
    }

    [Theory]
    [InlineData("02800000323100", "21", "Mobile phone")]
    [InlineData("02800000303000", "00", "Card")]
    [InlineData("02800000323300", "23", "Watch / wristband")]
    public void MastercardThirdPartyDataUsesAsciiDeviceCodes(string value, string code, string meaning)
    {
        var fields = EmvDecoder.DecodeFields("9F6E", value, "mastercard-tpd");
        Assert.Contains(fields, f => f.Name == "Country code" && f.Value == "0280");
        Assert.Contains(fields, f => f.Name == "Unique identifier" && f.Value == "0000");
        Assert.Contains(fields, f => f.Name == "Device type" && f.Value.EndsWith($"({code})") && f.Meaning == meaning);
        Assert.Contains(fields, f => f.Name == "Proprietary data" && f.Value == "00");
        Assert.Equal(value.Length * 4, EmvDecoder.DecodeBits("9F6E", value, "mastercard-tpd").Count);
    }

    [Fact]
    public void MastercardOptionalDeviceAndUnknownValuesStayRaw()
    {
        var noDevice = EmvDecoder.DecodeFields("9F6E", "02808000AB", "mastercard-tpd");
        Assert.Contains(noDevice, f => f.Name == "Device type" && f.Value == "Not present");
        Assert.Contains(noDevice, f => f.Name == "Proprietary data" && f.Value == "AB");
        var unknown = EmvDecoder.DecodeFields("9F6E", "0ABC00003939DEAD", "mastercard-tpd");
        Assert.Contains(unknown, f => f.Name == "Country code" && f.Value == "0ABC" && f.Meaning.Contains("Not a valid"));
        Assert.Contains(unknown, f => f.Name == "Device type" && f.Value == "3939 (99)" && f.Meaning.Contains("Unknown"));
        Assert.Contains(unknown, f => f.Name == "Proprietary data" && f.Value == "DEAD");
        Assert.Contains(EmvDecoder.DecodeFields("9F6E", "02800000FF0000", "mastercard-tpd"), f => f.Name == "Device type" && f.Value.Contains("non-ASCII"));
    }

    [Fact]
    public void SchemeVariantsChangeMeaningForTheSameTagAndBytes()
    {
        var visa = EmvDecoder.DecodeBits("9F66", "010008FF", "visa-k3");
        var discover = EmvDecoder.DecodeBits("9F66", "010008FF", "discover-k6-27");
        Assert.Contains(visa, b => b.Byte == 1 && b.Bit == 1 && b.Meaning == "Online ODA supported");
        Assert.Contains(discover, b => b.Byte == 1 && b.Bit == 1 && b.Meaning == "RFU");
        Assert.Contains(discover, b => b.Byte == 3 && b.Bit == 4 && b.Meaning == "Consumer device CVM required");
        Assert.Contains(EmvDecoder.DecodeBits("82", "0080", "amex-k4-210"), b => b.Byte == 2 && b.Bit == 8 && b.Meaning.StartsWith("EMV and mag-stripe"));
        Assert.Contains(EmvDecoder.DecodeBits("82", "0080"), b => b.Byte == 2 && b.Bit == 8 && b.Meaning.StartsWith("Reserved"));
        Assert.Contains(EmvDecoder.DecodeFields("9F6E", "DCE00003", "amex-k4-210"), f => f.Name == "Kernel version code" && f.Meaning == "C-4 2.7");
    }

    [Fact]
    public void CvmResultsAndAmexMobileCvmAreExplicitProfiles()
    {
        var common = EmvDecoder.DecodeFields("9F34", "010302");
        var mobile = EmvDecoder.DecodeFields("9F34", "010302", "amex-k4-210");
        Assert.Contains(common, f => f.Name == "CVM" && f.Meaning == "Plaintext PIN verified by ICC");
        Assert.Contains(mobile, f => f.Name == "CVM" && f.Meaning.StartsWith("Mobile CVM"));
        Assert.Contains(common, f => f.Name == "Condition" && f.Meaning == "Terminal supports this CVM");
        Assert.Contains(common, f => f.Name == "Result" && f.Meaning == "Successful");
    }

    [Fact]
    public void CvmListsDecodeBigEndianThresholdsAndOrderedRules()
    {
        var fields = EmvDecoder.DecodeFields("8E", "010203040000001041031F00");
        Assert.Contains(fields, f => f.Name == "Amount X" && f.Value == "16909060");
        Assert.Contains(fields, f => f.Name == "Amount Y" && f.Value == "16");
        Assert.Contains(fields, f => f.Name == "CV rule 1" && f.Value == "4103" && f.Meaning.Contains("continue to next"));
        Assert.Contains(fields, f => f.Name == "CV rule 2" && f.Meaning.Contains("No CVM required"));
        Assert.DoesNotContain("Byte 1", EmvDecoder.FormatBits("8E", "00000000000000001F00"));
        Assert.Throws<FormatException>(() => EmvDecoder.DecodeFields("8E", "00000000000000001F"));
    }

    [Theory]
    [InlineData("00", "AAC")]
    [InlineData("40", "TC")]
    [InlineData("80", "ARQC")]
    [InlineData("C0", "RFU")]
    public void CidDecodesTwoBitCryptogramType(string hex, string type)
        => Assert.Contains(EmvDecoder.DecodeFields("9F27", hex), f => f.Name == "Cryptogram type" && f.Meaning.StartsWith(type));

    [Fact]
    public void TerminalTypeDecodesOperatorAndEnvironment()
    {
        var fields = EmvDecoder.DecodeFields("9F35", "22");
        Assert.Contains(fields, f => f.Name == "Terminal operator" && f.Meaning == "Merchant");
        Assert.Contains(fields, f => f.Name == "Environment" && f.Meaning == "Attended, offline with online capability");
    }

    [Theory]
    [InlineData("9F6E", "23820000", "mastercard-tpd")]
    [InlineData("9F6E", "02800000323100", "visa-ffi-v1")]
    [InlineData("9F6E", "02800000323100", "amex-k4-210")]
    [InlineData("9F6E", "028000003231", "mastercard-tpd")]
    [InlineData("82", "0000", "visa-k3")]
    [InlineData("9F33", "E0F8C8", "unknown")]
    public void WrongSchemesAndLengthsAreRejected(string tag, string value, string profile)
        => Assert.Throws<FormatException>(() => EmvDecoder.DecodeBits(tag, value, profile));

    [Fact]
    public void EveryBitEditorProfileCanSetAndClearBitsWithoutChangingOtherBytes()
    {
        foreach (var definition in EmvDecoder.Profiles.Where(d => d.BitEditor))
        {
            var value = definition.Example.Length > 0 ? definition.Example : new string('0', definition.Bytes.Length * 2);
            foreach (var bit in EmvDecoder.DecodeBits(definition.Tag, value, definition.ProfileId))
            {
                var updated = EmvDecoder.SetBit(definition.Tag, value, bit.Byte, bit.Bit, !bit.IsSet, definition.ProfileId);
                Assert.Equal(value, EmvDecoder.SetBit(definition.Tag, updated, bit.Byte, bit.Bit, bit.IsSet, definition.ProfileId));
            }
        }
    }

    [Fact]
    public void TlvDoesNotGuessSchemesAndCanDecodeAnExplicitContext()
    {
        const string mastercard = "9F6E07028000003231009F660436004000";
        Assert.DoesNotContain("Mobile phone", EmvDecoder.FormatTlv(mastercard));
        Assert.Contains("choose an explicit", EmvDecoder.FormatTlv(mastercard));
        Assert.Contains("Mobile phone", EmvDecoder.FormatTlv(mastercard, "mastercard-tpd"));
        Assert.Contains("Consumer mobile phone", EmvDecoder.FormatTlv("9F6E04238200009F660436004000", "visa"));
        Assert.Contains("Consumer device CVM supported", EmvDecoder.FormatTlv("9F660436004000", "visa"));
        Assert.Contains("No CVM required", EmvDecoder.FormatTlv("8E0A00000000000000001F00"));
    }
}
