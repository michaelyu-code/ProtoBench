using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using System.Text.Json;
namespace ProtoBench.Desktop.Features.Generators;

public sealed class GeneratorViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "file-generator";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (GeneratorWorkspace)workspace;
        var required = new RequiredInputs(vm);
        var picker = new ComboBox { Header = "Template", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = vm.Generators, DisplayMemberPath = "Name" };
        required.Require(picker, "Select a generator template.");
        var hint = Ui.Text("");
        var hex = new HexViewer { Header = "Exact output bytes (hex)" };
        var text = new RawTextViewer { Header = "Text preview (UTF-8 / ASCII)" };
        var size = Ui.Text("0 bytes");
        var error = Ui.Text("");
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == "Preview") { hex.Show(vm.Preview); text.Text = System.Text.Encoding.UTF8.GetString(vm.Preview); size.Text = $"{vm.Preview.Length:N0} bytes"; } };
        var input = Ui.Input(vm, "Inputs", "Input values · JSON object of strings", true);
        input.MinHeight = 120;
        var fields = ToolLayout.Stack();
        var editor = new ComboBox { Header = "Edit as", ItemsSource = new[] { "Fields", "JSON" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var fieldEditors = new List<TextBox>();
        required.Add(input, () =>
        {
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(vm.Inputs);
                if (values is null || values.Values.Any(v => v is null)) return "Enter a JSON object of string values.";
                if (picker.SelectedItem is ProtoBench.Generation.Engines.FixedWidthGenerator generator)
                {
                    var missing = generator.RequiredInputNames.Where(name => string.IsNullOrWhiteSpace(values.GetValueOrDefault(name))).ToArray();
                    if (missing.Length > 0) return "Required values: " + string.Join(", ", missing) + ".";
                }
                return null;
            }
            catch (JsonException) { return "Enter a JSON object of string values."; }
        }, () => editor.SelectedIndex == 1);
        Dictionary<string, string>? ReadValues()
        {
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(vm.Inputs);
                if (values is null || values.Values.Any(v => v is null)) throw new JsonException();
                error.Text = "";
                return values;
            }
            catch (JsonException) { error.Text = "Enter a JSON object of string values before switching to Fields."; return null; }
        }
        void BuildFields()
        {
            var values = ReadValues();
            if (values is null) { editor.SelectedIndex = 1; return; }
            foreach (var old in fieldEditors) required.Remove(old);
            fieldEditors.Clear(); fields.Children.Clear();
            if (picker.SelectedItem is not ProtoBench.Generation.Abstractions.IFileGenerator generator) return;
            foreach (var name in generator.InputNames)
            {
                var field = new TextBox { Header = name, Text = values.GetValueOrDefault(name, ""), TextWrapping = TextWrapping.Wrap };
                field.TextChanged += (_, _) => { values[name] = field.Text; vm.Inputs = JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }); };
                fieldEditors.Add(field);
                if (generator is ProtoBench.Generation.Engines.FixedWidthGenerator fixedWidth && fixedWidth.RequiredInputNames.Contains(name))
                    required.Require(field, $"Enter a value for {name}.", () => editor.SelectedIndex == 0);
                fields.Children.Add(field);
            }
        }
        editor.SelectionChanged += (_, _) =>
        {
            if (editor.SelectedIndex == 0) BuildFields();
            fields.Visibility = editor.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            input.Visibility = editor.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            required.Refresh();
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not ProtoBench.Generation.Abstractions.IFileGenerator generator) return;
            vm.GeneratorId = generator.GeneratorId;
            hint.Text = "Fields: " + string.Join(", ", generator.InputNames);
            if (editor.SelectedIndex == 0) BuildFields();
        };
        if (vm.Generators.Count > 0) picker.SelectedIndex = 0;
        editor.SelectedIndex = 0;
        Button Save() => Ui.AsyncButton("Save file…", async () => { if (!required.Validate()) return; var path = await dialogs.SaveAsync(); if (path is not null) await vm.SaveAsync(path); }, ex => error.Text = ex.Message);
        var cancel = Ui.Button("Cancel", vm.Cancel);
        var preview = Ui.Primary(Ui.AsyncButton("Preview", () => required.RunAsync(vm.PreviewAsync), ex => error.Text = ex.Message));
        void UpdateBusy()
        {
            cancel.Visibility = vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            input.IsEnabled = picker.IsEnabled = editor.IsEnabled = !vm.IsBusy;
            foreach (var field in fields.Children.OfType<Control>()) field.IsEnabled = !vm.IsBusy;
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsBusy)) UpdateBusy(); };
        UpdateBusy();
        var previews = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent };
        previews.TabItems.Add(new TabViewItem { Header = "Text", IsClosable = false, Content = text });
        previews.TabItems.Add(new TabViewItem { Header = "Hex bytes", IsClosable = false, Content = hex });
        previews.SelectedIndex = 0;
        return ToolLayout.Page("File Generator", "Validate, preview and save fixed-width records.",
            ToolLayout.Workspace(vm,
                ToolLayout.Section("Inputs", picker, Ui.Row(preview, Save(), cancel), editor, fields, input,
                    new Expander { Header = "Template details", Content = hint, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch },
                    Ui.BoundText(vm, "Diagnostics"), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, "Status"))),
                ToolLayout.Section("Preview", size, previews, Save()), () => vm.Preview.Length > 0, nameof(vm.Preview)), error,
            ToolLayout.Help(Ui.Text("All fields are validated before bytes are written. Lengths are measured in encoded bytes. Switch between Text and Hex bytes to inspect the same generated output.")));
    }
}
