using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ProtoBench.Desktop.Features.Parsers;

public sealed class ParserVerticalInsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var vertical = value is double number ? number : 0;
        var horizontal = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var padding) ? padding : 0;
        return new Thickness(horizontal, vertical, horizontal, vertical);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class ParserBooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (parameter?.ToString() == "Invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
