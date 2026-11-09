using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Desktop.Controls;
using NonStopToolkit.Desktop.Services;
namespace NonStopToolkit.Desktop.Features.Generators;

public sealed class GeneratorViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "file-generator";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (GeneratorWorkspace)workspace;
        var picker = new ComboBox { Header = "Template", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = vm.Generators, DisplayMemberPath = "Name" };
        var hint = Ui.Text("");
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is NonStopToolkit.Generation.Abstractions.IFileGenerator g) { vm.GeneratorId = g.GeneratorId; hint.Text = "Fields: " + string.Join(", ", g.InputNames); } };
        if (vm.Generators.Count > 0)
            picker.SelectedIndex = 0;
        var hex = new HexViewer { Header = "Exact output bytes (hex)" };
        var text = new RawTextViewer { Header = "Text preview (UTF-8 / ASCII)" };
        var size = Ui.Text("0 bytes");
        var error = Ui.Text("");
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == "Preview") { hex.Show(vm.Preview); text.Text = System.Text.Encoding.UTF8.GetString(vm.Preview); size.Text = $"{vm.Preview.Length:N0} bytes"; } };
        var input = Ui.Input(vm, "Inputs", "Input values · JSON object of strings", true);
        input.MinHeight = 200;
        var previews = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent };
        previews.TabItems.Add(new TabViewItem { Header = "Text", IsClosable = false, Content = text });
        previews.TabItems.Add(new TabViewItem { Header = "Hex bytes", IsClosable = false, Content = hex });
        previews.SelectedIndex = 0;
        return ToolLayout.Page("File Generator", "Validate, preview and save fixed-width records.",
            ToolLayout.Columns(340,
                ToolLayout.Section("Inputs", picker, hint, input,
                    Ui.Row(Ui.AsyncButton("Preview", vm.PreviewAsync, ex => error.Text = ex.Message), Ui.AsyncButton("Save file…", async () => { var path = await dialogs.SaveAsync(); if (path is not null) await vm.SaveAsync(path); }, ex => error.Text = ex.Message), Ui.Button("Cancel", vm.Cancel)),
                    error, Ui.BoundText(vm, "Diagnostics"), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, "Status"))),
                ToolLayout.Section("Preview", size, previews)),
            ToolLayout.Help(Ui.Text("All fields are validated before bytes are written. Lengths are measured in encoded bytes. Switch between Text and Hex bytes to inspect the same generated output.")));
    }
}
