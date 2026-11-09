using Microsoft.UI.Xaml.Controls;

namespace ProtoBench.Desktop.Shell;

public sealed partial class ShellHeader : UserControl
{
    public TitleBar Bar => AppTitleBar;
    public ShellHeader() => InitializeComponent();
}
