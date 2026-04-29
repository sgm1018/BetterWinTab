using Microsoft.UI.Xaml.Data;

namespace BetterWinTab.Converters;

/// <summary>
/// Returns Visible when the string is non-empty, Collapsed when empty or null.
/// Used to hide the TitlePrefix and its separator dot when there is no prefix.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string s && !string.IsNullOrEmpty(s)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
