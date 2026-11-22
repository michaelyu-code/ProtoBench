using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Desktop.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Calculators;

public sealed partial class CalculatorViewFactory
{
    private static FrameworkElement CreateTripleDes(CalculatorWorkspace vm)
    {
        void Bind(FrameworkElement control, DependencyProperty target, string property, BindingMode mode = BindingMode.OneWay) =>
            control.SetBinding(target, new Binding { Source = vm, Path = new PropertyPath(property), Mode = mode });

        ComboBox Select(string label, string property, object items)
        {
            var combo = new ComboBox { Header = label, ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch };
            Bind(combo, ComboBox.SelectedIndexProperty, property, BindingMode.TwoWay);
            return combo;
        }
        CheckBox Option(string label, string property)
        {
            var option = new CheckBox { Content = Ui.Text(label) };
            Bind(option, CheckBox.IsCheckedProperty, property, BindingMode.TwoWay);
            return option;
        }
        TextBlock Validation(string property)
        {
            var text = Ui.BoundText(vm, property);
            text.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"];
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            return text;
        }
        void MarkInvalid(Control control, string property, Func<string> error)
        {
            var normalBrush = control.BorderBrush;
            void Refresh()
            {
                control.BorderBrush = error().Length == 0 ? normalBrush
                    : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemFillColorCriticalBrush"];
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(control, error());
            }
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == property) Refresh(); };
            Refresh();
        }

        var settings = ToolLayout.Stack();
        settings.Spacing = 6;
        var choices = ToolLayout.Columns(90,
            Select("Operation", nameof(vm.Mode), vm.Operations),
            Select("Cipher mode", nameof(vm.Cipher), new[] { "ECB", "CBC" }),
            Select("Padding", nameof(vm.Padding), new[] { "None", "PKCS7", "Zeroes" }),
            Select("Input format", nameof(vm.InputFormat), vm.TripleDesInputFormats));
        choices.MaximumColumns = 4;
        settings.Children.Add(choices);
        var key = new PasswordBox { Header = "Key * (16 or 24 bytes, hex)", MaxLength = 192, Password = vm.Key };
        key.PasswordChanged += (_, _) => vm.Key = key.Password;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Key) && key.Password != vm.Key) key.Password = vm.Key; };
        MarkInvalid(key, nameof(vm.KeyError), () => vm.KeyError);
        settings.Children.Add(key);
        settings.Children.Add(Validation(nameof(vm.KeyError)));
        var showKey = Option("Show key", nameof(vm.ShowKey));
        void RevealKey() => key.PasswordRevealMode = vm.ShowKey ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ShowKey)) RevealKey(); };
        RevealKey();
        var verbose = Option("Verbose calculation", nameof(vm.Verbose));
        ToolTipService.SetToolTip(verbose, "Show padding, block operations and CBC chaining in the results.");
        var pack = Option("Pack input", nameof(vm.PackInput));
        ToolTipService.SetToolTip(pack, "Remove spaces, tabs and line breaks before interpreting the input. Unchecked preserves text spacing. Key and IV are unchanged.");
        settings.Children.Add(Ui.Row(showKey, verbose, pack));
        var includeKeys = Option("Include key values in details", nameof(vm.IncludeKeyValues));
        void ShowKeyOption() => includeKeys.Visibility = vm.Verbose ? Visibility.Visible : Visibility.Collapsed;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Verbose)) ShowKeyOption(); };
        ShowKeyOption();
        settings.Children.Add(includeKeys);
        var iv = Ui.Input(vm, nameof(vm.Iv), "IV * (8 bytes, hex)");
        iv.MaxLength = 64;
        MarkInvalid(iv, nameof(vm.IvError), () => vm.IvError);
        var ivPanel = ToolLayout.Stack(iv, Validation(nameof(vm.IvError)));
        ivPanel.Spacing = 4;
        void ShowIv() => ivPanel.Visibility = vm.ShowIv ? Visibility.Visible : Visibility.Collapsed;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ShowIv)) ShowIv(); };
        ShowIv();
        settings.Children.Add(ivPanel);

        var input = Ui.Input(vm, nameof(vm.Input), vm.InputLabel + " *", true);
        input.MaxLength = 65536;
        input.MaxHeight = double.PositiveInfinity;
        input.VerticalAlignment = VerticalAlignment.Stretch;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.InputLabel)) input.Header = vm.InputLabel + " *"; };
        MarkInvalid(input, nameof(vm.InputError), () => vm.InputError);
        var actions = ToolLayout.Stack(Validation(nameof(vm.InputError)),
            Ui.Row(Ui.Primary(new Button { Content = "Calculate", Command = vm.CalculateCommand }),
                new Button { Content = "Clear inputs", Command = vm.ClearCommand }),
            Validation(nameof(vm.Error)), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        actions.Spacing = 4;
        var form = new Grid { RowSpacing = 8 };
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.Children.Add(settings);
        Grid.SetRow(input, 1); form.Children.Add(input);
        Grid.SetRow(actions, 2); form.Children.Add(actions);
        // Keep actions visible; only use outer scrolling below the form's minimum usable height.
        void FitMinimum() => form.MinHeight = settings.ActualHeight + actions.ActualHeight + 96;
        settings.SizeChanged += (_, _) => FitMinimum();
        actions.SizeChanged += (_, _) => FitMinimum();
        var inputSection = ToolLayout.FittedSection("Inputs · * required", form);

        var kcv = new TextBox { Header = "Key check value (KCV · 6 hex digits)", IsReadOnly = true };
        Bind(kcv, TextBox.TextProperty, nameof(vm.Kcv));
        var kcvFeedback = Ui.Text("");
        void Copy(string value, TextBlock feedback, string message)
        {
            if (value.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data); feedback.Text = message; }
            catch { feedback.Text = "Clipboard unavailable. Select and copy the value manually."; }
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Kcv)) kcvFeedback.Text = ""; };
        var kcvPanel = ToolLayout.Stack(kcv, Ui.Row(
            new Button { Content = "Calculate KCV", Command = vm.CalculateKcvCommand },
            Ui.Button("Copy KCV", () => Copy(vm.Kcv, kcvFeedback, "KCV copied"))), Validation(nameof(vm.KeyError)), kcvFeedback);
        kcvPanel.Spacing = 6;
        var output = new RawTextViewer { TextWrapping = TextWrapping.Wrap };
        Bind(output, TextBox.TextProperty, nameof(vm.DisplayOutput));
        var feedback = Ui.Text("");
        var copyResult = Ui.Button("Copy result", () => Copy(vm.Output, feedback, "Copied"));
        var copyDetails = Ui.Button("Copy calculation details", () => Copy(vm.CalculationDetails, feedback, "Calculation details copied"));
        void ShowCopyDetails() => copyDetails.Visibility = vm.CalculationDetails.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CalculationDetails)) ShowCopyDetails();
            if (e.PropertyName == nameof(vm.Output)) feedback.Text = "";
        };
        ShowCopyDetails();
        var resultSection = ToolLayout.TextResult("Results", output, copyResult, feedback, copyDetails, kcvPanel);
        return ToolLayout.Page(vm.Title, "Encrypt, decrypt and check keys locally.",
            ToolLayout.Workspace(vm, inputSection, resultSection, () => vm.Output.Length > 0),
            ToolLayout.Help(Ui.Text(vm.Help)));
    }
}
