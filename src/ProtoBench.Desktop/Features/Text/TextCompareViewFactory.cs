using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Text;

public sealed class TextCompareViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "text-compare";

    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (TextCompareWorkspace)workspace;
        var original = new ComparisonEditor("Original text · removals in red", () => vm.Original, text => vm.Original = text);
        var modified = new ComparisonEditor("Modified text · additions in green", () => vm.Modified, text => vm.Modified = text);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Original)) original.SyncText();
            if (e.PropertyName == nameof(vm.Modified)) modified.SyncText();
            if (e.PropertyName != nameof(vm.Result)) return;
            original.Highlight(vm.Result, TextDifferenceKind.Removed);
            modified.Highlight(vm.Result, TextDifferenceKind.Added);
        };
        var panes = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        panes.Children.Add(original);
        panes.Children.Add(modified);
        bool? stacked = null;
        void Reflow()
        {
            var narrow = panes.ActualWidth < 620;
            if (stacked == narrow) return;
            stacked = narrow;
            panes.ColumnDefinitions.Clear();
            panes.RowDefinitions.Clear();
            panes.ColumnDefinitions.Add(new ColumnDefinition());
            panes.RowDefinitions.Add(new RowDefinition());
            if (narrow) panes.RowDefinitions.Add(new RowDefinition());
            else panes.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(modified, narrow ? 0 : 1);
            Grid.SetRow(modified, narrow ? 1 : 0);
        }
        panes.SizeChanged += (_, _) => Reflow();
        Reflow();
        var content = new Grid { RowSpacing = 8 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(Ui.Row(Ui.Primary(new Button { Content = "Compare text", Command = vm.CompareCommand }),
            new Button { Content = "Swap", Command = vm.SwapCommand }, new Button { Content = "Clear", Command = vm.ClearCommand },
            Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        var feedback = ToolLayout.Stack(Ui.BoundText(vm, nameof(vm.Error)), Ui.BoundText(vm, nameof(vm.Summary)));
        Grid.SetRow(feedback, 1); content.Children.Add(feedback);
        Grid.SetRow(panes, 2); content.Children.Add(panes);
        return ToolLayout.FittedPage(vm.Title, "Edit and compare both texts here. Red = removed; green = added.", content,
            ToolLayout.Help(Ui.Text("Paste the original and modified text, then select Compare text. Differences are highlighted directly in the editable inputs. Both panes fill the available space and scroll independently. Comparison is case-sensitive and includes whitespace. The editor uses a single paragraph mark for each line ending. Emoji and combining characters stay together. Each input supports up to 100,000 characters. Large changes may be highlighted as whole blocks. Editing either input clears the previous highlights; select Compare text to refresh them.")));
    }

    private sealed class ComparisonEditor : RichEditBox
    {
        private readonly Func<string> read;
        private readonly Action<string> write;
        private readonly ITextCharacterFormat normal;
        private bool updating;
        private string current = "";

        public ComparisonEditor(string title, Func<string> read, Action<string> write)
        {
            this.read = read;
            this.write = write;
            Header = title;
            AcceptsReturn = true;
            MaxLength = TextComparison.MaxCharacters;
            MinHeight = 0;
            TextWrapping = TextWrapping.NoWrap;
            FontFamily = new FontFamily("Consolas");
            VerticalAlignment = VerticalAlignment.Stretch;
            ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollMode(this, ScrollMode.Auto);
            ScrollViewer.SetHorizontalScrollMode(this, ScrollMode.Auto);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, title);
            normal = Document.GetDefaultCharacterFormat().GetClone();
            normal.Name = "Consolas";
            normal.Size = (float)FontSize;
            Document.SetDefaultCharacterFormat(normal);
            SyncText();
            TextChanged += (_, _) =>
            {
                if (updating) return;
                Document.GetText(TextGetOptions.None, out var text);
                // RichEdit always includes one final, implicit paragraph mark.
                if (text.EndsWith('\r')) text = text[..^1];
                if (text == current) return;
                current = text;
                write(text);
            };
        }

        public void SyncText()
        {
            var text = read().ReplaceLineEndings("\r");
            if (text == current) return;
            updating = true;
            try { Document.SetText(TextSetOptions.None, text); current = text; }
            finally { updating = false; }
        }

        public void Highlight(TextComparisonResult? result, TextDifferenceKind changed)
        {
            updating = true;
            Document.BatchDisplayUpdates();
            try
            {
                Document.GetRange(0, int.MaxValue).CharacterFormat = normal;
                Document.Selection.CharacterFormat = normal;
                if (result is null) return;
                var offset = 0;
                foreach (var difference in result.Differences)
                {
                    if (difference.Kind != TextDifferenceKind.Equal && difference.Kind != changed) continue;
                    // RichEdit positions count CRLF as one paragraph mark.
                    var length = difference.Text.ReplaceLineEndings("\r").Length;
                    if (difference.Kind == changed)
                    {
                        var format = Document.GetRange(offset, offset + length).CharacterFormat;
                        format.BackgroundColor = changed == TextDifferenceKind.Removed
                            ? Windows.UI.Color.FromArgb(255, 255, 210, 210) : Windows.UI.Color.FromArgb(255, 196, 240, 205);
                        format.ForegroundColor = Colors.Black;
                    }
                    offset += length;
                }
            }
            finally { Document.ApplyDisplayUpdates(); updating = false; }
        }
    }
}
