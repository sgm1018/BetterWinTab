using BetterWinTab.Models;
using BetterWinTab.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

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
    public bool IsBrowserTab => Model.IsBrowserTab;
    public string ItemTypeLabel => Model.IsBrowserTab ? "TAB" : "WINDOW";
    public string SourceLabel => Model.IsBrowserTab ? $"{Model.BrowserName} · tab" : ProcessName;
    public Brush TypeAccentBrush => Model.IsBrowserTab
        ? new SolidColorBrush(ColorHelper.FromArgb(255, 53, 201, 255))
        : new SolidColorBrush(ColorHelper.FromArgb(255, 57, 255, 20));
    public Brush TypeAccentDimBrush => Model.IsBrowserTab
        ? new SolidColorBrush(ColorHelper.FromArgb(255, 23, 101, 127))
        : new SolidColorBrush(ColorHelper.FromArgb(255, 26, 138, 10));
    public Brush TypeChipBackgroundBrush => Model.IsBrowserTab
        ? new SolidColorBrush(ColorHelper.FromArgb(204, 9, 43, 54))
        : new SolidColorBrush(ColorHelper.FromArgb(204, 13, 61, 6));
    public string BrowserBadge => Model.IsBrowserTab ? "TAB" : string.Empty;
    public string BrowserIconGlyph => Model.BrowserName switch
    {
        "Chrome" => "\uE774",
        "Edge" => "\uE774",
        "Brave" => "\uE774",
        _ => "\uE737"
    };
    public BitmapImage? BrowserFavicon { get; }

    public WindowItemViewModel(WindowInfo model, bool hasMultipleDesktops = false)
    {
        Model = model;
        _windowTitle = TruncateTitle(model.Title, 45);
        _processName = model.ProcessName;
        _isMinimized = model.IsMinimized;
        _isPinned = model.IsPinned;
        Title = model.Title;
        BrowserFavicon = CreateFavicon(model.FaviconUrl, model.Url);

        // Desktop badge
        DesktopNumber = model.DesktopNumber;
        IsOnCurrentDesktop = model.IsOnCurrentDesktop;
        DesktopBadge = !string.IsNullOrEmpty(model.DesktopName) ? model.DesktopName
                     : model.DesktopNumber > 0
                         ? VirtualDesktopService.GetLocalizedDefaultDesktopName(model.DesktopNumber)
                         : "";
        ShowDesktopBadge = hasMultipleDesktops && !model.IsOnCurrentDesktop && model.DesktopNumber > 0;

        var parts = model.IsBrowserTab
            ? Array.Empty<string>()
            : model.Title.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
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

    private static BitmapImage? CreateFavicon(string faviconUrl, string pageUrl)
    {
        var url = faviconUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) ||
                (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps))
                return null;

            url = $"https://www.google.com/s2/favicons?sz=64&domain_url={Uri.EscapeDataString(pageUrl)}";
            uri = new Uri(url);
        }

        return new BitmapImage(uri);
    }
}
