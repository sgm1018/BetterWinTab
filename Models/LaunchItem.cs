namespace BetterWinTab.Models;

/// <summary>
/// Represents a launchable item (app shortcut, executable) found in the Start Menu.
/// </summary>
public record LaunchItem(string Name, string ShortcutPath);
