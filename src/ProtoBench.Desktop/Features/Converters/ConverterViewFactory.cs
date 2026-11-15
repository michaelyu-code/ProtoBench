using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Text;
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
        var encoding = new ComboBox { Header = "Text encoding", ItemsSource = TextEncodings.Options, HorizontalAlignment = HorizontalAlignment.Stretch };
        encoding.SetBinding(ComboBox.SelectedItemProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Source = vm, Path = new PropertyPath(nameof(vm.TextEncoding)), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay
        });
        var mode = new ComboBox
        {
            Header = "Conversion",
            ItemsSource = new[] { "Numeric bases", "Text → Hex", "Hex → Text", "Text → EBCDIC CP500 (hex)", "EBCDIC CP500 (hex) → Text", "Encoded hex → EBCDIC CP500 hex", "EBCDIC CP500 hex → Encoded hex" },
            SelectedIndex = (int)vm.Mode,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var help = Ui.Text("");
        var input = Ui.Input(vm, "Input", "Value", true);
        void UpdateMode()
        {
            numericOptions.Visibility = vm.Mode == ConversionMode.Numeric ? Visibility.Visible : Visibility.Collapsed;
            encoding.Visibility = vm.Mode is ConversionMode.AsciiToHex or ConversionMode.HexToAscii or ConversionMode.AsciiHexToCp500 or ConversionMode.Cp500ToAsciiHex ? Visibility.Visible : Visibility.Collapsed;
            input.AcceptsReturn = vm.Mode != ConversionMode.Numeric;
            input.MinHeight = vm.Mode == ConversionMode.Numeric ? 32 : 120;
            help.Text = vm.Mode switch
            {
                ConversionMode.AsciiToHex => "Encode using the selected text encoding, without adding a byte order mark. Unrepresentable characters are rejected. Spaces are preserved unless Remove spaces is checked; line breaks and controls are preserved.",
                ConversionMode.HexToAscii => "Decode using the selected text encoding. Enter complete hex bytes, contiguous or separated by whitespace: 48656C6C6F or 48 65 6C 6C 6F. Invalid byte sequences are rejected. Control characters may be invisible.",
                ConversionMode.TextToCp500 => "Encode text using IBM EBCDIC CP500. Example: Hello → C8 85 93 93 96. Unrepresentable characters are rejected. Spaces are preserved unless Remove spaces is checked. Line breaks and controls are preserved.",
                ConversionMode.Cp500ToText => "Decode CP500 hex byte pairs: C885939396 or C8 85 93 93 96 → Hello. Control bytes are preserved and may be invisible. This uses CP500, not CP037 or the Euro variant CP1148.",
                ConversionMode.AsciiHexToCp500 => "Decode hex with the selected text encoding, then encode as CP500 hex. Invalid bytes and characters outside CP500 are rejected.",
                ConversionMode.Cp500ToAsciiHex => "Decode CP500 hex, then encode hex using the selected text encoding. Unrepresentable characters are rejected, without replacement.",
                _ => "Signed values use a leading minus sign. Hex and binary prefixes (0x, 0b) are optional. No implicit bit width or two’s complement."
            };
        }
        mode.SelectionChanged += (_, _) => { vm.Mode = (ConversionMode)mode.SelectedIndex; UpdateMode(); };
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Output)) copyStatus.Text = ""; };
        UpdateMode();
        return ToolLayout.Page("Base Converter", "Numeric bases, text encodings and EBCDIC CP500.",
            ToolLayout.Columns(340,
                ToolLayout.Section("Inputs", mode, numericOptions, encoding, input, removeSpaces,
                    new Button { Content = "Convert", Command = vm.ConvertCommand }, Ui.BoundText(vm, "Error"),
                    Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, "Status"))),
                ToolLayout.Section("Results", result,
                    Ui.Button("Copy result", () => { if (vm.Output.Length == 0) return; try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copyStatus.Text = "Copied"; } catch { copyStatus.Text = "Clipboard is unavailable. Select the result and copy manually."; } }), copyStatus)),
            ToolLayout.Help(help));
    }
}
