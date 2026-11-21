using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Workspaces;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Controls;

/// <summary>Copyable named values projected from the tool's existing text result.</summary>
public static class ResultValues
{
    public static FrameworkElement Create(ToolWorkspace source, Func<string> output, params string[] labels)
    {
        var panel = ToolLayout.Stack();
        foreach (var label in labels)
        {
            var value = new TextBox { Header = label, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"), AcceptsReturn = true, MaxHeight = 120 };
            var feedback = Ui.Text("");
            var copy = Ui.Button("Copy " + label.ToLowerInvariant(), () =>
            {
                if (value.Text.Length == 0) return;
                try { var data = new DataPackage(); data.SetText(value.Text); Clipboard.SetContent(data); feedback.Text = "Copied"; }
                catch { feedback.Text = "Select the value and copy manually."; }
            });
            var row = ToolLayout.Stack(value, Ui.Row(copy, feedback));
            void Refresh()
            {
                value.Text = output().Split('\n').FirstOrDefault(line => line.StartsWith(label + ":", StringComparison.Ordinal))?[(label.Length + 1)..].Trim() ?? "";
                row.Visibility = value.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                feedback.Text = "";
            }
            source.PropertyChanged += (_, e) => { if (e.PropertyName == "Output") Refresh(); };
            Refresh(); panel.Children.Add(row);
        }
        return panel;
    }
}
