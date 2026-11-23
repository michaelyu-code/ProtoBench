using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Calculators;

public sealed partial class CalculatorViewFactory(string toolId) : IWorkspaceViewFactory
{
    public string ToolId => toolId;
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (CalculatorWorkspace)workspace;
        if (vm.Kind == CalculatorKind.TripleDes) return CreateTripleDes(vm);
        var required = new RequiredInputs(vm);
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
            var urlSafe = new CheckBox { Content = "URL-safe Base64" };
            ToolTipService.SetToolTip(urlSafe, "Uses - and _ and omits output padding.");
            urlSafe.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.UrlSafe)), Mode = BindingMode.TwoWay });
            inputs.Children.Add(urlSafe);
        }
        var input = Ui.Input(vm, nameof(vm.Input), vm.InputLabel, vm.Kind != CalculatorKind.Bitwise);
        input.MaxLength = vm.Kind == CalculatorKind.Bitwise ? 128 : 65536;
        input.SetBinding(TextBox.HeaderProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.InputLabel)) });
        required.Require(input, "Enter the first operand.", () => vm.Kind == CalculatorKind.Bitwise);
        inputs.Children.Add(input);
        if (vm.Kind == CalculatorKind.Bitwise)
        {
            var operand = Ui.Input(vm, nameof(vm.Operand), vm.OperandLabel);
            operand.MaxLength = 128;
            operand.SetBinding(TextBox.HeaderProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.OperandLabel)) });
            required.Require(operand, "Enter the second operand or shift count.", () => vm.ShowOperand);
            void UpdateOperand() => operand.Visibility = vm.ShowOperand ? Visibility.Visible : Visibility.Collapsed;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ShowOperand)) UpdateOperand(); };
            UpdateOperand();
            inputs.Children.Add(operand);
        }
        inputs.Children.Add(Ui.Row(Ui.Primary(required.Guard(new Button { Content = "Calculate", Command = vm.CalculateCommand })),
            Ui.Button("Clear inputs", () => { vm.ClearCommand.Execute(null); required.Clear(); })));
        inputs.Children.Add(Ui.BoundText(vm, nameof(vm.Error)));
        inputs.Children.Add(Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        var output = new RawTextViewer { Header = "Result", MinHeight = 180, MaxHeight = 360, TextWrapping = TextWrapping.Wrap };
        output.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.DisplayOutput)), Mode = BindingMode.OneWay });
        results.Children.Add(vm.Kind == CalculatorKind.Bitwise
            ? ResultValues.Create(vm, () => vm.Output, "Hex", "Decimal", "Binary") : output);
        var copied = Ui.Text("");
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Output)) copied.Text = ""; };
        var copyResult = Ui.Button("Copy result", () =>
        {
            if (vm.Output.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copied.Text = "Copied"; }
            catch { copied.Text = "Clipboard unavailable. Select and copy the result manually."; }
        });
        results.Children.Add(copyResult);
        results.Children.Add(copied);
        var subtitle = vm.Kind switch
        {
            CalculatorKind.Bitwise => "Fixed-width operations in binary, decimal and hex.",
            CalculatorKind.Base64 => "Encode or decode text and binary data.",
            _ => "Encrypt, decrypt and check keys locally."
        };
        FrameworkElement resultSection;
        if (vm.Kind != CalculatorKind.Bitwise)
        {
            results.Children.Clear();
            resultSection = ToolLayout.TextResult("Results", output, copyResult, copied);
        }
        else resultSection = ToolLayout.Section("Results", results);
        return ToolLayout.Page(vm.Title, subtitle,
            ToolLayout.Workspace(vm, ToolLayout.Section("Inputs", inputs), resultSection, () => vm.Output.Length > 0),
            ToolLayout.Help(Ui.Text(vm.Help)));
    }
}
