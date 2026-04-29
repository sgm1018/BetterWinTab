using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace BetterWinTab.Models;

/// <summary>
/// Represents a folder/category that groups windows together.
/// </summary>
public class WindowFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Folder";
    public string Icon { get; set; } = "\uE8B7"; // Folder icon
    public string BackgroundColor { get; set; } = ""; // Empty = no background tint
    public FolderType Type { get; set; } = FolderType.Manual;

    /// <summary>
    /// For smart folders: the process name to filter by (e.g., "Code", "chrome").
    /// Legacy single-process filter — kept for backward compatibility.
    /// </summary>
    public string? ProcessFilter { get; set; }

    /// <summary>
    /// For smart folders: the window class name to filter by.
    /// Legacy single-class filter — kept for backward compatibility.
    /// </summary>
    public string? ClassNameFilter { get; set; }

    /// <summary>
    /// Composite rule group for SmartRules folders (v2).
    /// Multiple conditions combined with AND/OR logic.
    /// </summary>
    public FolderRuleGroup? Rules { get; set; }

    /// <summary>
    /// Manually assigned window handles (stored as IntPtr values).
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<WindowInfo> Windows { get; set; } = new();

    /// <summary>
    /// Persistent collection of handles assigned manually to this folder.
    /// Used to survive window refreshes.
    /// </summary>
    [JsonIgnore]
    public HashSet<IntPtr> ManualWindowHandles { get; set; } = new();

    /// <summary>
    /// Sort order for display.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Returns a human-readable summary of this folder's filtering logic.
    /// </summary>
    public string GetFilterSummary()
    {
        return Type switch
        {
            FolderType.All => "All open windows",
            FolderType.SmartProcess => $"Process: {ProcessFilter}",
            FolderType.SmartClass => $"Class: {ClassNameFilter}",
            FolderType.SmartRules => Rules != null && Rules.Conditions.Count > 0
                ? $"Rules ({Rules.Operator}): {string.Join(", ", Rules.Conditions.Select(c => c.ToString()))}"
                : "Smart rules (empty)",
            FolderType.Manual => "Custom folder",
            FolderType.Clipboard => "Clipboard history",
            FolderType.RecycleBin => "Recycle Bin",
            _ => ""
        };
    }
}

public enum FolderType
{
    /// <summary>All windows (default folder).</summary>
    All,
    /// <summary>User manually assigns windows.</summary>
    Manual,
    /// <summary>Auto-filters by process name.</summary>
    SmartProcess,
    /// <summary>Auto-filters by window class.</summary>
    SmartClass,
    /// <summary>Composite rule-based smart folder (v2).</summary>
    SmartRules,
    /// <summary>Special clipboard history folder.</summary>
    Clipboard,
    /// <summary>Special recycle bin folder — opens the Windows Recycle Bin.</summary>
    RecycleBin
}
