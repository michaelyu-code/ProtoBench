using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Utilities.Payments;
using System.Security.Cryptography;

namespace ProtoBench.Desktop.Features.Payments;

public abstract class PaymentWorkspace : ToolWorkspace
{
    private string input = "", key = "", expected = "", output = "", error = "";
    private int mode, revision;
    public abstract IReadOnlyList<string> Modes { get; }
    public abstract string Help { get; }
    public abstract string InputLabel { get; }
    public virtual bool UsesKey => false;
    public string Input { get => input; set { if (SetProperty(ref input, value)) Invalidate(); } }
    public string Key { get => key; set { if (SetProperty(ref key, value)) Invalidate(); } }
    public string Expected { get => expected; set { if (SetProperty(ref expected, value)) Invalidate(); } }
    public virtual int Mode
    {
        get => mode;
        set
        {
            if (!SetProperty(ref mode, value)) return;
            Invalidate();
            OnPropertyChanged(nameof(Help));
            OnPropertyChanged(nameof(InputLabel));
        }
    }
    public string Output { get => output; protected set => SetProperty(ref output, value); }
    public string Error { get => error; protected set => SetProperty(ref error, value); }
    public IAsyncRelayCommand CalculateCommand { get; }
    public IRelayCommand ClearCommand { get; }
    protected PaymentWorkspace()
    {
        CalculateCommand = new AsyncRelayCommand(() => RunAsync(async token =>
        {
            var version = revision;
            var text = Input;
            var secret = Key;
            var expectedMac = Expected;
            var selected = Mode;
            Output = Error = "";
            try
            {
                var calculation = CaptureCalculation(selected, text, secret, expectedMac);
                var result = await Task.Run(calculation, token);
                token.ThrowIfCancellationRequested();
                if (version == revision) Output = result;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
            {
                if (version == revision) Error = ex is CryptographicException
                    ? "The key has a weak or semi-weak DES component, or the cryptographic provider rejected it. Supply a valid AC session key."
                    : ex.Message;
            }
        }));
        ClearCommand = new RelayCommand(Clear);
    }
    protected void Invalidate() { revision++; Output = Error = ""; OnInputsChanged(); }
    protected virtual void OnInputsChanged() { }
    private void Clear() { Input = Key = Expected = ""; Invalidate(); }
    protected abstract string Calculate(int selected, string text, string secret, string expectedMac);
    protected virtual Func<string> CaptureCalculation(int selected, string text, string secret, string expectedMac) => () => Calculate(selected, text, secret, expectedMac);
    public override void Dispose() { base.Dispose(); Clear(); }
}

public sealed class EmvWorkspace : PaymentWorkspace
{
    private IReadOnlyList<EmvBit> bits = [];
    private int profileIndex;
    private string fieldSummary = "";
    private static readonly string[] TlvProfiles = ["Common only (no scheme assumption)", "Visa / Kernel 3 + FFI v1", "Mastercard Third Party Data", "American Express / Kernel 4 v2.10", "Discover / Kernel 6 v2.7"];
    private static readonly string[] TlvProfileIds = ["", "visa", "mastercard-tpd", "amex-k4-210", "discover-k6-27"];
    public IReadOnlyList<EmvBit> Bits { get => bits; private set { bits = value; OnPropertyChanged(); } }
    public bool IsTagMode => Mode >= 0 && Mode < EmvDecoder.Definitions.Count;
    public EmvTagDefinition? SelectedDefinition => IsTagMode ? AvailableProfiles[Math.Clamp(profileIndex, 0, AvailableProfiles.Count - 1)] : null;
    public IReadOnlyList<EmvTagDefinition> AvailableProfiles => IsTagMode ? EmvDecoder.ProfilesFor(EmvDecoder.Definitions[Mode].Tag) : [];
    public IReadOnlyList<string> ProfileChoices => IsTagMode ? AvailableProfiles.Select(d => d.ProfileName).ToArray() : TlvProfiles;
    public string FieldSummary { get => fieldSummary; private set => SetProperty(ref fieldSummary, value); }
    public string ProfileCaption => SelectedDefinition is { } d ? $"{d.Name} · {d.ProfileName}\n{d.Reference}" : "TLV decoding uses only the selected scheme context; ambiguous tags otherwise remain raw.";
    public bool IsBitMode => SelectedDefinition?.BitEditor == true;
    public override int Mode
    {
        get => base.Mode;
        set
        {
            if (base.Mode == value) return;
            profileIndex = 0;
            base.Mode = value;
            OnPropertyChanged(nameof(ProfileChoices));
            OnPropertyChanged(nameof(ProfileIndex));
            OnPropertyChanged(nameof(ProfileCaption));
        }
    }
    public int ProfileIndex
    {
        get => profileIndex;
        set
        {
            if (value < 0 || value >= ProfileChoices.Count || !SetProperty(ref profileIndex, value)) return;
            Invalidate();
            OnPropertyChanged(nameof(InputLabel));
            OnPropertyChanged(nameof(Help));
            OnPropertyChanged(nameof(ProfileCaption));
        }
    }
    public void LoadExample()
    {
        if (SelectedDefinition is { } d)
            Input = d.Example.Length > 0 ? d.Example : d.Tag switch { "9F33" => "E0F8C8", "95" => "8000000000", "9B" => "E800", "82" => "5800", _ => new string('0', d.Bytes.Length * 2) };
        else Input = "77069F3303E0F8C8";
    }
    public void ResetBits()
    {
        if (SelectedDefinition is { BitEditor: true } d)
            Input = d.ProfileId == "mastercard-tpd" ? "0000800000" : new string('0', d.Bytes.Length * 2);
    }
    public void SetBit(int byteNumber, int bit, bool isSet)
    {
        if (IsBitMode && Bits.Count > 0)
            Input = EmvDecoder.SetBit(SelectedDefinition!.Tag, Input, byteNumber, bit, isSet, SelectedDefinition.ProfileId);
    }
    protected override void OnInputsChanged()
    {
        IReadOnlyList<EmvBit> decoded = [];
        FieldSummary = "";
        if (SelectedDefinition is { } definition && !string.IsNullOrWhiteSpace(Input))
        {
            try
            {
                if (definition.BitEditor) decoded = EmvDecoder.DecodeBits(definition.Tag, Input, definition.ProfileId);
                FieldSummary = string.Join("\n", EmvDecoder.DecodeFields(definition.Tag, Input, definition.ProfileId).Select(f => $"{f.Name}: {f.Value} — {f.Meaning}"));
                Output = EmvDecoder.FormatBits(definition.Tag, Input, definition.ProfileId);
            }
            catch (FormatException ex) { Error = ex.Message; }
        }
        Bits = decoded;
    }
    public override string ToolId => "emv-decoder";
    public override string Title => "EMV Tag Decoder";
    public override IReadOnlyList<string> Modes { get; } = EmvDecoder.Definitions.Select(d => d.Tag == "9F6E" ? "9F6E — Form factor / Third party / Reader capabilities" : $"{d.Tag} — {d.Name}").Append("BER-TLV / Field 55").ToArray();
    public override string InputLabel => IsTagMode ? $"{SelectedDefinition!.Tag} value (hex, without tag/length)" : "BER-TLV hex data";
    public override string Help => !IsTagMode
        ? "Decode nested BER-TLV objects with byte offsets and lengths. Choose a scheme context to interpret scheme-specific tags; Common only preserves ambiguous tags as raw bytes. Maximum 16 nesting levels and 2,048 objects."
        : "Choose a tag and one of its supported scheme/profile definitions. Common tags share EMV contact 4.3 rules; scheme variants are explicitly versioned where available. Changing profile preserves your hex input and validates its new length. Coded multi-bit fields have decoded summaries; their individual bits are raw code bits, not independent flags. Unknown/reserved codes retain their raw value. Use Load example for the selected profile. Byte toggles update hex live. CVM List is decoded as rules rather than byte tabs. Profile definitions are not inferred from input length or an AID.";
    protected override Func<string> CaptureCalculation(int selected, string text, string secret, string expectedMac)
    {
        var definition = SelectedDefinition;
        var context = TlvProfileIds[Math.Clamp(profileIndex, 0, TlvProfileIds.Length - 1)];
        return definition is not null ? () => EmvDecoder.FormatBits(definition.Tag, text, definition.ProfileId) : () => EmvDecoder.FormatTlv(text, context);
    }
    protected override string Calculate(int selected, string text, string secret, string expectedMac) => CaptureCalculation(selected, text, secret, expectedMac)();
}

public sealed class BitmapWorkspace : PaymentWorkspace
{
    public override string ToolId => "iso8583-bitmap";
    public override string Title => "ISO 8583 Bitmap";
    public override IReadOnlyList<string> Modes { get; } = ["Decode hex bitmap", "Encode field numbers", "Decode binary bitmap"];
    public override string InputLabel => Mode switch { 1 => "Field numbers", 2 => "Binary bitmap (64 or 128 bits)", _ => "Hex bitmap (8 or 16 bytes)" };
    public override string Help => "Primary/secondary bitmap profile (fields 2–128). Bit 1 indicates a secondary bitmap and is generated automatically. DE65 is treated as a data field; tertiary bitmaps are unsupported. Field meanings depend on your host specification. Hex example: 3020000000800000. Field list: 3, 4, 11, 41. Empty field list encodes an all-zero primary bitmap.";
    protected override string Calculate(int selected, string text, string secret, string expectedMac)
    {
        var result = selected switch
        {
            0 => Iso8583Bitmap.Decode(text), 1 => Iso8583Bitmap.Encode(text), 2 => Iso8583Bitmap.Decode(text, true),
            _ => throw new ArgumentException("Select a supported bitmap operation.")
        };
        return $"Hex: {result.Hex}\nBinary: {result.Binary}\nSecondary bitmap: {(result.HasSecondary ? "present (bit 1 set)" : "absent")}\nData fields ({result.Fields.Count}): {(result.Fields.Count == 0 ? "none" : string.Join(", ", result.Fields))}";
    }
}

public sealed class LuhnWorkspace : PaymentWorkspace
{
    public override string ToolId => "luhn";
    public override string Title => "Luhn Check Digit";
    public override IReadOnlyList<string> Modes { get; } = ["Validate number", "Calculate check digit"];
    public override string InputLabel => Mode == 1 ? "Number prefix (without check digit)" : "Complete number (including check digit)";
    public override string Help => "Validate 2–19 digits or calculate a check digit for a 1–18 digit prefix. ASCII digits, spaces and hyphens are accepted; leading zeroes are preserved. Example: prefix 7992739871 → check digit 3. A passing checksum does not establish that a card exists, is active, or can authorise a payment.";
    protected override string Calculate(int selected, string text, string secret, string expectedMac) => selected switch
    {
        0 => Luhn.Validate(text) ? "Valid Luhn checksum." : "Invalid Luhn checksum.",
        1 => $"Check digit: {Luhn.CheckDigit(text)}\nComplete number: {text.Replace(" ", "").Replace("-", "")}{Luhn.CheckDigit(text)}",
        _ => throw new ArgumentException("Select a supported Luhn operation.")
    };
}

public sealed class ArqcWorkspace : PaymentWorkspace
{
    public override string ToolId => "arqc";
    public override string Title => "ARQC Calculator";
    public override bool UsesKey => true;
    public override IReadOnlyList<string> Modes { get; } = ["ISO 9797-1 algorithm 3 · padding method 2 (80 00…)", "ISO 9797-1 algorithm 3 · padding method 1 (00…)"];
    public override string InputLabel => "Assembled transaction data (hex, unpadded)";
    public override string Help => "Calculate an 8-byte AC using a supplied 16-byte AC session key, retail MAC (DES CBC + final decrypt/encrypt) and zero IV. Method 2 always adds 80 then zeroes; method 1 adds zeroes only to a block boundary. Supply the exact data order required by your card/CVN profile, without TLV headers or padding. Issuer/ICC master-key derivation, AES and scheme-specific CVN assembly are not included. Inputs stay in this tab and are not saved; Clear inputs removes them from the view. .NET rejects weak/semi-weak DES key components.";
    protected override string Calculate(int selected, string text, string secret, string expectedMac)
    {
        if (selected is not (0 or 1)) throw new ArgumentException("Select a supported padding method.");
        var mac = ArqcCalculator.Calculate(secret, text, selected == 0 ? 2 : 1);
        return $"ARQC: {mac}" + (string.IsNullOrWhiteSpace(expectedMac) ? "" :
            ArqcCalculator.Verify(mac, expectedMac) ? "\nVerification: match." : "\nVerification: mismatch.");
    }
}
