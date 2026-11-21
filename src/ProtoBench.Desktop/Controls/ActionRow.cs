using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ProtoBench.Desktop.Controls;

/// <summary>Wraps actions using their measured size, including Windows text scaling.</summary>
public sealed class ActionRow : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Layout(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    private Size Layout(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, usedWidth = 0;
        foreach (var child in Children.Where(c => c.Visibility == Visibility.Visible))
        {
            var size = child.DesiredSize;
            var childWidth = Math.Min(width, size.Width);
            if (x > 0 && x + childWidth > width)
            {
                y += lineHeight + Spacing;
                x = lineHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, childWidth, size.Height));
            usedWidth = Math.Max(usedWidth, x + childWidth);
            x += childWidth + Spacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return new Size(usedWidth, y + lineHeight);
    }
}
