using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BetterWinTab.Converters;

/// <summary>
/// Converts a <see cref="bool"/> to <see cref="Visibility"/>:
/// <c>true</c> → <see cref="Visibility.Visible"/>,
/// <c>false</c> → <see cref="Visibility.Collapsed"/>.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>
/// Inverse: <c>false</c> → <see cref="Visibility.Visible"/>,
/// <c>true</c> → <see cref="Visibility.Collapsed"/>.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not Visibility.Visible;
}
