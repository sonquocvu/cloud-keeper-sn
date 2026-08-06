using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CloudKeeperSN.App.UI.Converters;

public sealed class NavigationWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && width < 1180 ? new GridLength(80) : new GridLength(248);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CompactVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var compact = value is double width && width < 1180;
        var showWhenCompact = string.Equals(parameter as string, "Compact", StringComparison.Ordinal);
        return compact == showWhenCompact ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

