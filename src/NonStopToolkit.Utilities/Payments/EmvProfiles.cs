using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NonStopToolkit.Utilities.Binary;

namespace NonStopToolkit.Utilities.Payments;

public static partial class EmvDecoder
{
    private static string[] Reserved() => Enumerable.Repeat("RFU", 8).ToArray();
    private static string[] CodeBits(string field) => Enumerable.Range(1, 8).Reverse().Select(b => $"{field} · coded bit {b}").ToArray();

    private static IEnumerable<EmvTagDefinition> AdditionalDefinitions()
    {
        yield return new("9F40", "Additional Terminal Capabilities", [
            ["Cash", "Goods", "Services", "Cashback", "Inquiry", "Transfer", "Payment", "Administrative"],
            ["Cash deposit", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU"],
            ["Numeric keys", "Alphabetic / special keys", "Command keys", "Function keys", "RFU", "RFU", "RFU", "RFU"],
            ["Print for attendant", "Print for cardholder", "Display for attendant", "Display for cardholder", "RFU", "RFU", "ISO 8859 code table 10", "ISO 8859 code table 9"],
            Enumerable.Range(1, 8).Reverse().Select(n => $"ISO 8859 code table {n}").ToArray()], Example: "FF80F05001");
        yield return new("9F07", "Application Usage Control (AUC)", [
            ["Domestic cash", "International cash", "Domestic goods", "International goods", "Domestic services", "International services", "Valid at ATMs", "Valid at terminals other than ATMs"],
            ["Domestic cashback", "International cashback", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU"]], Example: "FF00");
        yield return new("9F27", "Cryptogram Information Data (CID)", [
            ["Cryptogram type · bit 2", "Cryptogram type · bit 1", "RFU", "RFU", "Advice required", "Advice reason · bit 3", "Advice reason · bit 2", "Advice reason · bit 1"]], Example: "80");
        yield return new("9F34", "CVM Results", [
            ["RFU", "Continue to next CV rule on failure", "CVM code · bit 6", "CVM code · bit 5", "CVM code · bit 4", "CVM code · bit 3", "CVM code · bit 2", "CVM code · bit 1"],
            CodeBits("CVM condition"), CodeBits("CVM result")], Example: "1F0302");
        yield return new("8E", "CVM List", [], MinimumLength: 8, MaximumLength: 252, Example: "00000000000000001F00", BitEditor: false);
        yield return new("9F35", "Terminal Type", [CodeBits("Terminal type")], Example: "22");
        yield return new("9F66", "Terminal Transaction Qualifiers (TTQ)", [
            ["MSD supported", "RFU", "qVSDC supported", "Contact chip supported", "Offline-only reader", "Online PIN supported", "Signature supported", "Online ODA supported"],
            ["Online cryptogram required", "CVM required", "Contact offline PIN supported", "RFU", "RFU", "RFU", "RFU", "RFU"],
            ["Issuer update processing supported", "Consumer device CVM supported", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU"], Reserved()],
            "visa-k3", "Visa / Kernel 3", "EMV Contactless Book C-3 / Visa VCPS 2.2 TTQ", Example: "36004000");
        yield return new("9F6C", "Card Transaction Qualifiers (CTQ)", [
            ["Online PIN required", "Signature required", "Go online if ODA fails and reader can go online", "Switch interface if ODA fails and contact chip supported", "Go online if application expired and reader can go online", "Switch interface for cash if contact chip supported", "Switch interface for cashback if contact chip supported", "RFU"],
            ["Consumer device CVM performed", "Issuer update processing supported", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU"]],
            "visa-k3", "Visa / Kernel 3", "EMV Contactless Book C-3 / Visa VCPS 2.2 CTQ", Example: "2080");
        yield return new("9F6E", "Form Factor Indicator (FFI)", [
            ["FFI version · bit 3", "FFI version · bit 2", "FFI version · bit 1", "Form factor · bit 5", "Form factor · bit 4", "Form factor · bit 3", "Form factor · bit 2", "Form factor · bit 1"],
            ["Passcode capable", "Signature panel", "Hologram", "CVV2 present", "Two-way messaging", "Cloud-based payment credentials", "Biometric verification capable", "RFU"],
            Reserved(), ["RFU", "RFU", "RFU", "RFU", "Technology · bit 4", "Technology · bit 3", "Technology · bit 2", "Technology · bit 1"]],
            "visa-ffi-v1", "Visa FFI version 1 (VCPS 2.2)", "Visa VCPS 2.2, Form Factor Indicator version 001", Example: "23820000");
    }

    private static IEnumerable<EmvTagDefinition> SchemeDefinitions()
    {
        var ttq = Definitions.Single(d => d.Tag == "9F66");
        var discover = ttq.Bytes.Select(r => r.ToArray()).ToArray();
        discover[0][5] = "Online PIN supported";
        discover[0][7] = "RFU";
        discover[2][4] = "Consumer device CVM required";
        discover[3] = Enumerable.Repeat("Reserved (set to 1 in this profile)", 8).ToArray();
        yield return ttq with { Bytes = discover, ProfileId = "discover-k6-27", ProfileName = "Discover / Kernel 6 v2.7", Reference = "EMV Contactless Book C-6 v2.7, TTQ table", Example = "360048FF" };

        yield return new("9F6E", "Third Party Data", [], "mastercard-tpd", "Mastercard Third Party Data",
            "M/Chip Third Party Data structure; Visa Acceptance Solutions initiation-channel device codes", 5, 32, "02800000323100");
        yield return new("9F6E", "Enhanced Contactless Reader Capabilities", [
            ["Contact mode supported", "Contactless mag-stripe supported", "Legacy full-online mode (not supported)", "Contactless partial-online EMV supported", "Contactless mobile supported", "Try another interface after decline", "RFU", "RFU"],
            ["Mobile CVM supported", "Online PIN supported", "Signature supported", "Plaintext offline PIN supported", "RFU", "RFU", "RFU", "RFU"],
            ["Reader offline-only", "CVM required", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU"],
            ["Exempt from no-CVM checks", "Delayed authorisation terminal", "Transit terminal", "RFU", "RFU", "Kernel version · bit 3", "Kernel version · bit 2", "Kernel version · bit 1"]],
            "amex-k4-210", "American Express / Kernel 4 v2.10", "EMV Contactless Book C-4 v2.10, table 4-4 (2021; not the 2026 bulletin)", Example: "DCE00003");
        var aip = Definitions.Single(d => d.Tag == "82");
        var amexAip = aip.Bytes.Select(r => r.ToArray()).ToArray();
        amexAip[0][2] = "DDA (not supported in this profile)";
        amexAip[0][6] = "Reserved for contactless specifications";
        amexAip[1][0] = "EMV and mag-stripe modes supported (0 = mag-stripe only)";
        yield return aip with { Bytes = amexAip, ProfileId = "amex-k4-210", ProfileName = "American Express / Kernel 4 v2.10", Reference = "EMV Contactless Book C-4 v2.10, table 5-2", Example = "5880" };
        foreach (var tag in new[] { "9F34", "8E" })
            yield return Definitions.Single(d => d.Tag == tag) with
            {
                ProfileId = "amex-k4-210", ProfileName = "American Express mobile CVM / Kernel 4 v2.10",
                Reference = "EMV Contactless Book C-4 v2.10, mobile CVM processing (CVM code 01)"
            };
    }

    private static byte[] ReadValue(EmvTagDefinition definition, string value)
    {
        var bytes = HexBytes.Parse(value);
        var min = definition.MinimumLength > 0 ? definition.MinimumLength : definition.Bytes.Length;
        var max = definition.MaximumLength > 0 ? definition.MaximumLength : definition.Bytes.Length;
        if (bytes.Length < min || bytes.Length > max)
            throw new FormatException($"Tag {definition.Tag} ({definition.ProfileName}) requires {(min == max ? $"exactly {min}" : $"{min}–{max}")} bytes. Enter the value without tag/length headers.");
        if (definition.Tag == "8E" && (bytes.Length - 8) % 2 != 0) throw new FormatException("CVM List requires 8 amount bytes followed by complete 2-byte CV rules.");
        if (definition.ProfileId == "mastercard-tpd" && (bytes[2] & 0x80) == 0 && bytes.Length < 7)
            throw new FormatException("This Third Party Data identifier requires a 2-byte Device Type plus at least one proprietary-data byte (7 bytes total).");
        return bytes;
    }

    private static string[][] BitRows(EmvTagDefinition definition, byte[] bytes)
    {
        var rows = Enumerable.Range(0, bytes.Length).Select(i => i < definition.Bytes.Length ? definition.Bytes[i] : CodeBits("Raw data")).ToArray();
        if (definition.ProfileId == "mastercard-tpd")
            for (var i = 0; i < rows.Length; i++) rows[i] = CodeBits(i < 2 ? "Country code (BCD)" : i < 4 ? "Unique identifier" : (bytes[2] & 0x80) == 0 && i < 6 ? "Device type (ASCII)" : "Proprietary data");
        if (definition.ProfileId == "visa-ffi-v1" && bytes[0] >> 5 != 1)
            for (var i = 0; i < 3; i++) rows[i] = CodeBits("Unsupported FFI version; raw data");
        return rows;
    }

    public static IReadOnlyList<EmvDecodedField> DecodeFields(string tag, string value, string? profileId = null)
    {
        var definition = Definition(tag, profileId);
        var bytes = ReadValue(definition, value);
        var fields = new List<EmvDecodedField>();
        void Add(string name, string raw, string meaning) => fields.Add(new(name, raw, meaning));
        if (definition.ProfileId == "visa-ffi-v1")
        {
            var version = bytes[0] >> 5;
            Add("FFI version", version.ToString(CultureInfo.InvariantCulture), version == 1 ? "Version 1 (001)" : "Unsupported version; device-form and feature meanings are not inferred");
            if (version == 1)
            {
                var form = bytes[0] & 0x1F;
                Add("Form factor", form.ToString("X2"), form switch { 0 => "Standard card", 1 => "Mini-card", 2 => "Non-card form factor", 3 => "Consumer mobile phone", 4 => "Wrist-worn device", _ => "RFU / unsupported form-factor code" });
            }
            Add("Technology", (bytes[3] & 0x0F).ToString("X1"), (bytes[3] & 0x0F) == 0 ? "Proximity contactless (ISO 14443 / NFC)" : "RFU / unsupported technology code");
        }
        else if (definition.ProfileId == "mastercard-tpd")
        {
            var country = Convert.ToHexString(bytes.AsSpan(0, 2));
            Add("Country code", country, country[0] == '0' && country.All(c => c is >= '0' and <= '9') ? $"Numeric code {country[1..]} (scheme country-code table)" : "Not a valid n3 BCD country code");
            var uid = Convert.ToHexString(bytes.AsSpan(2, 2));
            Add("Unique identifier", uid, uid == "0000" ? "No third-party proprietary identifier" : "Scheme-assigned identifier");
            var hasDevice = (bytes[2] & 0x80) == 0;
            if (hasDevice)
            {
                var device = bytes.AsSpan(4, 2);
                var code = device[0] is >= 0x20 and <= 0x7E && device[1] is >= 0x20 and <= 0x7E ? Encoding.ASCII.GetString(device) : "non-ASCII";
                Add("Device type", $"{Convert.ToHexString(device)} ({code})", DeviceType(code));
            }
            else Add("Device type", "Not present", "Unique identifier bit 16 is set");
            Add("Proprietary data", Convert.ToHexString(bytes.AsSpan(hasDevice ? 6 : 4)), "Preserved as raw hex; issuer/third-party format is not inferred");
        }
        else if (definition.Tag == "9F27")
        {
            Add("Cryptogram type", (bytes[0] >> 6).ToString("X1"), (bytes[0] >> 6) switch { 0 => "AAC (decline)", 1 => "TC (offline approval)", 2 => "ARQC (online request)", _ => "RFU" });
            Add("Advice reason", (bytes[0] & 7).ToString("X1"), (bytes[0] & 7) switch { 0 => "No information", 1 => "Service not allowed", 2 => "PIN Try Limit exceeded", 3 => "Issuer authentication failed", _ => "RFU" });
        }
        else if (definition.Tag == "9F34")
        {
            Add("CVM", bytes[0].ToString("X2"), Cvm(bytes[0] & 0x3F, definition.ProfileId));
            Add("Condition", bytes[1].ToString("X2"), Condition(bytes[1]));
            Add("Result", bytes[2].ToString("X2"), bytes[2] switch { 0 => "Unknown", 1 => "Failed", 2 => "Successful", _ => "RFU" });
        }
        else if (definition.Tag == "8E")
        {
            Add("Amount X", BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4)).ToString(CultureInfo.InvariantCulture), "Threshold in application-currency units");
            Add("Amount Y", BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4)).ToString(CultureInfo.InvariantCulture), "Threshold in application-currency units");
            for (var i = 8; i < bytes.Length; i += 2)
                Add($"CV rule {(i - 8) / 2 + 1}", Convert.ToHexString(bytes.AsSpan(i, 2)), $"{Cvm(bytes[i] & 0x3F, definition.ProfileId)}; {Condition(bytes[i + 1])}; {((bytes[i] & 0x40) != 0 ? "continue to next rule on failure" : "fail verification if unsuccessful")}{((bytes[i] & 0x80) != 0 ? "; RFU bit 8 is set" : "")}");
        }
        else if (definition.Tag == "9F35")
        {
            Add("Terminal operator", (bytes[0] >> 4).ToString("X1"), (bytes[0] >> 4) switch { 1 => "Financial institution", 2 => "Merchant", 3 => "Cardholder", _ => "RFU" });
            Add("Environment", (bytes[0] & 0x0F).ToString("X1"), (bytes[0] & 0x0F) switch { 1 => "Attended, online-only", 2 => "Attended, offline with online capability", 3 => "Attended, offline-only", 4 => "Unattended, online-only", 5 => "Unattended, offline with online capability", 6 => "Unattended, offline-only", _ => "RFU" });
        }
        else if (definition.Tag == "9F6E" && definition.ProfileId == "amex-k4-210")
            Add("Kernel version code", (bytes[3] & 7).ToString("X1"), (bytes[3] & 7) switch { 1 => "C-4 2.2–2.3", 2 => "C-4 2.4–2.6", 3 => "C-4 2.7", _ => "RFU / unsupported version code" });
        return fields.AsReadOnly();
    }

    private static string Cvm(int code, string profile) => code switch
    {
        0 => "Fail CVM processing", 1 => profile == "amex-k4-210" ? "Mobile CVM (Kernel 4 contactless mobile)" : "Plaintext PIN verified by ICC",
        2 => "Enciphered PIN verified online", 3 => "Plaintext ICC PIN and signature", 4 => "Enciphered PIN verified by ICC",
        5 => "Enciphered ICC PIN and signature", 0x1E => "Signature", 0x1F => "No CVM required", 0x3F => "No CVM performed / unavailable",
        >= 0x20 and <= 0x2F => "Payment-system-specific CVM (raw code retained)", >= 0x30 and <= 0x3E => "Issuer-specific CVM (raw code retained)", _ => "RFU CVM code"
    };
    private static string Condition(int code) => code switch
    {
        0 => "Always", 1 => "Unattended cash", 2 => "Not unattended cash, manual cash or purchase with cashback", 3 => "Terminal supports this CVM",
        4 => "Manual cash", 5 => "Purchase with cashback", 6 => "Application currency and under X", 7 => "Application currency and over X",
        8 => "Application currency and under Y", 9 => "Application currency and over Y", >= 0x80 => "Payment-system-specific condition", _ => "RFU condition"
    };
    private static string DeviceType(string code) => code switch
    {
        "00" => "Card", "01" => "Mobile phone, operator-controlled removable secure element", "02" => "Key fob", "03" => "Watch",
        "04" => "Mobile tag", "05" => "Wristband", "06" => "Mobile phone case / sleeve", "07" => "Mobile phone, operator-controlled fixed secure element",
        "08" => "Mobile phone, removable secure element outside operator control", "09" => "Mobile phone, fixed secure element outside operator control",
        "10" => "Tablet / e-reader, operator-controlled removable secure element", "11" => "Tablet / e-reader, operator-controlled fixed secure element",
        "12" => "Tablet / e-reader, removable secure element outside operator control", "13" => "Tablet / e-reader, fixed secure element outside operator control",
        "14" => "Mobile phone, host-based payment application", "15" => "Tablet / e-reader, host-based payment application",
        "16" => "Mobile phone, trusted execution environment", "17" => "Tablet / e-reader, trusted execution environment",
        "18" => "Watch, trusted execution environment", "19" => "Watch, host-based payment application",
        "20" => "Card", "21" => "Mobile phone", "22" => "Tablet / e-reader", "23" => "Watch / wristband", "24" => "Sticker", "25" => "PC",
        "26" => "Device peripheral", "27" => "Electronic tag / key fob", "28" => "Jewelry", "29" => "Fashion accessory", "30" => "Garment",
        "31" => "Domestic appliance", "32" => "Vehicle / vehicle-mounted device", "33" => "Media / gaming device", _ => "Unknown / reserved device type (raw bytes retained)"
    };
}
