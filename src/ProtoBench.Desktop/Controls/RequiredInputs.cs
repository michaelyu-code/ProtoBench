using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ProtoBench.Desktop.Controls;

/// <summary>Shared, action-scoped required-field feedback. Optional inputs remain optional.</summary>
public sealed class RequiredInputs
{
    private readonly List<Field> fields = [];

    public RequiredInputs(INotifyPropertyChanged source) => source.PropertyChanged += (_, _) => Refresh();

    public void Require(Control control, string message, Func<bool>? when = null) =>
        Add(control, () => string.IsNullOrWhiteSpace(control switch
        {
            TextBox text => text.Text,
            PasswordBox password => password.Password,
            ComboBox combo => combo.SelectedItem?.ToString(),
            _ => throw new ArgumentException("Unsupported required input control.")
        }) ? message : null, when);

    public void Add(Control control, Func<string?> error, Func<bool>? when = null)
    {
        var field = new Field(control, error, when ?? (() => true));
        fields.Add(field);
        field.Refresh();
    }

    public void Refresh() { foreach (var field in fields) field.Refresh(); }
    public void Clear() { foreach (var field in fields) field.Reset(); }
    public void Remove(Control control)
    {
        foreach (var field in fields.Where(f => ReferenceEquals(f.Control, control)).ToArray())
        {
            field.Reset();
            fields.Remove(field);
        }
    }

    public bool Validate() => ValidateFields(fields);
    public bool Validate(Control control) => ValidateFields(fields.Where(f => ReferenceEquals(f.Control, control)).ToArray());

    private bool ValidateFields(IEnumerable<Field> selected)
    {
        var targets = selected.ToArray();
        foreach (var field in targets) field.Attempted = true;
        Refresh();
        var first = targets.FirstOrDefault(f => f.HasError);
        if (first is null) return true;
        first.Control.StartBringIntoView();
        first.Control.Focus(FocusState.Programmatic);
        return false;
    }

    public Button Guard(Button button)
    {
        var command = button.Command ?? throw new ArgumentException("A command is required.");
        button.Command = new RelayCommand<object?>(parameter =>
        {
            if (Validate() && command.CanExecute(parameter)) command.Execute(parameter);
        }, command.CanExecute);
        command.CanExecuteChanged += (_, _) => ((IRelayCommand)button.Command).NotifyCanExecuteChanged();
        return button;
    }

    public void Run(Action action) { if (Validate()) action(); }
    public Task RunAsync(Func<Task> action) => Validate() ? action() : Task.CompletedTask;

    private sealed class Field
    {
        public Control Control { get; }
        public bool Attempted { get; set; }
        public bool HasError { get; private set; }
        private readonly Func<string?> error;
        private readonly Func<bool> required;
        private readonly TextBlock message;
        private readonly object border, description, headerTemplate;
        private readonly string help;
        private readonly DependencyProperty headerTemplateProperty, descriptionProperty;
        private readonly DataTemplate requiredTemplate;
        private bool refreshing;

        public Field(Control control, Func<string?> error, Func<bool> required)
        {
            Control = control; this.error = error; this.required = required;
            (headerTemplateProperty, descriptionProperty, var valueProperty) = control switch
            {
                TextBox => (TextBox.HeaderTemplateProperty, TextBox.DescriptionProperty, TextBox.TextProperty),
                PasswordBox => (PasswordBox.HeaderTemplateProperty, PasswordBox.DescriptionProperty, PasswordBox.PasswordProperty),
                ComboBox => (ComboBox.HeaderTemplateProperty, ComboBox.DescriptionProperty, Microsoft.UI.Xaml.Controls.Primitives.Selector.SelectedItemProperty),
                NumberBox => (NumberBox.HeaderTemplateProperty, NumberBox.DescriptionProperty, NumberBox.ValueProperty),
                _ => throw new ArgumentException("Unsupported required input control.")
            };
            border = control.ReadLocalValue(Microsoft.UI.Xaml.Controls.Control.BorderBrushProperty);
            description = control.ReadLocalValue(descriptionProperty);
            headerTemplate = control.ReadLocalValue(headerTemplateProperty);
            help = AutomationProperties.GetHelpText(control);
            requiredTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <StackPanel Orientation="Horizontal" Spacing="4">
                        <TextBlock Text="{Binding}" TextWrapping="Wrap" />
                        <TextBlock Text="*" AutomationProperties.Name="Required" />
                    </StackPanel>
                </DataTemplate>
                """);
            message = (TextBlock)XamlReader.Load("""
                <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Foreground="{ThemeResource SystemFillColorCriticalBrush}" TextWrapping="Wrap" />
                """);
            AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
            control.RegisterPropertyChangedCallback(valueProperty, (_, _) => Refresh());
            control.ActualThemeChanged += (_, _) => Refresh();
        }

        public void Reset() { Attempted = false; Refresh(); }

        public void Refresh()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                var active = required();
                if (active) Control.SetValue(headerTemplateProperty, requiredTemplate);
                else Restore(headerTemplateProperty, headerTemplate);
                var issue = active && Attempted ? error() : null;
                HasError = !string.IsNullOrEmpty(issue);
                message.Text = issue ?? "";
                if (HasError)
                {
                    Control.SetValue(descriptionProperty, message);
                    Control.BorderBrush = message.Foreground;
                }
                else
                {
                    Restore(descriptionProperty, description);
                    Restore(Microsoft.UI.Xaml.Controls.Control.BorderBrushProperty, border);
                }
                AutomationProperties.SetHelpText(Control, HasError ? issue! : active ? "Required. " + help : help);
            }
            finally { refreshing = false; }
        }

        private void Restore(DependencyProperty property, object value)
        {
            if (value == DependencyProperty.UnsetValue) Control.ClearValue(property);
            else Control.SetValue(property, value);
        }
    }
}
