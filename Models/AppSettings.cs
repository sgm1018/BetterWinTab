namespace BetterWinTab.Models;

/// <summary>
/// Application settings persisted to disk.
/// </summary>
public class AppSettings
{
    public List<WindowFolder> Folders { get; set; } = new();

    /// <summary>
    /// Hotkey modifier keys (MOD_CONTROL = 0x0002, MOD_ALT = 0x0001, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008).
    /// Default: Ctrl + Tab equivalent via Ctrl+` (grave accent).
    /// </summary>
    public uint HotkeyModifiers { get; set; } = 0x0002; // MOD_CONTROL
    
    /// <summary>
    /// Virtual key code for the hotkey. Default: VK_TAB = 0x09.
    /// Ctrl+Tab toggle.
    /// </summary>
    public uint HotkeyVKey { get; set; } = 0x09; // VK_TAB

    /// <summary>
    /// Whether to show live DWM thumbnails or static icons.
    /// </summary>
    public bool ShowLivePreviews { get; set; } = true;

    /// <summary>
    /// Thumbnail preview size.
    /// </summary>
    public int ThumbnailWidth { get; set; } = 320;
    public int ThumbnailHeight { get; set; } = 200;

    /// <summary>
    /// Appearance settings — all customizable theme colors.
    /// Empty/null means use the default value.
    /// </summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>
    /// User-created custom theme presets.
    /// </summary>
    public List<ThemePreset> CustomPresets { get; set; } = new();

    /// <summary>
    /// Whether the app should start automatically with Windows.
    /// </summary>
    public bool RunAtStartup { get; set; } = false;

    /// <summary>
    /// Pinned windows identifiers (process + title pattern) that survive restarts.
    /// These windows appear first in every folder and survive filtering.
    /// </summary>
    public List<PinnedWindowId> PinnedWindows { get; set; } = new();

    /// <summary>
    /// Maximum number of clipboard history items to keep.
    /// </summary>
    public int ClipboardHistoryMaxItems { get; set; } = 50;

    /// <summary>
    /// Whether clipboard history tracking is enabled.
    /// </summary>
    public bool ClipboardHistoryEnabled { get; set; } = true;

    /// <summary>
    /// Pinned clipboard item texts that survive restarts.
    /// </summary>
    public List<string> PinnedClipboardItems { get; set; } = new();

    /// <summary>
    /// Whether the first-run onboarding walkthrough has been completed or skipped.
    /// </summary>
    public bool HasCompletedOnboarding { get; set; } = false;
}

/// <summary>
/// Identifies a pinned window by process name and title pattern.
/// Handles are volatile (change on reboot), so we use process+title to re-identify.
/// </summary>
public class PinnedWindowId
{
    public string ProcessName { get; set; } = string.Empty;
    public string TitlePattern { get; set; } = string.Empty;

    /// <summary>
    /// Checks if a WindowInfo matches this pin identifier.
    /// TitlePattern supports '*' wildcard at start/end for contains-style matching.
    /// </summary>
    public bool Matches(WindowInfo window)
    {
        if (!window.ProcessName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrEmpty(TitlePattern) || TitlePattern == "*")
            return true;

        var pattern = TitlePattern;
        bool startsWild = pattern.StartsWith('*');
        bool endsWild = pattern.EndsWith('*');
        var core = pattern.Trim('*');

        if (startsWild && endsWild)
            return window.Title.Contains(core, StringComparison.OrdinalIgnoreCase);
        if (startsWild)
            return window.Title.EndsWith(core, StringComparison.OrdinalIgnoreCase);
        if (endsWild)
            return window.Title.StartsWith(core, StringComparison.OrdinalIgnoreCase);

        return window.Title.Equals(core, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Customizable appearance/theme colors. Stored as hex strings (e.g., "#39FF14").
/// Empty or null means the default is used.
/// </summary>
public class AppearanceSettings
{
    public string AccentColor { get; set; } = "#39FF14";         // NeonGreen
    public string AccentDimColor { get; set; } = "#1A8A0A";      // NeonGreenDim
    public string AccentSubtleColor { get; set; } = "#0D3D06";   // NeonGreenSubtle
    public string BackgroundColor { get; set; } = "#000000";      // PureBlack
    public string SurfaceColor { get; set; } = "#0A0A0A";        // DarkSurface
    public string CardColor { get; set; } = "#111111";            // CardSurface
    public string BorderColor { get; set; } = "#1A1A1A";         // BorderDark
    public string TextPrimaryColor { get; set; } = "#FFFFFF";     // TextPrimary
    public string TextSecondaryColor { get; set; } = "#AAAAAA";  // TextSecondary
    public string TextMutedColor { get; set; } = "#666666";       // TextMuted
    public string DangerColor { get; set; } = "#FF3344";          // DangerRed
    public string FolderHoverColor { get; set; } = "#1A39FF14";   // Folder item hover overlay
    public string FolderSelectedColor { get; set; } = "#2939FF14"; // Folder item selected overlay
    public string WindowHoverBorderColor { get; set; } = "#6639FF14";   // Window card hover border
    public string WindowHoverBackgroundColor { get; set; } = "#0C39FF14"; // Window card hover background

    /// <summary>
    /// Returns a new AppearanceSettings with all defaults.
    /// </summary>
    public static AppearanceSettings Default() => new();
}
