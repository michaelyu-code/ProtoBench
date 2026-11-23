namespace ProtoBench.Parsing.Engines;

internal enum IsoPrefix { Ascii, Bcd, Ebcdic }
internal enum IsoValue { Text, Ebcdic, Bcd, PackedHex, Binary, AsciiHex, EbcdicHex, BcdAmount }
internal sealed record Iso8583Codec(int PrefixDigits, IsoPrefix Prefix, IsoValue Value, bool FPadding = false)
{
    public int PrefixBytes => Prefix == IsoPrefix.Bcd ? (PrefixDigits + 1) / 2 : PrefixDigits;
}

/// <summary>Explicit mappings verified against jPOS classes; names alone are not a wire specification.</summary>
internal static class Iso8583Codecs
{
    private static readonly IReadOnlyDictionary<string, Iso8583Codec> Codecs = Build();
    public static Iso8583Codec? Get(string name) => Codecs.GetValueOrDefault(name);

    private static Dictionary<string, Iso8583Codec> Build()
    {
        var result = new Dictionary<string, Iso8583Codec>(StringComparer.Ordinal);
        void Add(string names, int digits, IsoPrefix prefix, IsoValue value, bool f = false)
        {
            foreach (var name in names.Split(' ')) result.Add("org.jpos.iso." + name, new(digits, prefix, value, f));
        }
        Add("IF_CHAR IFA_NUMERIC IFA_AMOUNT", 0, IsoPrefix.Ascii, IsoValue.Text);
        Add("IFE_CHAR IFE_NUMERIC IFE_AMOUNT", 0, IsoPrefix.Ebcdic, IsoValue.Ebcdic);
        Add("IFB_NUMERIC", 0, IsoPrefix.Bcd, IsoValue.Bcd);
        Add("IFB_FNUMERIC", 0, IsoPrefix.Bcd, IsoValue.Bcd, true);
        Add("IFB_HEX", 0, IsoPrefix.Bcd, IsoValue.PackedHex, true);
        Add("IFB_AMOUNT", 0, IsoPrefix.Bcd, IsoValue.BcdAmount);
        Add("IFB_BINARY", 0, IsoPrefix.Ascii, IsoValue.Binary);
        Add("IFA_BINARY", 0, IsoPrefix.Ascii, IsoValue.AsciiHex);
        Add("IFE_BINARY", 0, IsoPrefix.Ebcdic, IsoValue.EbcdicHex);
        for (var digits = 1; digits <= 4; digits++)
        {
            var ls = new string('L', digits);
            Add($"IFA_{ls}CHAR", digits, IsoPrefix.Ascii, IsoValue.Text);
            Add($"IFA_{ls}BINARY", digits, IsoPrefix.Ascii, IsoValue.Binary);
            Add($"IFE_{ls}CHAR", digits, IsoPrefix.Ebcdic, IsoValue.Ebcdic);
            Add($"IFE_{ls}BINARY", digits, IsoPrefix.Ebcdic, IsoValue.Binary);
            if (digits is 2 or 3)
            {
                Add($"IFA_{ls}NUM", digits, IsoPrefix.Ascii, IsoValue.Text);
                Add($"IFE_{ls}NUM", digits, IsoPrefix.Ebcdic, IsoValue.Ebcdic);
                Add($"IFB_{ls}NUM", digits, IsoPrefix.Bcd, IsoValue.Bcd);
                Add($"IFB_F{ls}NUM", digits, IsoPrefix.Bcd, IsoValue.Bcd, true);
            }
            if (digits >= 2)
            {
                Add($"IFB_{ls}CHAR", digits, IsoPrefix.Bcd, IsoValue.Text);
                Add($"IFB_{ls}BINARY", digits, IsoPrefix.Bcd, IsoValue.Binary);
            }
        }
        Add("IFAE_LLCHAR", 2, IsoPrefix.Ascii, IsoValue.Ebcdic);
        Add("IFEA_LLCHAR", 2, IsoPrefix.Ebcdic, IsoValue.Text);
        return result;
    }
}
