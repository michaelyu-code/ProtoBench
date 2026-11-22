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
        var inputs = ToolLayout.Stack();
        var advanced = ToolLayout.Stack();
        var reset = new List<Action>();
        foreach (var definition in tool.Inputs)
        {
            var target = definition.IsAdvanced ? advanced : inputs;
            var label = definition.Label + (definition.Required ? " *" : "");
            switch (definition.Kind)
            {
                case ToolInputKind.Secret:
                    var secret = new PasswordBox { Header = label, Password = definition.DefaultValue, MaxLength = 1048576 };
                    secret.PasswordChanged += (_, _) => vm.SetInput(definition.Id, secret.Password);
                    reset.Add(() => secret.Password = definition.DefaultValue);
                    target.Children.Add(secret);
                    break;
                case ToolInputKind.Choice:
                    var choice = new ComboBox { Header = label, ItemsSource = definition.Choices, SelectedItem = definition.DefaultValue, HorizontalAlignment = HorizontalAlignment.Stretch };
                    choice.SelectionChanged += (_, _) => vm.SetInput(definition.Id, (string?)choice.SelectedItem ?? "");
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
                    reset.Add(() => number.Value = int.TryParse(definition.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : double.NaN);
                    target.Children.Add(number);
                    break;
                case ToolInputKind.FieldValues:
                    var fields = new PluginFieldValuesEditor(label, definition.DefaultValue, value => vm.SetInput(definition.Id, value));
                    reset.Add(() => fields.Reset(definition.DefaultValue));
                    target.Children.Add(fields);
                    break;
                default:
                    var multiline = definition.Kind == ToolInputKind.Multiline;
                    var text = new TextBox { Header = label, Text = definition.DefaultValue, AcceptsReturn = multiline,
                        TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 100 : 32, MaxHeight = 280, MaxLength = 1048576 };
                    text.TextChanged += (_, _) => vm.SetInput(definition.Id, text.Text);
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
        var run = Ui.Primary(Ui.AsyncButton(tool.ActionLabel, vm.ExecuteAsync));
        var clear = Ui.Button("Clear inputs", () => { if (vm.IsBusy) return; vm.Clear(); foreach (var action in reset) action(); });
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
