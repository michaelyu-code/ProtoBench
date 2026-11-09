using Microsoft.UI.Xaml.Controls;

namespace NonStopToolkit.Desktop.Shell;

public sealed partial class ShellHeader : UserControl
{
    public TitleBar Bar => AppTitleBar;
    public ShellHeader() => InitializeComponent();
}
