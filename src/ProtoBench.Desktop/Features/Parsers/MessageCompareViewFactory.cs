using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;

namespace ProtoBench.Desktop.Features.Parsers;

public sealed class MessageCompareViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "message-compare";

    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (MessageCompareWorkspace)workspace;
        var required = new RequiredInputs(vm);
        var spec = new ComboBox
        {
            Header = "Spec", ItemsSource = vm.Parsers, DisplayMemberPath = "Name", SelectedValuePath = "FormatId",
            HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select a parser spec"
        };
        spec.SetBinding(ComboBox.SelectedValueProperty, new Binding
        {
            Source = vm, Path = new PropertyPath(nameof(vm.SelectedFormatId)), Mode = BindingMode.TwoWay
        });
        var first = Ui.Input(vm, nameof(vm.Message1), "Message 1", true);
        var second = Ui.Input(vm, nameof(vm.Message2), "Message 2", true);
        foreach (var editor in new[] { first, second })
        {
            editor.MaxHeight = double.PositiveInfinity;
            editor.VerticalAlignment = VerticalAlignment.Stretch;
            ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        }
        required.Require(spec, "Select a parser spec.");
        required.Require(first, "Paste Message 1.");
        required.Require(second, "Paste Message 2.");
        var settings = ToolLayout.Stack(spec,
            Ui.Text("Paste one request in each box, or paste the request followed by its reply in each box. Both boxes must contain the same number of messages.", 13));
        var actions = ToolLayout.Stack(Ui.Row(Ui.Primary(required.Guard(new Button { Content = "Compare", Command = vm.CompareCommand })),
                Ui.Button("Clear", () => { vm.ClearCommand.Execute(null); required.Clear(); }),
                Ui.Button("Cancel", vm.Cancel), Ui.Progress(vm)), Ui.BoundText(vm, nameof(vm.Status)));
        var editors = new Grid { RowSpacing = 8 };
        editors.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editors.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editors.Children.Add(first);
        Grid.SetRow(second, 1); editors.Children.Add(second);
        var form = new Grid { RowSpacing = 8 };
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.Children.Add(settings);
        Grid.SetRow(editors, 1); form.Children.Add(editors);
        Grid.SetRow(actions, 2); form.Children.Add(actions);
        void ResizeInputs() => form.MinHeight = settings.ActualHeight + actions.ActualHeight + first.MinHeight + second.MinHeight + 24;
        settings.SizeChanged += (_, _) => ResizeInputs();
        actions.SizeChanged += (_, _) => ResizeInputs();
        var inputs = ToolLayout.FittedSection("Messages", form);
        var onlyDifferences = new CheckBox { Content = "Show only differences" };
        onlyDifferences.SetBinding(CheckBox.IsCheckedProperty, new Binding
        {
            Source = vm, Path = new PropertyPath(nameof(vm.DifferencesOnly)), Mode = BindingMode.TwoWay
        });
        CheckBox MessageFilter(string label, string property)
        {
            var box = new CheckBox { Content = label };
            box.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.TwoWay });
            return box;
        }
        var fieldSearch = new TextBox { PlaceholderText = "Search fields, e.g. 3, 39, 55", AcceptsReturn = true, MaxHeight = 100 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fieldSearch, "Search comparison fields");
        fieldSearch.TextChanged += (_, _) => vm.FieldSearch = fieldSearch.Text;
        var fieldChoices = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 320,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" HorizontalAlignment="Stretch">
                    <TextBlock Text="{Binding Label}" TextWrapping="Wrap" />
                  </CheckBox>
                </DataTemplate>
                """)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fieldChoices, "Fields to show in comparison");
        fieldChoices.SetBinding(ItemsControl.ItemsSourceProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.VisibleFields)) });
        var fieldPicker = new DropDownButton
        {
            Flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 320, Spacing = 8,
                    Children =
                    {
                        fieldSearch,
                        Ui.Text("Top-level fields come from the parsed messages. Separate searches with commas, semicolons or new lines, then tick fields or choose Show matching fields. Use Show subfields to show their decoded contents.", 13),
                        Ui.Button("Show matching fields", vm.SelectMatchingFields),
                        Ui.Row(Ui.Button("Select all", () => vm.SelectAllFields(true)), Ui.Button("Clear selection", () => vm.SelectAllFields(false))),
                        fieldChoices
                    }
                }
            }
        };
        fieldPicker.SetBinding(ContentControl.ContentProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.FieldFilterLabel)) });
        ToolTipService.SetToolTip(fieldPicker, "Choose top-level fields to show in both requests and replies. Their subfields follow Show subfields.");
        var list = new ListView { SelectionMode = ListViewSelectionMode.None, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Field and subfield comparison");
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.VisibleRows)) });
        list.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="HorizontalContentAlignment" Value="Stretch" />
              <Setter Property="Padding" Value="0" />
            </Style>
            """);
        var templates = new ResourceDictionary { Source = new Uri("ms-appx:///Features/Parsers/MessageCompareTemplates.xaml") };
        list.ItemTemplate = (DataTemplate)templates["ComparisonTemplate"];
        var results = new Grid { RowSpacing = 8 };
        results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        results.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        results.Children.Add(ToolLayout.Stack(Ui.BoundText(vm, nameof(vm.Summary)),
            Ui.Row(MessageFilter("Request", nameof(vm.ShowRequest)), MessageFilter("Reply", nameof(vm.ShowReply)),
                onlyDifferences, MessageFilter("Show subfields", nameof(vm.ShowSubfields))),
            fieldPicker,
            Ui.Text("Yellow edge = changed · Purple edge = missing. Red / green marks the differing text span.", 13)));
        var diagnostics = new Expander
        {
            Header = "Parser diagnostics", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer { Content = Ui.BoundText(vm, nameof(vm.Diagnostics)), MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        void RefreshDiagnostics() => diagnostics.Visibility = vm.Diagnostics.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Diagnostics)) RefreshDiagnostics(); };
        RefreshDiagnostics();
        Grid.SetRow(diagnostics, 1); results.Children.Add(diagnostics);
        Grid.SetRow(list, 2); results.Children.Add(list);
        return ToolLayout.FittedPage(vm.Title, "Compare requests or request-and-reply pairs using the same spec, including every decoded subfield.",
            ToolLayout.Workspace(vm, inputs, ToolLayout.FittedSection("Comparison", results), () => vm.Rows.Count > 0, nameof(vm.Rows), 1.6),
            ToolLayout.Help(Ui.Text("Paste one request into each box, or paste a request followed by its reply into each box, select the spec and choose Compare. Use the message boundaries expected by the spec. Messages are compared in pasted order: first against first (Request), then second against second (Reply). Paired results are labeled Request / Reply. Both boxes must contain the same number of messages, with at most two per box. Fields are matched by spec ID or name within their parent; repeated fields are matched by occurrence. Values are compared exactly, including case and whitespace, after the spec's parsing and decoding. Missing fields differ from empty values. Parent fields are marked changed when a subfield differs. Parser diagnostics appear above the results; review them before relying on a comparison. Editing either message or changing the spec clears the previous result.")));
    }
}
