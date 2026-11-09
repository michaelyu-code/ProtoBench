using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Core.Documents;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.PluginSdk;
using ProtoBench.PluginSdk.WinUI;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Plugins;

public sealed class PluginToolViewFactory(PluginTool tool, IToolkitPlugin plugin) : IWorkspaceViewFactory
{
    public string ToolId => tool.Id;
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (PluginToolWorkspace)workspace;
        if (plugin is ICustomToolViewProvider custom && custom.CreateView(tool.Id, vm.Session) is { } view) return view;
        var inputs = ToolLayout.Stack();
        var reset = new List<Action>();
        foreach (var definition in tool.Inputs)
        {
            switch (definition.Kind)
            {
                case ToolInputKind.Secret:
                    var secret = new PasswordBox { Header = definition.Label, Password = definition.DefaultValue, MaxLength = 1048576 };
                    secret.PasswordChanged += (_, _) => vm.Inputs[definition.Id] = secret.Password;
                    reset.Add(() => secret.Password = definition.DefaultValue);
                    inputs.Children.Add(secret);
                    break;
                case ToolInputKind.Choice:
                    var choice = new ComboBox { Header = definition.Label, ItemsSource = definition.Choices, SelectedItem = definition.DefaultValue, HorizontalAlignment = HorizontalAlignment.Stretch };
                    choice.SelectionChanged += (_, _) => vm.Inputs[definition.Id] = (string?)choice.SelectedItem ?? "";
                    reset.Add(() => choice.SelectedItem = definition.DefaultValue);
                    inputs.Children.Add(choice);
                    break;
                default:
                    var multiline = definition.Kind == ToolInputKind.Multiline;
                    var text = new TextBox { Header = definition.Label, Text = definition.DefaultValue, AcceptsReturn = multiline,
                        TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 100 : 32, MaxHeight = 280, MaxLength = 1048576 };
                    text.TextChanged += (_, _) => vm.Inputs[definition.Id] = text.Text;
                    reset.Add(() => text.Text = definition.DefaultValue);
                    inputs.Children.Add(text);
                    break;
            }
            if (definition.Description.Length > 0) inputs.Children.Add(Ui.Text(definition.Description, 12));
        }
        var run = Ui.AsyncButton(tool.ActionLabel, vm.ExecuteAsync);
        var clear = Ui.Button("Clear inputs", () => { if (vm.IsBusy) return; vm.Clear(); foreach (var action in reset) action(); });
        var cancel = Ui.Button("Cancel", vm.Cancel);
        var controls = ToolLayout.Stack(inputs, Ui.Row(run, clear, cancel), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        var output = new RawTextViewer { Header = "Result", MinHeight = 180, MaxHeight = 480, TextWrapping = TextWrapping.Wrap };
        output.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Output)) });
        var tree = new TreeView { MaxHeight = 360 };
        var copied = Ui.Text("");
        var results = ToolLayout.Stack(output, Ui.Button("Copy result", () =>
        {
            if (vm.Output.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copied.Text = "Copied"; }
            catch { copied.Text = "Clipboard unavailable. Select and copy the result manually."; }
        }), copied, tree);
        void Refresh()
        {
            run.IsEnabled = clear.IsEnabled = !vm.IsBusy;
            foreach (var input in inputs.Children.OfType<Control>()) input.IsEnabled = !vm.IsBusy;
            cancel.IsEnabled = vm.IsBusy;
            ParsedFieldTree.Show(tree, new ParsedRecord(0, "", vm.Fields));
            tree.Visibility = vm.Fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Output)) copied.Text = ""; Refresh(); };
        Refresh();
        return ToolLayout.Page(tool.Name, tool.Description,
            ToolLayout.Columns(340, ToolLayout.Section("Inputs", controls), ToolLayout.Section("Results", results)),
            ToolLayout.Help(Ui.Text(tool.Help)));
    }
}
