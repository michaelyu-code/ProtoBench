using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NonStopToolkit.Application.Tools;

namespace NonStopToolkit.Desktop.Shell;

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
            if (args.Key == Windows.System.VirtualKey.Enter && Tools.Items.Count == 1 && Tools.Items[0] is ToolDescriptor tool)
            {
                args.Handled = true;
                open(tool.Id);
            }
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
        Empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
