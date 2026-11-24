using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Workspaces;

namespace ProtoBench.Desktop.Services;

public static class ComparisonReportExport
{
    public static Button CreateButton(ToolWorkspace workspace, FileDialogService dialogs, Func<bool> canExport,
        Func<string> createReport, string fileName, TextBlock feedback)
    {
        var button = new Button { Content = "Export differences report…" };
        var exporting = false;
        void Refresh() => button.IsEnabled = !exporting && !workspace.IsBusy && canExport();
        workspace.PropertyChanged += (_, _) => { feedback.Text = ""; Refresh(); };
        button.Click += async (_, _) =>
        {
            exporting = true;
            Refresh();
            feedback.Text = "";
            try
            {
                // Capture the displayed result and filters before opening the save dialog.
                var report = createReport();
                var path = await dialogs.SaveAsync(fileName, ".html", "HTML differences report");
                if (path is null) return;
                await File.WriteAllTextAsync(path, report);
                feedback.Text = "Differences report exported.";
            }
            catch (Exception ex) { feedback.Text = $"Could not export report: {ex.Message}"; }
            finally { exporting = false; Refresh(); }
        };
        Refresh();
        return button;
    }
}
