using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace ProtoBench.Desktop.Controls;

/// <summary>Hosts a native draggable thumb with the horizontal resize cursor.</summary>
public sealed class ColumnResizeHandle : ContentControl
{
    public ColumnResizeHandle()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
    }
}
