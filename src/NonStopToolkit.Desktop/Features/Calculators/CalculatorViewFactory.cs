using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Desktop.Controls;
using NonStopToolkit.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace NonStopToolkit.Desktop.Features.Calculators;

public sealed class CalculatorViewFactory(string toolId) : IWorkspaceViewFactory
{
    public string ToolId => toolId;
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (CalculatorWorkspace)workspace;
        ComboBox Select(string label, string property, object items)
        {
            var combo = new ComboBox { Header = label, ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SetBinding(ComboBox.SelectedIndexProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.TwoWay });
            return combo;
        }
        var inputs = ToolLayout.Stack(Select("Operation", nameof(vm.Mode), vm.Operations));
        var results = ToolLayout.Stack();
        if (vm.Kind == CalculatorKind.Bitwise)
        {
            ComboBox NumberOption(string label, string property, int[] items)
            {
                var combo = new ComboBox { Header = label, ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.SetBinding(ComboBox.SelectedItemProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.TwoWay });
                return combo;
            }
            inputs.Children.Add(ToolLayout.Columns(140, NumberOption("Input base", nameof(vm.Radix), [2, 10, 16]), NumberOption("Bit width", nameof(vm.Width), [8, 16, 32, 64])));
        }
        if (vm.Kind == CalculatorKind.Base64)
        {
            var urlSafe = new CheckBox { Content = "URL-safe Base64 (- and _, no output padding)" };
            urlSafe.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.UrlSafe)), Mode = BindingMode.TwoWay });
            inputs.Children.Add(urlSafe);
        }
        if (vm.Kind == CalculatorKind.TripleDes)
        {
            inputs.Children.Add(ToolLayout.Columns(160, Select("Cipher mode", nameof(vm.Cipher), new[] { "ECB", "CBC" }),
                Select("Padding", nameof(vm.Padding), new[] { "None", "PKCS7", "Zeroes" })));
            var key = new PasswordBox { Header = "Key (16 or 24 bytes, hex)", MaxLength = 192, PasswordRevealMode = PasswordRevealMode.Peek };
            key.PasswordChanged += (_, _) => vm.Key = key.Password;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Key) && key.Password != vm.Key) key.Password = vm.Key; };
            inputs.Children.Add(key);
            var checkValue = new TextBox { Header = "Key check value (KCV · 6 hex digits)", IsReadOnly = true };
            checkValue.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Kcv)), Mode = BindingMode.OneWay });
            results.Children.Add(checkValue);
            var kcvStatus = Ui.Text("");
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Kcv)) kcvStatus.Text = ""; };
            results.Children.Add(Ui.Row(new Button { Content = "Calculate KCV", Command = vm.CalculateKcvCommand }, Ui.Button("Copy KCV", () =>
            {
                if (vm.Kcv.Length == 0) return;
                try { var data = new DataPackage(); data.SetText(vm.Kcv); Clipboard.SetContent(data); kcvStatus.Text = "KCV copied"; }
                catch { kcvStatus.Text = "Clipboard unavailable. Select and copy the KCV manually."; }
            })));
            results.Children.Add(kcvStatus);
            var iv = Ui.Input(vm, nameof(vm.Iv), "IV (8 bytes, hex)");
            iv.MaxLength = 64;
            void UpdateIv() => iv.Visibility = vm.ShowIv ? Visibility.Visible : Visibility.Collapsed;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ShowIv)) UpdateIv(); };
            UpdateIv();
            inputs.Children.Add(iv);
        }
        var input = Ui.Input(vm, nameof(vm.Input), vm.InputLabel, vm.Kind != CalculatorKind.Bitwise);
        input.MaxLength = vm.Kind == CalculatorKind.Bitwise ? 128 : 65536;
        input.SetBinding(TextBox.HeaderProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.InputLabel)) });
        inputs.Children.Add(input);
        if (vm.Kind == CalculatorKind.Bitwise)
        {
            var operand = Ui.Input(vm, nameof(vm.Operand), vm.OperandLabel);
            operand.MaxLength = 128;
            operand.SetBinding(TextBox.HeaderProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.OperandLabel)) });
            void UpdateOperand() => operand.Visibility = vm.ShowOperand ? Visibility.Visible : Visibility.Collapsed;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ShowOperand)) UpdateOperand(); };
            UpdateOperand();
            inputs.Children.Add(operand);
        }
        inputs.Children.Add(Ui.Row(new Button { Content = "Calculate", Command = vm.CalculateCommand }, new Button { Content = "Clear inputs", Command = vm.ClearCommand }));
        inputs.Children.Add(Ui.BoundText(vm, nameof(vm.Error)));
        inputs.Children.Add(Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        var output = new RawTextViewer { Header = "Result", MinHeight = 180, MaxHeight = 360, TextWrapping = TextWrapping.Wrap };
        output.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Output)), Mode = BindingMode.OneWay });
        results.Children.Add(output);
        var copied = Ui.Text("");
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Output)) copied.Text = ""; };
        results.Children.Add(Ui.Button("Copy result", () =>
        {
            if (vm.Output.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copied.Text = "Copied"; }
            catch { copied.Text = "Clipboard unavailable. Select and copy the result manually."; }
        }));
        results.Children.Add(copied);
        var subtitle = vm.Kind switch
        {
            CalculatorKind.Bitwise => "Fixed-width operations in binary, decimal and hex.",
            CalculatorKind.Base64 => "Encode or decode text and binary data.",
            _ => "Encrypt, decrypt and check keys locally."
        };
        return ToolLayout.Page(vm.Title, subtitle,
            ToolLayout.Columns(340, ToolLayout.Section("Inputs", inputs), ToolLayout.Section("Results", results)),
            ToolLayout.Help(Ui.Text(vm.Help)));
    }
}
