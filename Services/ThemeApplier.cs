using BetterWinTab.Models;

namespace BetterWinTab.Services;

public static class ThemeApplier
{
    public static void Apply(AppearanceSettings a)
    {
        var res = Application.Current.Resources;

        var folderHover = ResolveHover(a.FolderHoverColor, a.AccentColor, 0x1A);
        var folderSel = ResolveHover(a.FolderSelectedColor, a.AccentColor, 0x29);
        var winHoverBdr = ResolveHover(a.WindowHoverBorderColor, a.AccentColor, 0x66);
        var winHoverBg = ResolveHover(a.WindowHoverBackgroundColor, a.AccentColor, 0x0C);

        SetBrush("NeonGreenBrush", a.AccentColor);
        SetBrush("NeonGreenDimBrush", a.AccentDimColor);
        SetBrush("NeonGreenSubtleBrush", a.AccentSubtleColor);
        SetBrush("PureBlackBrush", a.BackgroundColor);
        SetBrush("DarkSurfaceBrush", a.SurfaceColor);
        SetBrush("CardSurfaceBrush", a.CardColor);
        SetBrush("BorderDarkBrush", a.BorderColor);
        SetBrush("TextPrimaryBrush", a.TextPrimaryColor);
        SetBrush("TextSecondaryBrush", a.TextSecondaryColor);
        SetBrush("TextMutedBrush", a.TextMutedColor);
        SetBrush("DangerRedBrush", a.DangerColor);
        SetBrush("FolderHoverBrush", folderHover);
        SetBrush("FolderSelectedBrush", folderSel);
        SetBrush("WindowHoverBorderBrush", winHoverBdr);
        SetBrush("WindowHoverBackgroundBrush", winHoverBg);
        SetColor("NeonGreen", a.AccentColor);
        SetColor("NeonGreenDim", a.AccentDimColor);
        SetColor("NeonGreenSubtle", a.AccentSubtleColor);
        SetColor("PureBlack", a.BackgroundColor);
        SetColor("DarkSurface", a.SurfaceColor);
        SetColor("CardSurface", a.CardColor);
        SetColor("BorderDark", a.BorderColor);
        SetColor("TextPrimary", a.TextPrimaryColor);
        SetColor("TextSecondary", a.TextSecondaryColor);
        SetColor("TextMuted", a.TextMutedColor);
        SetColor("DangerRed", a.DangerColor);
        SetColor("FolderHover", folderHover);
        SetColor("FolderSelected", folderSel);
        SetColor("WindowHoverBorder", winHoverBdr);
        SetColor("WindowHoverBackground", winHoverBg);
    }

    public static Windows.UI.Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte alpha = 255, r, g, b;
        if (hex.Length == 8) { alpha = System.Convert.ToByte(hex[..2], 16); r = System.Convert.ToByte(hex[2..4], 16); g = System.Convert.ToByte(hex[4..6], 16); b = System.Convert.ToByte(hex[6..8], 16); }
        else if (hex.Length == 6) { r = System.Convert.ToByte(hex[..2], 16); g = System.Convert.ToByte(hex[2..4], 16); b = System.Convert.ToByte(hex[4..6], 16); }
        else return Windows.UI.Color.FromArgb(255, 0, 0, 0);
        return Windows.UI.Color.FromArgb(alpha, r, g, b);
    }

    public static string ResolveHoverFromAccent(string hoverHex, string accentHex, byte alpha)
    {
        if (string.IsNullOrEmpty(hoverHex)) return $"#{alpha:X2}{accentHex.TrimStart('#').ToUpperInvariant()}";
        var accentRgb = accentHex.TrimStart('#');
        if (accentRgb.Length == 8) accentRgb = accentRgb[2..];
        var hoverRgb = hoverHex.TrimStart('#');
        if (hoverRgb.Length == 8) hoverRgb = hoverRgb[2..];
        if (hoverRgb.Equals("39FF14", StringComparison.OrdinalIgnoreCase) &&
            !accentRgb.Equals("39FF14", StringComparison.OrdinalIgnoreCase))
            return $"#{alpha:X2}{accentRgb.ToUpperInvariant()}";
        return hoverHex;
    }

    private static string ResolveHover(string hoverHex, string accentHex, byte alpha)
    {
        if (string.IsNullOrEmpty(hoverHex)) return hoverHex;
        var accentRgb = accentHex.TrimStart('#');
        if (accentRgb.Length == 8) accentRgb = accentRgb[2..];
        var hoverRgb = hoverHex.TrimStart('#');
        if (hoverRgb.Length == 8) hoverRgb = hoverRgb[2..];
        if (hoverRgb.Equals("39FF14", StringComparison.OrdinalIgnoreCase) &&
            !accentRgb.Equals("39FF14", StringComparison.OrdinalIgnoreCase))
            return $"#{alpha:X2}{accentRgb.ToUpperInvariant()}";
        return hoverHex;
    }

    private static void SetBrush(string key, string hex)
    {
        var res = Application.Current.Resources;
        if (!string.IsNullOrEmpty(hex) && res.ContainsKey(key))
            if (res[key] is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
                brush.Color = ParseHex(hex);
    }

    private static void SetColor(string key, string hex)
    {
        var res = Application.Current.Resources;
        if (!string.IsNullOrEmpty(hex) && res.ContainsKey(key))
            res[key] = ParseHex(hex);
    }
}
