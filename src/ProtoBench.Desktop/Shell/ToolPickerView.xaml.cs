using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Tools;

namespace ProtoBench.Desktop.Shell;

public sealed partial class ToolPickerView : UserControl
{
    private readonly IToolRegistry registry;

    public ToolPickerView(IToolRegistry registry, Action<string> open)
    {
        this.registry = registry;
        InitializeComponent();
        Search.TextChanged += (_, _) => Filter();
        Search.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter && Tools.SelectedItem is ToolDescriptor tool)
            {
                args.Handled = true;
                open(tool.Id);
            }
        };
        Tools.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter && Tools.SelectedItem is ToolDescriptor tool)
            { args.Handled = true; open(tool.Id); }
        };
        Search.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Down && Tools.Items.Count > 0)
            { args.Handled = true; Tools.Focus(FocusState.Programmatic); }
        };
        Tools.ItemClick += (_, args) => { if (args.ClickedItem is ToolDescriptor tool) open(tool.Id); };
        Loaded += (_, _) => Search.Focus(FocusState.Programmatic);
        Filter();
    }

    public void Reset()
    {
        Search.Text = "";
        Search.Focus(FocusState.Programmatic);
    }

    private void Filter()
    {
        var matches = registry.Tools.Where(t => $"{t.Name} {t.Description} {t.Category.DisplayName()}"
            .Contains(Search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Category).ThenBy(t => t.Name).ToArray();
        Tools.ItemsSource = matches;
        Tools.SelectedIndex = matches.Length > 0 ? 0 : -1;
        Empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
