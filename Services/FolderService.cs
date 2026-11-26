using System.Collections.ObjectModel;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>
/// Manages window folders (categories) and their window assignments.
/// Handles both manual folders and smart (auto-filtered) folders.
/// </summary>
public class FolderService
{
    public ObservableCollection<WindowFolder> Folders { get; } = new();

    public FolderService()
    {
        InitializeDefaultFolders();
    }

    private void InitializeDefaultFolders()
    {
        // "All Windows" is always the first folder
        Folders.Add(new WindowFolder
        {
            Name = "All Windows",
            Icon = "\uE71D", // Globe/All icon
            Type = FolderType.All,
            SortOrder = 0
        });
    }

    /// <summary>
    /// Refreshes the windows in all folders based on their filter rules.
    /// </summary>
    public void RefreshAllFolders(IReadOnlyList<WindowInfo> windows, IReadOnlyList<WindowInfo> browserTabs)
    {
        foreach (var folder in Folders)
        {
            RefreshFolder(folder, windows, browserTabs);
        }
    }

    /// <summary>
    /// Refreshes a single folder with the current list of windows.
    /// </summary>
    private static void RefreshFolder(
        WindowFolder folder, IReadOnlyList<WindowInfo> windows, IReadOnlyList<WindowInfo> browserTabs)
    {
        folder.Windows.Clear();

        // Special workspaces don't contain windows.
        if (folder.Type is FolderType.Clipboard or FolderType.RecycleBin or FolderType.Notes or FolderType.Calls or FolderType.Ssh)
            return;

        IEnumerable<WindowInfo> filtered = folder.Type switch
        {
            FolderType.All => windows.Concat(browserTabs),
            FolderType.SmartProcess => windows
                .Where(w => w.ProcessName.Equals(folder.ProcessFilter, StringComparison.OrdinalIgnoreCase)),
            FolderType.SmartClass => windows
                .Where(w => w.ClassName.Equals(folder.ClassNameFilter, StringComparison.OrdinalIgnoreCase)),
            FolderType.SmartRules when folder.Rules is { } rules => FilterByRules(rules, windows, browserTabs),
            FolderType.Manual => windows.Where(w => folder.ManualWindowHandles.Contains(w.Handle)),
            _ => windows
        };

        foreach (var window in filtered)
        {
            folder.Windows.Add(window);
        }
    }

    private static IEnumerable<WindowInfo> FilterByRules(
        FolderRuleGroup rules, IReadOnlyList<WindowInfo> windows, IReadOnlyList<WindowInfo> browserTabs)
    {
        var candidates = rules.Conditions.Any(condition => condition.Field == RuleField.Domain)
            ? windows.Concat(browserTabs)
            : windows;
        return candidates.Where(rules.Matches);
    }

    /// <summary>
    /// Creates a new smart folder that auto-filters by process name.
    /// </summary>
    public WindowFolder CreateSmartProcessFolder(string name, string processName, string icon = "\uE756", string bgColor = "#2D3A2D")
    {
        var folder = new WindowFolder
        {
            Name = name,
            Icon = icon,
            BackgroundColor = bgColor,
            Type = FolderType.SmartProcess,
            ProcessFilter = processName,
            SortOrder = Folders.Count
        };

        Folders.Add(folder);
        return folder;
    }

    /// <summary>
    /// Creates a new manual folder.
    /// </summary>
    public WindowFolder CreateManualFolder(string name, string icon = "\uE8B7", string bgColor = "#2D2D3D")
    {
        var folder = new WindowFolder
        {
            Name = name,
            Icon = icon,
            BackgroundColor = bgColor,
            Type = FolderType.Manual,
            SortOrder = Folders.Count
        };

        Folders.Add(folder);
        return folder;
    }

    /// <summary>
    /// Creates a new smart folder with composite rules (v2).
    /// </summary>
    public WindowFolder CreateSmartRulesFolder(string name, FolderRuleGroup rules, string icon = "\uE756", string bgColor = "#2D3A2D")
    {
        var folder = new WindowFolder
        {
            Name = name,
            Icon = icon,
            BackgroundColor = bgColor,
            Type = FolderType.SmartRules,
            Rules = rules,
            SortOrder = Folders.Count
        };

        Folders.Add(folder);
        return folder;
    }

    /// <summary>
    /// Creates the special clipboard history folder (singleton — only one should exist).
    /// </summary>
    public WindowFolder CreateClipboardFolder()
    {
        var folder = new WindowFolder
        {
            Name = "Clipboard",
            Icon = "\uE8C8", // Copy icon
            BackgroundColor = "#2A2A3D",
            Type = FolderType.Clipboard,
            SortOrder = Folders.Count
        };
        Folders.Add(folder);
        return folder;
    }

    /// <summary>
    /// Creates the special Recycle Bin folder (singleton — only one should exist).
    /// Clicking it opens the Windows Recycle Bin and hides the overlay.
    /// </summary>
    public WindowFolder CreateRecycleBinFolder()
    {
        var folder = new WindowFolder
        {
            Name = "Recycle Bin",
            Icon = "\uE74D", // Recycle bin icon
            BackgroundColor = "#2A2A2A",
            Type = FolderType.RecycleBin,
            SortOrder = Folders.Count
        };
        Folders.Add(folder);
        return folder;
    }

    /// <summary>
    /// Creates the special Notes folder (singleton — only one should exist).
    /// </summary>
    public WindowFolder CreateNotesFolder()
    {
        var folder = new WindowFolder
        {
            Name = "Notes",
            Icon = "\uE70B", // QuickNote icon
            BackgroundColor = "#2A2A2A",
            Type = FolderType.Notes,
            SortOrder = Folders.Count
        };
        Folders.Add(folder);
        return folder;
    }

    /// <summary>Creates the special Calls workspace.</summary>
    public WindowFolder CreateCallsFolder()
    {
        var folder = new WindowFolder
        {
            Name = "Calls",
            Icon = "\uE720",
            BackgroundColor = "#2A2A2A",
            Type = FolderType.Calls,
            SortOrder = Folders.Count
        };
        Folders.Add(folder);
        return folder;
    }

    public void CreateSshFolder()
    {
        Folders.Add(new WindowFolder
        {
            Name = "SSH",
            Icon = "\uE756",
            Type = FolderType.Ssh,
            SortOrder = Folders.Count
        });
    }

    /// <summary>
    /// Removes a folder by its ID. Cannot remove built-in special folders.
    /// </summary>
    public bool RemoveFolder(string folderId)
    {
        var folder = Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder == null || folder.Type == FolderType.All ||
            folder.Type == FolderType.Clipboard || folder.Type == FolderType.RecycleBin ||
            folder.Type == FolderType.Notes || folder.Type == FolderType.Calls || folder.Type == FolderType.Ssh)
            return false;

        return Folders.Remove(folder);
    }

    /// <summary>
    /// Loads folder configuration from settings.
    /// </summary>
    public void LoadFromSettings(AppSettings settings)
    {
        Folders.Clear();

        // Always ensure "All Windows" folder exists
        Folders.Add(new WindowFolder
        {
            Name = "All Windows",
            Icon = "\uE71D",
            Type = FolderType.All,
            SortOrder = 0
        });

        foreach (var folder in settings.Folders.Where(f => f.Type != FolderType.All).OrderBy(f => f.SortOrder))
        {
            Folders.Add(folder);
        }
    }

    /// <summary>
    /// Saves folder configuration to settings.
    /// </summary>
    public void SaveToSettings(AppSettings settings)
    {
        settings.Folders = Folders
            .Where(f => f.Type != FolderType.All)
            .Select((f, i) => { f.SortOrder = i + 1; return f; })
            .ToList();
    }
}
