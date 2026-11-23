using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Desktop.Controls;
using ProtoBench.PluginSdk;

namespace ProtoBench.Desktop.Features.Plugins;

/// <summary>Editable supporting fields, passed to portable sessions as a JSON object.</summary>
public sealed class PluginFieldValuesEditor : StackPanel
{
    private readonly StackPanel rows = ToolLayout.Stack();
    private readonly List<(TextBox Field, TextBox Value)> editors = [];
    private readonly Action<string> changed;
    private readonly RequiredInputs? validation;
    private readonly bool required;
    private bool resetting;

    public PluginFieldValuesEditor(string label, string initial, Action<string> changed, RequiredInputs? validation = null, bool required = false)
    {
        this.changed = changed;
        this.validation = validation;
        this.required = required;
        Spacing = 8;
        Children.Add(Ui.Text(label + (required ? " *" : "")));
        Children.Add(rows);
        Children.Add(Ui.Button("Add field", () => { Add("", ""); Notify(); }));
        Reset(initial);
    }

    public void Reset(string value)
    {
        resetting = true;
        try
        {
            foreach (var editor in editors) validation?.Remove(editor.Field);
            rows.Children.Clear(); editors.Clear();
            foreach (var field in ToolInputValues.ParseFieldValues(value)) Add(field.Key, field.Value);
            if (required && editors.Count == 0) Add("", "");
        }
        finally { resetting = false; }
    }

    private void Add(string field, string value)
    {
        var key = new TextBox { Header = "Field", Text = field, MaxLength = 256 };
        validation?.Require(key, "Enter a supporting field name.");
        var input = new TextBox { Header = "Value", Text = value, MaxLength = 1048576 };
        var pair = (key, input);
        var values = ToolLayout.Columns(120, key, input);
        var row = ToolLayout.Stack(values);
        row.Children.Add(Ui.Button("Remove field", () =>
        {
            validation?.Remove(key); editors.Remove(pair); rows.Children.Remove(row);
            if (required && editors.Count == 0) Add("", "");
            Notify();
        }));
        editors.Add(pair); rows.Children.Add(row);
        key.TextChanged += (_, _) => Notify();
        input.TextChanged += (_, _) => Notify();
    }

    private void Notify()
    {
        if (resetting) return;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            // Preserve duplicate/blank names for workspace validation rather than silently losing a row.
            foreach (var (field, value) in editors) writer.WriteString(field.Text, value.Text);
            writer.WriteEndObject();
        }
        changed(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
