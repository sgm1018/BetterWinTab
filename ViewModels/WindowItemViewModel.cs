using BetterWinTab.Models;
using BetterWinTab.Services;

namespace BetterWinTab.ViewModels;

/// <summary>
/// ViewModel wrapper for a WindowInfo displayed in the window grid.
/// </summary>
public partial class WindowItemViewModel : BaseViewModel
{
    public WindowInfo Model { get; }

    [ObservableProperty]
    private string _windowTitle;

    [ObservableProperty]
    private string _processName;

    [ObservableProperty]
    private bool _isMinimized;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isPinned;

    // ── Virtual Desktop ─────────────────────────────────────

    /// <summary>1-based desktop number. 0 = unknown.</summary>
    public int DesktopNumber { get; }

    /// <summary>True when the window is on the currently active virtual desktop.</summary>
    public bool IsOnCurrentDesktop { get; }

    /// <summary>Display label for the badge, e.g. "Desktop 2".</summary>
    public string DesktopBadge { get; }

    /// <summary>Badge visible only when the window is on a *different* desktop AND multiple desktops exist.</summary>
    public bool ShowDesktopBadge { get; }

    /// <summary>
    /// The "important" segment of the window title — the second part when split by " - ".
    /// Example: "CHANGELOG.md - polonia - Visual Studio Code" → "polonia"
    /// Falls back to the full title when there is only one segment.
    /// </summary>
    public string DisplayTitle { get; }

    /// <summary>
    /// The context prefix before the important segment (first part split by " - ").
    /// Example: "CHANGELOG.md - polonia - Visual Studio Code" → "CHANGELOG.md"
    /// Empty when there is only one segment.
    /// </summary>
    public string TitlePrefix { get; }

    public WindowItemViewModel(WindowInfo model, bool hasMultipleDesktops = false)
    {
        Model = model;
        _windowTitle = TruncateTitle(model.Title, 45);
        _processName = model.ProcessName;
        _isMinimized = model.IsMinimized;
        _isPinned = model.IsPinned;
        Title = model.Title;

        // Desktop badge
        DesktopNumber = model.DesktopNumber;
        IsOnCurrentDesktop = model.IsOnCurrentDesktop;
        DesktopBadge = !string.IsNullOrEmpty(model.DesktopName) ? model.DesktopName
                     : model.DesktopNumber > 0
                         ? VirtualDesktopService.GetLocalizedDefaultDesktopName(model.DesktopNumber)
                         : "";
        ShowDesktopBadge = hasMultipleDesktops && !model.IsOnCurrentDesktop && model.DesktopNumber > 0;

        var parts = model.Title.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            DisplayTitle = TruncateTitle(parts[1].Trim(), 30);
            TitlePrefix  = TruncateTitle(parts[0].Trim(), 25);
        }
        else
        {
            DisplayTitle = TruncateTitle(model.Title, 30);
            TitlePrefix  = string.Empty;
        }
    }

    private static string TruncateTitle(string title, int maxLength)
    {
        if (title.Length <= maxLength) return title;
        return title[..(maxLength - 3)] + "...";
    }
}
