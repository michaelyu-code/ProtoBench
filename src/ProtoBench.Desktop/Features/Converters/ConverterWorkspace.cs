using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Utilities.Numeric;
using ProtoBench.Utilities.Text;
namespace ProtoBench.Desktop.Features.Converters;

public enum ConversionMode
{
    Numeric, AsciiToHex, HexToAscii, TextToCp500, Cp500ToText, AsciiHexToCp500, Cp500ToAsciiHex
}

public sealed class ConverterWorkspace : ToolWorkspace
{
    private readonly IBaseConverter converter;
    public override string ToolId => "base-converter";
    public override string Title => "Base Converter";
    private string input = "255", output = "", error = "";
    private int fromBase = 10, toBase = 16;
    private ConversionMode mode;
    private bool removeSpaces;
    private int revision;
    public bool RemoveSpaces
    {
        get => removeSpaces;
        set
        {
            if (SetProperty(ref removeSpaces, value))
                InvalidateResult();
        }
    }
    public ConversionMode Mode
    {
        get => mode;
        set
        {
            if (SetProperty(ref mode, value))
                InvalidateResult();
        }
    }
    private void InvalidateResult()
    {
        revision++;
        Output = "";
        Error = "";
    }
    public string Input
    {
        get => input; set
        {
            SetProperty(ref input, value);
            InvalidateResult();
        }
    }
    public string Output
    {
        get => output; private set => SetProperty(ref output, value);
    }
    public string Error
    {
        get => error; private set => SetProperty(ref error, value);
    }
    public int FromBase
    {
        get => fromBase; set
        {
            SetProperty(ref fromBase, value);
            InvalidateResult();
        }
    }
    public int ToBase
    {
        get => toBase; set
        {
            SetProperty(ref toBase, value);
            InvalidateResult();
        }
    }
    public IAsyncRelayCommand ConvertCommand
    {
        get;
    }
    public ConverterWorkspace(IBaseConverter converter, IAsciiHexConverter asciiConverter, ICp500Converter cp500Converter)
    {
        this.converter = converter;
        ConvertCommand = new AsyncRelayCommand(() => RunAsync(async token =>
        {
            Error = "";
            Output = "";
            var inputRevision = revision;
            try
            {
                var text = RemoveSpaces ? Input.Replace(" ", "", StringComparison.Ordinal) : Input;
                var from = FromBase;
                var to = ToBase;
                var selectedMode = Mode;
                var converted = await Task.Run(() => selectedMode switch
                {
                    ConversionMode.Numeric => converter.Convert(text, from, to),
                    ConversionMode.AsciiToHex => asciiConverter.ToHex(text),
                    ConversionMode.HexToAscii => asciiConverter.ToAscii(text),
                    ConversionMode.TextToCp500 => cp500Converter.ToHex(text),
                    ConversionMode.Cp500ToText => cp500Converter.ToText(text),
                    ConversionMode.AsciiHexToCp500 => cp500Converter.ToHex(asciiConverter.ToAscii(text)),
                    ConversionMode.Cp500ToAsciiHex => asciiConverter.ToHex(cp500Converter.ToText(text)),
                    _ => throw new ArgumentException("Select a supported conversion mode.")
                }, token);
                if (revision == inputRevision)
                    Output = converted;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                if (revision == inputRevision) Error = ex.Message;
            }
        }));
    }
}
