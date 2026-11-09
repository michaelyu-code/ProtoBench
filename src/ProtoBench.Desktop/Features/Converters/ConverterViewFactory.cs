using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
namespace ProtoBench.Desktop.Features.Converters;

public sealed class ConverterViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "base-converter";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (ConverterWorkspace)workspace;
        ComboBox Bases(string header, int initial, Action<int> change)
        {
            var combo = new ComboBox { Header = header, ItemsSource = new[] { 2, 10, 16 }, SelectedItem = initial, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is int value) change(value); };
            return combo;
        }
        var result = new RawTextViewer { Header = "Result", MinHeight = 180, MaxHeight = 360, TextWrapping = TextWrapping.Wrap };
        result.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Source = vm, Path = new PropertyPath("Output"), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        var copyStatus = Ui.Text("");
        var removeSpaces = new CheckBox { Content = "Remove spaces from input" };
        removeSpaces.SetBinding(CheckBox.IsCheckedProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Source = vm,
            Path = new PropertyPath(nameof(vm.RemoveSpaces)),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay
        });
        ToolTipService.SetToolTip(removeSpaces, "Ignore ordinary spaces before converting, including spaces in ASCII/CP500 text. Tabs and line breaks are kept. Your original input stays unchanged.");
        var numericOptions = ToolLayout.Columns(140, Bases("From base", vm.FromBase, b => vm.FromBase = b), Bases("To base", vm.ToBase, b => vm.ToBase = b));
        var mode = new ComboBox
        {
            Header = "Conversion",
            ItemsSource = new[] { "Numeric bases", "ASCII → Hex", "Hex → ASCII", "Text → EBCDIC CP500 (hex)", "EBCDIC CP500 (hex) → Text", "ASCII hex → EBCDIC CP500 hex", "EBCDIC CP500 hex → ASCII hex" },
            SelectedIndex = (int)vm.Mode,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var help = Ui.Text("");
        var input = Ui.Input(vm, "Input", "Value", true);
        void UpdateMode()
        {
            numericOptions.Visibility = vm.Mode == ConversionMode.Numeric ? Visibility.Visible : Visibility.Collapsed;
            input.AcceptsReturn = vm.Mode != ConversionMode.Numeric;
            input.MinHeight = vm.Mode == ConversionMode.Numeric ? 32 : 120;
            help.Text = vm.Mode switch
            {
                ConversionMode.AsciiToHex => "Strict ASCII (00–7F). Spaces are preserved unless Remove spaces is checked. Line breaks and control characters are preserved. Example: Hello → 48 65 6C 6C 6F.",
                ConversionMode.HexToAscii => "Enter complete hex bytes, contiguous or separated by whitespace: 48656C6C6F or 48 65 6C 6C 6F. Bytes above 7F are rejected. Control bytes are preserved and may be invisible in the result.",
                ConversionMode.TextToCp500 => "Encode text using IBM EBCDIC CP500. Example: Hello → C8 85 93 93 96. Unrepresentable characters are rejected. Spaces are preserved unless Remove spaces is checked. Line breaks and controls are preserved.",
                ConversionMode.Cp500ToText => "Decode CP500 hex byte pairs: C885939396 or C8 85 93 93 96 → Hello. Control bytes are preserved and may be invisible. This uses CP500, not CP037 or the Euro variant CP1148.",
                ConversionMode.AsciiHexToCp500 => "Convert ASCII byte pairs to CP500 byte pairs. Example: 41 42 43 → C1 C2 C3. ASCII input bytes must be 00–7F.",
                ConversionMode.Cp500ToAsciiHex => "Convert CP500 byte pairs to ASCII byte pairs. Example: C1 C2 C3 → 41 42 43. CP500 characters outside ASCII are rejected, without replacement.",
                _ => "Signed values use a leading minus sign. Hex and binary prefixes (0x, 0b) are optional. No implicit bit width or two’s complement."
            };
        }
        mode.SelectionChanged += (_, _) => { vm.Mode = (ConversionMode)mode.SelectedIndex; UpdateMode(); };
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Output)) copyStatus.Text = ""; };
        UpdateMode();
        return ToolLayout.Page("Base Converter", "Numeric bases, ASCII and EBCDIC CP500.",
            ToolLayout.Columns(340,
                ToolLayout.Section("Inputs", mode, numericOptions, input, removeSpaces,
                    new Button { Content = "Convert", Command = vm.ConvertCommand }, Ui.BoundText(vm, "Error"),
                    Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, "Status"))),
                ToolLayout.Section("Results", result,
                    Ui.Button("Copy result", () => { if (vm.Output.Length == 0) return; try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copyStatus.Text = "Copied"; } catch { copyStatus.Text = "Clipboard is unavailable. Select the result and copy manually."; } }), copyStatus)),
            ToolLayout.Help(help));
    }
}
