using System.Collections.ObjectModel;
using BetterWinTab.Models;

namespace BetterWinTab.Services;

/// <summary>
/// Manages window folders (categories) and their window assignments.
/// Handles both manual folders and smart (auto-filtered) folders.
/// </summary>
public class FolderService
{
    private readonly WindowEnumerationService _windowService;

    public ObservableCollection<WindowFolder> Folders { get; } = new();

    public FolderService(WindowEnumerationService windowService)
    {
        _windowService = windowService;
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
    public void RefreshAllFolders()
    {
        var allWindows = _windowService.GetAllWindows();

        foreach (var folder in Folders)
        {
            RefreshFolder(folder, allWindows);
        }
    }

    /// <summary>
    /// Refreshes a single folder with the current list of windows.
    /// </summary>
    public void RefreshFolder(WindowFolder folder, List<WindowInfo>? allWindows = null)
    {
        allWindows ??= _windowService.GetAllWindows();

        folder.Windows.Clear();

        // Clipboard and RecycleBin folders don't contain windows
        if (folder.Type == FolderType.Clipboard || folder.Type == FolderType.RecycleBin)
            return;

        var filtered = folder.Type switch
        {
            FolderType.All => allWindows,
            FolderType.SmartProcess => allWindows
                .Where(w => w.ProcessName.Equals(folder.ProcessFilter, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            FolderType.SmartClass => allWindows
                .Where(w => w.ClassName.Equals(folder.ClassNameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            FolderType.SmartRules => folder.Rules != null
                ? allWindows.Where(w => folder.Rules.Matches(w)).ToList()
                : allWindows,
            FolderType.Manual => allWindows
                .Where(w => folder.ManualWindowHandles.Contains(w.Handle))
                .ToList(),
            _ => allWindows
        };

        foreach (var window in filtered)
        {
            folder.Windows.Add(window);
        }
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
    /// Removes a folder by its ID. Cannot remove built-in special folders.
    /// </summary>
    public bool RemoveFolder(string folderId)
    {
        var folder = Folders.FirstOrDefault(f => f.Id == folderId);
        if (folder == null || folder.Type == FolderType.All ||
            folder.Type == FolderType.Clipboard || folder.Type == FolderType.RecycleBin)
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
