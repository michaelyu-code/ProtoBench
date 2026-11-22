using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Text;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Text;

public sealed class TextCleanupViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "text-cleanup";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (TextCleanupWorkspace)workspace;
        CheckBox Option(string label, string property)
        {
            var box = new CheckBox { Content = Ui.Text(label) };
            box.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.TwoWay });
            return box;
        }
        var options = ToolLayout.Columns(170,
            Option("Spaces", nameof(vm.Spaces)), Option("Tabs", nameof(vm.Tabs)),
            Option("Line endings", nameof(vm.LineEndings)), Option("All whitespace", nameof(vm.AllWhitespace)),
            Option("Digits", nameof(vm.Digits)), Option("Punctuation", nameof(vm.Punctuation)),
            Option("Other control characters", nameof(vm.ControlCharacters)));
        options.RowSpacing = 0;
        var custom = Ui.Input(vm, nameof(vm.CustomCharacters), "Also remove these characters (literal, not a pattern)");
        custom.MaxLength = 4096;
        var input = Ui.Input(vm, nameof(vm.Input), "Text to clean", true);
        input.MaxLength = TextCleanup.MaxCharacters;
        input.MaxHeight = double.PositiveInfinity;
        input.VerticalAlignment = VerticalAlignment.Stretch;
        var settings = ToolLayout.Stack(options, custom);
        var actions = ToolLayout.Stack(Ui.Row(
            Ui.Primary(new Button { Content = "Clean text", Command = vm.CleanCommand }),
            new Button { Content = "Clear", Command = vm.ClearCommand }),
            Ui.BoundText(vm, nameof(vm.Error)), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        var form = new Grid { RowSpacing = 8 };
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.Children.Add(settings);
        Grid.SetRow(input, 1); form.Children.Add(input);
        Grid.SetRow(actions, 2); form.Children.Add(actions);
        void Resize() => form.MinHeight = settings.ActualHeight + actions.ActualHeight + 96;
        settings.SizeChanged += (_, _) => Resize();
        actions.SizeChanged += (_, _) => Resize();
        var result = new RawTextViewer { TextWrapping = TextWrapping.Wrap };
        result.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Output)) });
        var copied = Ui.Text("");
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.HasResult)) copied.Text = ""; };
        var copy = Ui.Button("Copy result", () =>
        {
            if (!vm.HasResult) return;
            try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copied.Text = "Copied"; }
            catch { copied.Text = "Clipboard unavailable. Select and copy the result manually."; }
        });
        return ToolLayout.Page(vm.Title, "Remove selected characters from pasted text.",
            ToolLayout.Workspace(vm, ToolLayout.FittedSection("Remove", form),
                ToolLayout.TextResult("Cleaned text", result, copy, copied, aboveResult: Ui.BoundText(vm, nameof(vm.Summary))),
                () => vm.HasResult, nameof(vm.HasResult)),
            ToolLayout.Help(Ui.Text("Spaces removes ordinary spaces. Line endings removes CRLF, CR, LF and Unicode line separators. All whitespace also removes tabs and Unicode spacing. Other control characters removes controls such as NUL and DEL while keeping tabs and line endings unless those options are selected. Digits and punctuation include Unicode characters. The custom list removes each character literally, including emoji; it is not a regular expression. Your original input stays unchanged. Maximum 1,000,000 characters.")));
    }
}
