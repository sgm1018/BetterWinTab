namespace BetterWinTab.Models;

/// <summary>
/// A named color scheme that can be applied quickly from the Settings panel.
/// </summary>
public class ThemePreset
{
    public string Name { get; set; } = "";
    public AppearanceSettings Appearance { get; set; } = new();
}
