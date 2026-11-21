using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Payments;

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
        var calculate = Ui.Primary(new Button { Content = vm is EmvWorkspace or BitmapWorkspace ? "Decode" : "Calculate", Command = vm.CalculateCommand });
        void UpdateAction() => calculate.Content = vm is LuhnWorkspace ? vm.Mode == 0 ? "Validate" : "Calculate check digit"
            : vm is BitmapWorkspace ? vm.Mode == 1 ? "Encode" : "Decode" : vm is EmvWorkspace ? "Decode" : "Calculate ARQC";
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Mode)) UpdateAction(); };
        UpdateAction();
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
        if (vm is LuhnWorkspace) output.FontSize = 18;
        if (vm is BitmapWorkspace)
        {
            var fieldGroups = ToolLayout.Stack();
            textResults.Children.Add(ResultValues.Create(vm, () => vm.Output, "Hex"));
            textResults.Children.Add(fieldGroups);
            textResults.Children.Add(new Expander { Header = "Binary and decoded text", Content = output,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(vm.Output)) return;
                fieldGroups.Children.Clear();
                var line = vm.Output.Split('\n').FirstOrDefault(line => line.StartsWith("Data fields (", StringComparison.Ordinal));
                if (line is null) return;
                var separator = line.IndexOf(": ", StringComparison.Ordinal);
                var numbers = line[(separator + 2)..].Split(',').Select(s => int.TryParse(s.Trim(), out var n) ? n : 0).Where(n => n > 0).ToArray();
                foreach (var secondary in new[] { false, true })
                {
                    var group = numbers.Where(n => (n > 64) == secondary).ToArray();
                    if (group.Length == 0) continue;
                    fieldGroups.Children.Add(Ui.Text(secondary ? "Secondary bitmap fields" : "Primary bitmap fields", 16));
                    var chips = new AdaptiveColumns { MinimumColumnWidth = 64, MaximumColumns = 8, ColumnSpacing = 6, RowSpacing = 6 };
                    foreach (var number in group) chips.Children.Add(ToolLayout.Section("", Ui.Text($"DE {number}")));
                    fieldGroups.Children.Add(chips);
                }
                if (numbers.Length == 0) fieldGroups.Children.Add(Ui.Text("No data fields set."));
            };
        }
        else if (vm is ArqcWorkspace) textResults.Children.Add(ResultValues.Create(vm, () => vm.Output, "ARQC", "Verification"));
        else textResults.Children.Add(output);
        var copyResult = Ui.Button("Copy result", () =>
        {
            if (vm.Output.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copyStatus.Text = "Copied"; }
            catch { copyStatus.Text = "Clipboard unavailable. Select and copy the result manually."; }
        });
        textResults.Children.Add(copyResult);
        textResults.Children.Add(copyStatus);
        FrameworkElement? emvResults = null;
        if (vm is EmvWorkspace emv)
        {
            var summary = Ui.BoundText(emv, nameof(emv.FieldSummary));
            var bits = new EmvBitEditor(emv);
            textResults.Children.Clear();
            output.Header = null;
            output.CompactViewport = false;
            output.MinHeight = 0;
            output.MaxHeight = double.PositiveInfinity;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(output, "Decoded text");
            var decoded = new Grid { RowSpacing = 8 };
            decoded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            decoded.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            decoded.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            decoded.Children.Add(output);
            Grid.SetRow(copyResult, 1); decoded.Children.Add(copyResult);
            Grid.SetRow(copyStatus, 2); decoded.Children.Add(copyStatus);
            var details = new Expander { Header = "Decoded text", Content = decoded, IsExpanded = !emv.IsBitMode,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            var layout = new Grid { RowSpacing = 8 };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(summary);
            Grid.SetRow(bits, 1); layout.Children.Add(bits);
            Grid.SetRow(details, 2); layout.Children.Add(details);
            void ResizeDecoded()
            {
                // A collapsed bit editor retains its previous ActualHeight.
                // Remove its row and exclude that stale height for rule/TLV tags.
                var showBits = emv.IsBitMode;
                layout.RowDefinitions[1].Height = showBits ? GridLength.Auto : new GridLength(0);
                layout.RowDefinitions[2].Height = details.IsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
                // Keep a usable text viewport on short windows; larger windows
                // give the remaining height to the editor's own scroll viewer.
                layout.MinHeight = (summary.Visibility == Visibility.Visible ? summary.ActualHeight : 0)
                    + (showBits ? bits.ActualHeight : 0) + Math.Max(48, details.MinHeight) + 16
                    + (details.IsExpanded ? 160 + details.Padding.Top + details.Padding.Bottom : 0);
            }
            details.Expanding += (_, _) => ResizeDecoded();
            details.Collapsed += (_, _) => ResizeDecoded();
            bits.SizeChanged += (_, _) => ResizeDecoded();
            summary.SizeChanged += (_, _) => ResizeDecoded();
            ResizeDecoded();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(vm.InputLabel)) return;
                details.IsExpanded = !emv.IsBitMode;
                ResizeDecoded();
            };
            emvResults = ToolLayout.FittedSection("Results", layout);
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
            var tagOptions = new AdaptiveColumns { MinimumColumnWidth = 220 };
            tagOptions.Children.Add(modes);
            tagOptions.Children.Add(profile);
            inputs.Children.Insert(0, tagOptions);
            inputs.Children.Insert(1, input);
            return ToolLayout.Page(vm.Title, "Decode tag values and edit individual bits.",
                ToolLayout.Workspace(vm, ToolLayout.Section("Inputs", inputs), emvResults!, () => vm.Output.Length > 0,
                    resultColumnWeight: 1.6),
                ToolLayout.Help(Ui.BoundText(vm, nameof(vm.Help))));
        }
        var inputSection = ToolLayout.Section("Inputs", inputs);
        FrameworkElement resultSection;
        if (vm is LuhnWorkspace)
        {
            textResults.Children.Remove(output);
            textResults.Children.Remove(copyResult);
            textResults.Children.Remove(copyStatus);
            resultSection = ToolLayout.TextResult("Results", output, copyResult, copyStatus);
        }
        else resultSection = ToolLayout.Section("Results", results);
        return ToolLayout.Page(vm.Title, "Local payment card calculations and decoding.",
            ToolLayout.Workspace(vm, inputSection, resultSection, () => vm.Output.Length > 0),
            ToolLayout.Help(Ui.BoundText(vm, nameof(vm.Help))));
    }
}
