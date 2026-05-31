using Microsoft.UI.Xaml.Data;
using Windows.UI;

namespace BetterWinTab.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#39FF14") to a Windows.UI.Color.
/// Used to set a ColorPicker's Color property from a hex string.
/// </summary>
public class HexColorToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = System.Convert.ToByte(hex[..2], 16);
                    byte g = System.Convert.ToByte(hex[2..4], 16);
                    byte b = System.Convert.ToByte(hex[4..6], 16);
                    return Color.FromArgb(255, r, g, b);
                }
                if (hex.Length == 8)
                {
                    byte a = System.Convert.ToByte(hex[..2], 16);
                    byte r = System.Convert.ToByte(hex[2..4], 16);
                    byte g = System.Convert.ToByte(hex[4..6], 16);
                    byte b = System.Convert.ToByte(hex[6..8], 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"HexColorToColorConverter: {ex.Message}"); }
        }
        return Color.FromArgb(255, 40, 40, 40);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
