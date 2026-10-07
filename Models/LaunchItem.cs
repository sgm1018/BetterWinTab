namespace BetterWinTab.Models;

public enum LaunchItemKind
{
    App,
    Folder
}

/// <summary>
/// Represents a launchable item: an app shortcut found in the Start Menu or a folder on disk.
/// </summary>
public record LaunchItem(string Name, string ShortcutPath, LaunchItemKind Kind = LaunchItemKind.App);
