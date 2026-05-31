using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace BetterWinTab.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#2D2D3D") to a SolidColorBrush.
/// Used in DataTemplates where x:Bind can't directly convert strings to brushes.
/// </summary>
public class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                hex = hex.TrimStart('#');
                byte a = 255;
                byte r, g, b;

                if (hex.Length == 8)
                {
                    a = System.Convert.ToByte(hex[..2], 16);
                    r = System.Convert.ToByte(hex[2..4], 16);
                    g = System.Convert.ToByte(hex[4..6], 16);
                    b = System.Convert.ToByte(hex[6..8], 16);
                }
                else if (hex.Length == 6)
                {
                    r = System.Convert.ToByte(hex[..2], 16);
                    g = System.Convert.ToByte(hex[2..4], 16);
                    b = System.Convert.ToByte(hex[4..6], 16);
                }
                else
                {
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                }

                return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
            }
            catch
            {
                // Fall through to transparent default
            }
        }

        // Empty or null → fully transparent (represents "no color")
        return new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
