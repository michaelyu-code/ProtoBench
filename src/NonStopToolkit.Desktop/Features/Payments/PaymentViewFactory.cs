using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Desktop.Controls;
using NonStopToolkit.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace NonStopToolkit.Desktop.Features.Payments;

public sealed class PaymentViewFactory(string toolId) : IWorkspaceViewFactory
{
    public string ToolId => toolId;
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (PaymentWorkspace)workspace;
        var modes = new ComboBox { Header = "Operation", ItemsSource = vm.Modes, SelectedIndex = vm.Mode, HorizontalAlignment = HorizontalAlignment.Stretch };
        modes.SelectionChanged += (_, _) => { if (modes.SelectedIndex >= 0) vm.Mode = modes.SelectedIndex; };
        var input = Ui.Input(vm, nameof(vm.Input), vm.InputLabel, true);
        input.MaxLength = 65536;
        input.SetBinding(TextBox.HeaderProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.InputLabel)) });
        var output = new RawTextViewer { Header = "Result", MinHeight = vm is LuhnWorkspace or ArqcWorkspace ? 80 : 180, MaxHeight = 360, TextWrapping = TextWrapping.Wrap };
        output.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Output)), Mode = BindingMode.OneWay });
        var copyStatus = Ui.Text("");
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Output)) copyStatus.Text = ""; };
        var inputs = ToolLayout.Stack(modes);
        var results = ToolLayout.Stack();
        var textResults = ToolLayout.Stack();
        void UpdateInputSize()
        {
            var multiline = vm is not LuhnWorkspace && (vm is not EmvWorkspace ev || !ev.IsTagMode || ev.SelectedDefinition?.Tag == "8E");
            input.AcceptsReturn = multiline;
            input.MinHeight = multiline ? 100 : 32;
            input.MaxHeight = multiline ? 240 : 80;
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.InputLabel)) UpdateInputSize(); };
        UpdateInputSize();
        if (vm.UsesKey)
        {
            var key = new PasswordBox { Header = "AC session key (16 bytes, hex)", MaxLength = 128, PasswordRevealMode = PasswordRevealMode.Peek };
            key.Password = vm.Key;
            key.PasswordChanged += (_, _) => vm.Key = key.Password;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Key) && key.Password != vm.Key) key.Password = vm.Key; };
            inputs.Children.Add(key);
        }
        inputs.Children.Add(input);
        if (vm.UsesKey)
        {
            var expected = Ui.Input(vm, nameof(vm.Expected), "Expected ARQC (optional, 8 bytes hex)");
            expected.MaxLength = 64;
            inputs.Children.Add(expected);
        }
        var calculate = new Button { Content = "Calculate / Decode", Command = vm.CalculateCommand };
        if (vm is EmvWorkspace live)
        {
            inputs.Children.Add(Ui.Button("Load example", live.LoadExample));
            void UpdateCalculate() => calculate.Visibility = live.IsBitMode ? Visibility.Collapsed : Visibility.Visible;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.InputLabel)) UpdateCalculate(); };
            UpdateCalculate();
        }
        inputs.Children.Add(Ui.Row(calculate, new Button { Content = "Clear inputs", Command = vm.ClearCommand }));
        inputs.Children.Add(Ui.BoundText(vm, nameof(vm.Error)));
        if (vm is not EmvWorkspace) inputs.Children.Add(Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        textResults.Children.Add(output);
        textResults.Children.Add(Ui.Button("Copy result", () =>
        {
            if (vm.Output.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copyStatus.Text = "Copied"; }
            catch { copyStatus.Text = "Clipboard unavailable. Select and copy the result manually."; }
        }));
        textResults.Children.Add(copyStatus);
        if (vm is EmvWorkspace emv)
        {
            results.Children.Add(Ui.BoundText(emv, nameof(emv.ProfileCaption)));
            results.Children.Add(Ui.BoundText(emv, nameof(emv.FieldSummary)));
            results.Children.Add(new EmvBitEditor(emv));
            var details = new Expander { Header = "Decoded text", Content = textResults, IsExpanded = !emv.IsBitMode,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.InputLabel)) details.IsExpanded = !emv.IsBitMode; };
            results.Children.Add(details);
        }
        else results.Children.Add(textResults);
        if (vm is EmvWorkspace tagWorkspace)
        {
            modes.Header = "EMV tag / decoder";
            var profile = new ComboBox { Header = "Scheme / profile", HorizontalAlignment = HorizontalAlignment.Stretch };
            profile.SetBinding(ComboBox.ItemsSourceProperty, new Binding { Source = tagWorkspace, Path = new PropertyPath(nameof(tagWorkspace.ProfileChoices)), Mode = BindingMode.OneWay });
            profile.SetBinding(ComboBox.SelectedIndexProperty, new Binding { Source = tagWorkspace, Path = new PropertyPath(nameof(tagWorkspace.ProfileIndex)), Mode = BindingMode.TwoWay });
            inputs.Children.Remove(modes);
            inputs.Children.Remove(input);
            var tagOptions = new AdaptiveColumns { MaximumColumns = 3, MinimumColumnWidth = 220 };
            tagOptions.Children.Add(modes);
            tagOptions.Children.Add(profile);
            tagOptions.Children.Add(input);
            inputs.Children.Insert(0, tagOptions);
            return ToolLayout.Page(vm.Title, "Decode tag values and edit individual bits.",
                ToolLayout.Section("", inputs), ToolLayout.Section("", results),
                ToolLayout.Help(Ui.BoundText(vm, nameof(vm.Help))));
        }
        return ToolLayout.Page(vm.Title, vm is EmvWorkspace ? "Decode tag values and edit individual bits." : "Local payment card calculations and decoding.",
            ToolLayout.Columns(340, ToolLayout.Section("Inputs", inputs), ToolLayout.Section(vm is EmvWorkspace ? "" : "Results", results)),
            ToolLayout.Help(Ui.BoundText(vm, nameof(vm.Help))));
    }
}
