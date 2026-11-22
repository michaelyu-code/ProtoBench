using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Utilities.Binary;
using ProtoBench.Utilities.Numeric;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Calculators;

public enum CalculatorKind { Bitwise, Base64, TripleDes }

public sealed class CalculatorWorkspace : ToolWorkspace
{
    public CalculatorKind Kind { get; }
    public override string ToolId => Kind switch { CalculatorKind.Bitwise => "bitwise-calculator", CalculatorKind.Base64 => "base64-calculator", _ => "3des-calculator" };
    public override string Title => Kind switch { CalculatorKind.Bitwise => "Bitwise Calculator", CalculatorKind.Base64 => "Base64", _ => "3DES Calculator" };
    public IReadOnlyList<string> Operations => Kind switch
    {
        CalculatorKind.Bitwise => ["AND", "OR", "XOR", "NOT", "Shift left", "Shift right (logical)", "Rotate left", "Rotate right"],
        CalculatorKind.Base64 => ["UTF-8 text → Base64", "Base64 → UTF-8 text", "Hex bytes → Base64", "Base64 → Hex bytes"],
        _ => ["Encrypt", "Decrypt"]
    };
    public string Help => Kind switch
    {
        CalculatorKind.Bitwise => "Unsigned operands in binary, decimal or hex. Results show all three bases, padded to the selected width. NOT and left shifts are masked to that width; right shift fills with zeroes. Shift/rotation counts are decimal, from 0 to the selected width. Optional 0x/0b prefixes match the selected base.",
        CalculatorKind.Base64 => "Encode UTF-8 text or hex bytes; decode to strict UTF-8 or hex. Standard Base64 uses + / and requires padding. URL-safe uses - _ and emits no padding; decoding accepts padded or unpadded URL-safe input. Whitespace in encoded input is ignored; invalid alphabets, padding and pad bits are rejected. Maximum 16,384 data bytes.",
        _ => "3DES EDE with a 16-byte (K1/K2/K1) or 24-byte (K1/K2/K3) hex key. ECB uses no IV; CBC requires an explicit 8-byte hex IV. None requires complete 8-byte blocks. PKCS7 adds/removes padding; zero padding is retained on decryption because trailing zeroes may be data. KCV is the first 3 bytes of encrypting 8 zero bytes with the key in ECB, without padding; it is independent of the selected data mode and IV. Calculate KCV needs only the key. Inputs and keys stay local and are not saved. Input format selects Hex, strict ASCII or UTF-8; results, keys and IVs use hex. Pack input removes spaces, tabs and line breaks before decoding or encoding; otherwise text spacing is preserved. It does not change the displayed input. Text input is not automatically padded: choose PKCS7 or zero padding for incomplete blocks. Maximum 16,384 input bytes after encoding."
    };
    public string InputLabel => Kind switch
    {
        CalculatorKind.Bitwise => "Operand A",
        CalculatorKind.Base64 => Mode switch { 0 => "UTF-8 text", 2 => "Hex bytes", _ => "Base64 input" },
        _ => $"{(Mode == 0 ? "Plaintext" : "Ciphertext")} ({TripleDesInputFormatLabel})"
    };
    public IReadOnlyList<string> TripleDesInputFormats => ["Hex", "ASCII", "UTF-8"];
    public string TripleDesInputFormatLabel => InputFormat switch { 1 => "ASCII", 2 => "UTF-8", _ => "hex bytes" };
    public string OperandLabel => Mode >= 4 ? "Shift / rotation count (decimal)" : "Operand B";
    public bool ShowOperand => Kind == CalculatorKind.Bitwise && Mode != 3;
    public bool ShowIv => Kind == CalculatorKind.TripleDes && Cipher == 1;
    private string input = "", operand = "", key = "", iv = "", output = "", error = "", kcv = "";
    private int mode, radix = 16, width = 8, cipher, padding, revision, inputFormat;
    private bool urlSafe, verbose, includeKeyValues, showKey = true, packInput;
    private string calculationDetails = "";
    private string keyError = "", inputError = "", ivError = "";
    public string KeyError { get => keyError; private set => SetProperty(ref keyError, value); }
    public string InputError { get => inputError; private set => SetProperty(ref inputError, value); }
    public string IvError { get => ivError; private set => SetProperty(ref ivError, value); }
    public string Input { get => input; set => Change(ref input, value); }
    public string Operand { get => operand; set => Change(ref operand, value); }
    public string Key { get => key; set => Change(ref key, value); }
    public string Iv { get => iv; set => Change(ref iv, value); }
    public int Mode { get => mode; set => Change(ref mode, value); }
    public int Radix { get => radix; set => Change(ref radix, value); }
    public int Width { get => width; set => Change(ref width, value); }
    public int Cipher { get => cipher; set => Change(ref cipher, value); }
    public int Padding { get => padding; set => Change(ref padding, value); }
    public int InputFormat { get => inputFormat; set => Change(ref inputFormat, value); }
    public bool PackInput { get => packInput; set => Change(ref packInput, value); }
    public bool UrlSafe { get => urlSafe; set => Change(ref urlSafe, value); }
    public bool Verbose { get => verbose; set => Change(ref verbose, value); }
    public bool IncludeKeyValues { get => includeKeyValues; set => Change(ref includeKeyValues, value); }
    public bool ShowKey { get => showKey; set => SetProperty(ref showKey, value); }
    public string Output { get => output; private set { if (SetProperty(ref output, value)) OnPropertyChanged(nameof(DisplayOutput)); } }
    public string CalculationDetails { get => calculationDetails; private set { if (SetProperty(ref calculationDetails, value)) OnPropertyChanged(nameof(DisplayOutput)); } }
    public string DisplayOutput => CalculationDetails.Length == 0 ? Output : $"{Output}\n\nCalculation details\n{CalculationDetails}";
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Kcv { get => kcv; private set => SetProperty(ref kcv, value); }
    public IAsyncRelayCommand CalculateCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand CalculateKcvCommand { get; }

    private void Change<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (!SetProperty(ref field, value, property)) return;
        revision++;
        Output = Error = "";
        CalculationDetails = "";
        if (property == nameof(Key)) KeyError = "";
        if (property is nameof(Input) or nameof(Mode) or nameof(InputFormat) or nameof(PackInput)) InputError = "";
        if (property is nameof(Iv) or nameof(Cipher)) IvError = "";
        if (!IsBusy) Status = "Ready";
        if (property == nameof(Key)) Kcv = "";
        OnPropertyChanged(nameof(InputLabel));
        OnPropertyChanged(nameof(OperandLabel));
        OnPropertyChanged(nameof(ShowOperand));
        OnPropertyChanged(nameof(ShowIv));
    }

    public CalculatorWorkspace(CalculatorKind kind)
    {
        Kind = kind;
        input = kind switch { CalculatorKind.Bitwise => "F0", CalculatorKind.Base64 => "Hello", _ => "" };
        operand = kind == CalculatorKind.Bitwise ? "0F" : "";
        CalculateCommand = new AsyncRelayCommand(async () =>
        {
            await RunAsync(async token =>
            {
                var version = revision;
                var text = Input; var b = Operand; var secret = Key; var initialVector = Iv;
                var operation = Mode; var numberBase = Radix; var bitWidth = Width;
                var cipherMode = Cipher; var pad = Padding; var url = UrlSafe;
                var trace = Verbose; var includeKeys = IncludeKeyValues;
                var format = InputFormat; var pack = PackInput;
                var formatLabel = TripleDesInputFormatLabel;
                string details = "";
                Output = Error = "";
                CalculationDetails = "";
                if (Kind == CalculatorKind.TripleDes && !ValidateRequiredFields()) return;
                if (Kind == CalculatorKind.TripleDes)
                {
                    try { text = PrepareTripleDesInput(text, format, pack); }
                    catch (FormatException ex) { InputError = Error = ex.Message; return; }
                }
                try
                {
                    var result = await Task.Run(() =>
                    {
                        switch (Kind)
                        {
                            case CalculatorKind.Bitwise:
                                var bits = BitwiseCalculator.Calculate(text, b, numberBase, bitWidth, (BitwiseOperation)operation);
                                return $"Hex: {bits.Hex}\nDecimal: {bits.Decimal}\nBinary: {bits.Binary}";
                            case CalculatorKind.Base64:
                                return operation switch
                                {
                                    0 => Base64Converter.Encode(text, urlSafe: url), 1 => Base64Converter.Decode(text, urlSafe: url),
                                    2 => Base64Converter.Encode(text, true, url), 3 => Base64Converter.Decode(text, true, url),
                                    _ => throw new FormatException("Select a Base64 operation.")
                                };
                            default:
                                if (operation is not (0 or 1) || cipherMode is not (0 or 1)) throw new FormatException("Select an operation and cipher mode.");
                                var paddingMode = pad switch { 0 => PaddingMode.None, 1 => PaddingMode.PKCS7, 2 => PaddingMode.Zeros, _ => throw new FormatException("Select a padding mode.") };
                                if (trace)
                                {
                                    var calculation = TripleDesCalculator.TransformDetailed(text, secret, cipherMode == 1 ? initialVector : "", operation == 1,
                                        cipherMode == 0 ? CipherMode.ECB : CipherMode.CBC, paddingMode, includeKeys, token);
                                    details = $"Input format: {formatLabel}\nPack input (remove whitespace): {(pack ? "on" : "off")}\nInput bytes: {text}\n\n{calculation.Details}";
                                    return calculation.Output;
                                }
                                return TripleDesCalculator.Transform(text, secret, cipherMode == 1 ? initialVector : "", operation == 1,
                                    cipherMode == 0 ? CipherMode.ECB : CipherMode.CBC, paddingMode);
                        }
                    }, token);
                    var checkValue = Kind == CalculatorKind.TripleDes ? TripleDesCalculator.KeyCheckValue(secret) : "";
                    token.ThrowIfCancellationRequested();
                    if (revision == version) { Output = result; CalculationDetails = details; Kcv = checkValue; }
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    if (revision == version) Error = ex.Message;
                }
            });
            if (Error.Length > 0) Status = "Check inputs";
        });
        CalculateKcvCommand = new RelayCommand(() =>
        {
            if (Kind != CalculatorKind.TripleDes) return;
            Kcv = Error = "";
            if (!ValidateRequiredFields(keyOnly: true)) return;
            try
            {
                Kcv = TripleDesCalculator.KeyCheckValue(Key);
                Status = "Completed";
                if (Verbose)
                {
                    var calculation = TripleDesCalculator.TransformDetailed("0000000000000000", Key, "", false,
                        CipherMode.ECB, PaddingMode.None, IncludeKeyValues);
                    Output = Kcv;
                    CalculationDetails = $"KCV = first 3 bytes of {calculation.Output} = {Kcv}\n\n{calculation.Details}";
                }
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { Error = ex.Message; Status = "Check inputs"; }
        });
        ClearCommand = new RelayCommand(Clear);
    }

    private bool ValidateRequiredFields(bool keyOnly = false)
    {
        KeyError = string.IsNullOrWhiteSpace(Key) ? "Enter a 16- or 24-byte key in hex." : "";
        var missingInput = Input.Length == 0 || ((InputFormat == 0 || PackInput) && string.IsNullOrWhiteSpace(Input));
        InputError = !keyOnly && missingInput ? $"Enter {(Mode == 0 ? "plaintext" : "ciphertext")} in {TripleDesInputFormatLabel}." : "";
        IvError = !keyOnly && ShowIv && string.IsNullOrWhiteSpace(Iv) ? "Enter an 8-byte IV for CBC mode." : "";
        if (KeyError.Length + InputError.Length + IvError.Length == 0) return true;
        Error = "Fill in the required fields highlighted above.";
        Status = "Check inputs";
        return false;
    }

    private static string PrepareTripleDesInput(string text, int format, bool pack)
    {
        if (text.Length > HexBytes.MaxBytes * 4) throw new FormatException("Input is limited to 65,536 characters.");
        if (pack) text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        byte[]? bytes = null;
        try
        {
            if (format == 0) bytes = HexBytes.Parse(text);
            else
            {
                Encoding encoding = format switch
                {
                    1 => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
                    2 => new UTF8Encoding(false, true),
                    _ => throw new FormatException("Select an input format.")
                };
                if (encoding.GetByteCount(text) > HexBytes.MaxBytes)
                    throw new FormatException("Encoded input is limited to 16,384 bytes.");
                bytes = encoding.GetBytes(text);
            }
            return Convert.ToHexString(bytes);
        }
        catch (EncoderFallbackException)
        {
            throw new FormatException(format == 1
                ? "ASCII input only supports characters U+0000–U+007F. Select UTF-8 for other characters."
                : "UTF-8 input contains an invalid character sequence.");
        }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private void Clear() { Input = Operand = Key = Iv = ""; revision++; Output = Error = Kcv = CalculationDetails = KeyError = InputError = IvError = ""; Status = "Ready"; }
    public override void Dispose() { base.Dispose(); Clear(); }
}

public sealed class CalculatorWorkspaceFactory(CalculatorKind kind) : IToolWorkspaceFactory
{
    public string ToolId => kind switch { CalculatorKind.Bitwise => "bitwise-calculator", CalculatorKind.Base64 => "base64-calculator", _ => "3des-calculator" };
    public ToolWorkspace Create() => new CalculatorWorkspace(kind);
}
