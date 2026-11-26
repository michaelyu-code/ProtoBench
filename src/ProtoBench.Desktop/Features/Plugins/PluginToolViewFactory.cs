using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.PluginSdk;
using ProtoBench.PluginSdk.WinUI;
using System.Globalization;

namespace ProtoBench.Desktop.Features.Plugins;

public sealed class PluginToolViewFactory(PluginTool tool, IToolkitPlugin plugin) : IWorkspaceViewFactory
{
    public string ToolId => tool.Id;
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (PluginToolWorkspace)workspace;
        if (plugin is ICustomToolViewProvider custom && custom.CreateView(tool.Id, vm.Session) is { } view) return view;
        if (tool.WebView is { } web) return new PluginWebViewHost(vm, web);
        var required = new RequiredInputs(vm);
        var inputs = ToolLayout.Stack();
        var advanced = ToolLayout.Stack();
        var reset = new List<Action>();
        foreach (var definition in tool.Inputs)
        {
            // A required advanced input must be visible before running the tool.
            var target = definition.IsAdvanced && !definition.Required ? advanced : inputs;
            var label = definition.Label;
            switch (definition.Kind)
            {
                case ToolInputKind.Secret:
                    var secret = new PasswordBox { Header = label, Password = definition.DefaultValue, MaxLength = 1048576 };
                    secret.PasswordChanged += (_, _) => vm.SetInput(definition.Id, secret.Password);
                    if (definition.Required) required.Require(secret, $"Enter {definition.Label}.");
                    reset.Add(() => secret.Password = definition.DefaultValue);
                    target.Children.Add(secret);
                    break;
                case ToolInputKind.Choice:
                    var choice = new ComboBox { Header = label, ItemsSource = definition.Choices, SelectedItem = definition.DefaultValue, HorizontalAlignment = HorizontalAlignment.Stretch };
                    choice.SelectionChanged += (_, _) => vm.SetInput(definition.Id, (string?)choice.SelectedItem ?? "");
                    if (definition.Required) required.Require(choice, $"Select {definition.Label}.");
                    reset.Add(() => choice.SelectedItem = definition.DefaultValue);
                    target.Children.Add(choice);
                    break;
                case ToolInputKind.Integer:
                    var number = new NumberBox
                    {
                        Header = label, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                        Minimum = definition.Minimum ?? int.MinValue, Maximum = definition.Maximum ?? int.MaxValue,
                        Value = int.TryParse(definition.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var initial) ? initial : double.NaN
                    };
                    number.ValueChanged += (_, _) => vm.SetInput(definition.Id, double.IsNaN(number.Value) ? "" : number.Value.ToString(CultureInfo.InvariantCulture));
                    if (definition.Required) required.Add(number, () => double.IsNaN(number.Value) ? $"Enter {definition.Label}." : null);
                    reset.Add(() => number.Value = int.TryParse(definition.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : double.NaN);
                    target.Children.Add(number);
                    break;
                case ToolInputKind.FieldValues:
                    var fields = new PluginFieldValuesEditor(label, definition.DefaultValue, value => vm.SetInput(definition.Id, value), required, definition.Required);
                    reset.Add(() => fields.Reset(definition.DefaultValue));
                    target.Children.Add(fields);
                    break;
                default:
                    var multiline = definition.Kind == ToolInputKind.Multiline;
                    var text = new TextBox { Header = label, Text = definition.DefaultValue, AcceptsReturn = multiline,
                        TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 100 : 32, MaxHeight = 280, MaxLength = 1048576 };
                    text.TextChanged += (_, _) => vm.SetInput(definition.Id, text.Text);
                    if (definition.Required) required.Require(text, $"Enter {definition.Label}.");
                    reset.Add(() => text.Text = definition.DefaultValue);
                    target.Children.Add(text);
                    break;
            }
            if (definition.Description.Length > 0) target.Children.Add(Ui.Text(definition.Description, 12));
        }
        if (advanced.Children.Count > 0) inputs.Children.Add(new Expander
        {
            Header = "Optional inputs", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        });
        var run = Ui.Primary(Ui.AsyncButton(tool.ActionLabel, () => required.RunAsync(vm.ExecuteAsync)));
        var clear = Ui.Button("Clear inputs", () => { if (vm.IsBusy) return; vm.Clear(); foreach (var action in reset) action(); required.Clear(); });
        var cancel = Ui.Button("Cancel", vm.Cancel);
        var inputHost = new ContentControl { Content = inputs, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var controls = ToolLayout.Stack(inputHost, Ui.Row(run, clear, cancel), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        var results = new PluginToolResultView();
        void Refresh()
        {
            run.IsEnabled = clear.IsEnabled = !vm.IsBusy;
            inputHost.IsEnabled = !vm.IsBusy;
            cancel.IsEnabled = vm.IsBusy;
            cancel.Visibility = vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Result)) results.Show(vm.Result); Refresh(); };
        results.Show(vm.Result);
        Refresh();
        return ToolLayout.Page(tool.Name, tool.Description,
            ToolLayout.Workspace(vm, ToolLayout.Section("Inputs", controls), ToolLayout.Section("Results", results), () => vm.Result is not null, nameof(vm.Result)),
            ToolLayout.Help(Ui.Text(tool.Help)));
    }
}
