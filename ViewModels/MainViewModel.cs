using System.Collections.ObjectModel;
using System.Text;
using BetterWinTab.Models;
using BetterWinTab.Services;
using Microsoft.UI.Dispatching;

namespace BetterWinTab.ViewModels;

/// <summary>
/// Main ViewModel that drives the overlay UI.
/// Manages folder navigation, window list, and user interactions.
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    private readonly WindowEnumerationService _windowService;
    private readonly FolderService _folderService;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly LaunchService _launchService = new();
    private readonly VirtualDesktopService _virtualDesktopService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly HashSet<IntPtr> _sessionPinnedHandles = new();
    private DispatcherQueue? _dispatcherQueue;
    private System.Threading.Timer? _toastTimer;
    private readonly UpdateService _updateService = new();

    public ObservableCollection<FolderItemViewModel> Folders { get; } = new();
    /// <summary>All folders excluding the Clipboard folder — bound to the sidebar ListView.</summary>
    public ObservableCollection<FolderItemViewModel> NonClipboardFolders { get; } = new();
    public ObservableCollection<WindowItemViewModel> Windows { get; } = new();
    public ObservableCollection<LaunchItemViewModel> LaunchResults { get; } = new();
    public ObservableCollection<ClipboardItem> ClipboardHistory => _clipboardService.History;

    /// <summary>
    /// Fired after the Windows collection is refreshed (folder change, manual refresh, etc.).
    /// Used by the View to re-register DWM thumbnails.
    /// </summary>
    public event Action? WindowsRefreshed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedFolder))]
    [NotifyPropertyChangedFor(nameof(RecycleBinFolderIsSelected))]
    private FolderItemViewModel? _selectedFolder;

    /// <summary>
    /// The selection reflected in the FolderList ListView (only non-Clipboard folders).
    /// TwoWay-bound to the ListView so that ListView clicks update SelectedFolder,
    /// but setting SelectedFolder to Clipboard doesn't write null back into SelectedFolder.
    /// </summary>
    [ObservableProperty]
    private FolderItemViewModel? _selectedNonClipboardFolder;

    private bool _syncingSelection;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipboardFolderVisibility))]
    [NotifyPropertyChangedFor(nameof(ClipboardFolderIsSelected))]
    private FolderItemViewModel? _clipboardFolderVM;

    /// <summary>True when the Clipboard folder tab is selected (for button highlight).</summary>
    public bool ClipboardFolderIsSelected => SelectedFolder == ClipboardFolderVM && ClipboardFolderVM != null;
    /// <summary>Controls visibility of the pinned Clipboard folder button in the sidebar.</summary>
    public Microsoft.UI.Xaml.Visibility ClipboardFolderVisibility =>
        ClipboardFolderVM != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecycleBinFolderVisibility))]
    [NotifyPropertyChangedFor(nameof(RecycleBinFolderIsSelected))]
    private FolderItemViewModel? _recycleBinFolderVM;

    /// <summary>True when the Recycle Bin folder tab is selected (for button highlight).</summary>
    public bool RecycleBinFolderIsSelected => SelectedFolder == RecycleBinFolderVM && RecycleBinFolderVM != null;
    /// <summary>Controls visibility of the pinned Recycle Bin folder button in the sidebar.</summary>
    public Microsoft.UI.Xaml.Visibility RecycleBinFolderVisibility =>
        RecycleBinFolderVM != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    private WindowItemViewModel? _selectedWindow;

    [ObservableProperty]
    private bool _isOverlayVisible;

    // ── Update notification ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    [NotifyPropertyChangedFor(nameof(ShowCurrentVersionLabel))]
    [NotifyPropertyChangedFor(nameof(ShowUpdatePanel))]
    [NotifyPropertyChangedFor(nameof(UpdateActionLabel))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateActionLabel))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCurrentVersionLabel))]
    [NotifyPropertyChangedFor(nameof(ShowUpdatePanel))]
    [NotifyPropertyChangedFor(nameof(UpdateProgressText))]
    private bool _isUpdateProgressVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateProgressText))]
    private bool _isUpdateProgressIndeterminate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateProgressText))]
    private int _updateDownloadPercentage;

    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    /// <summary>True while no download is in progress — drives the button's IsEnabled.</summary>
    public bool CanInstallUpdate => IsUpdateAvailable && !IsDownloadingUpdate;
    public bool ShowUpdatePanel => IsUpdateAvailable || IsUpdateProgressVisible;
    public bool ShowCurrentVersionLabel => !ShowUpdatePanel;
    public string CurrentVersionLabel => $"BetterWinTab v{UpdateService.CurrentVersion}";
    public string UpdateActionLabel =>
        IsDownloadingUpdate
            ? "Downloading update..."
            : $"\u2B06 Update to v{_updateService.LatestVersion}";
    public string UpdateProgressText =>
        IsUpdateProgressIndeterminate ? "..." : $"{Math.Clamp(UpdateDownloadPercentage, 0, 100)}%";

    /// <summary>
    /// Triggered by the version badge button when an update is available.
    /// Downloads the installer to %TEMP% and launches it.
    /// Uses a void wrapper so the source generator emits RelayCommand (not
    /// AsyncRelayCommand), keeping the XAML compiler compatible.
    /// </summary>
    [RelayCommand]
    private void 
    InstallUpdate()
    {
        if (IsDownloadingUpdate) return;
        _ = InstallUpdateInternalAsync();
    }

    private async Task InstallUpdateInternalAsync()
    {
        IsDownloadingUpdate = true;
        IsUpdateProgressVisible = true;
        IsUpdateProgressIndeterminate = true;
        UpdateDownloadPercentage = 0;
        UpdateStatusMessage = "Preparing installer download...";

        try
        {
            var progress = new Progress<UpdateService.DownloadProgress>(info =>
            {
                UpdateStatusMessage = info.Message;
                IsUpdateProgressIndeterminate = info.IsIndeterminate;

                if (info.Percentage.HasValue)
                    UpdateDownloadPercentage = Math.Clamp(info.Percentage.Value, 0, 100);
            });

            var ok = await _updateService.DownloadAndInstallAsync(progress);
            if (ok)
            {
                IsUpdateAvailable = false;
                IsUpdateProgressIndeterminate = false;
                UpdateDownloadPercentage = 100;
                UpdateStatusMessage = "Installer opened. Follow the installation steps in the setup wizard.";
                App.Current.HideOverlay();
            }
            else
            {
                IsUpdateAvailable = false;
                IsUpdateProgressIndeterminate = false;
                UpdateStatusMessage = "Update found, but this release does not include an installer asset.";
            }
        }
        catch (Exception ex)
        {
            var failureLog = BuildUpdateFailureLog(ex);
            System.Diagnostics.Debug.WriteLine(failureLog);

            var copiedToClipboard = _clipboardService.CopyTextToClipboard(failureLog);
            IsUpdateProgressIndeterminate = false;
            UpdateDownloadPercentage = 0;
            UpdateStatusMessage = copiedToClipboard
                ? "Update download failed. The error log was copied to the clipboard. Send it to me and I will fix it."
                : "Update download failed. Open the debugger output and send me the updater log.";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private string BuildUpdateFailureLog(Exception ex)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[BetterWinTab Updater Failure]");
        builder.Append("Timestamp: ").AppendLine(DateTimeOffset.Now.ToString("O"));
        builder.Append("CurrentVersion: ").AppendLine(UpdateService.CurrentVersion);
        builder.Append("LatestVersion: ").AppendLine(_updateService.LatestVersion);
        builder.Append("InstallerUrl: ").AppendLine(_updateService.InstallerDownloadUrl ?? "<null>");
        builder.Append("LastStatus: ").AppendLine(UpdateStatusMessage);
        builder.Append("ProgressVisible: ").AppendLine(IsUpdateProgressVisible.ToString());
        builder.Append("ProgressIndeterminate: ").AppendLine(IsUpdateProgressIndeterminate.ToString());
        builder.Append("Percentage: ").AppendLine(UpdateDownloadPercentage.ToString());
        builder.AppendLine("Exception:");
        builder.AppendLine(ex.ToString());
        return builder.ToString();
    }

    // ── Onboarding (first-run walkthrough) ───────────────────────────────────

    [ObservableProperty]
    private bool _isOnboardingVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnboardingStepIcon))]
    [NotifyPropertyChangedFor(nameof(OnboardingStepTitle))]
    [NotifyPropertyChangedFor(nameof(OnboardingStepDescription))]
    [NotifyPropertyChangedFor(nameof(OnboardingNextButtonText))]
    [NotifyPropertyChangedFor(nameof(OnboardingProgressText))]
    private int _currentOnboardingStep;

    public string OnboardingProgressText => $"Step {CurrentOnboardingStep + 1} of 6";
    public string OnboardingNextButtonText => CurrentOnboardingStep == 5 ? "Get Started  🎉" : "Next  →";

    public string OnboardingStepIcon => CurrentOnboardingStep switch
    {
        0 => "\uE8A1",
        1 => "\uE721",
        2 => "\uE8B7",
        3 => "\uE737",
        4 => "\uE765",
        _ => "\uE713",
    };

    public string OnboardingStepTitle => CurrentOnboardingStep switch
    {
        0 => "Welcome to BetterWinTab",
        1 => "Instant search",
        2 => "Folders",
        3 => "Window switcher",
        4 => "Keyboard controls",
        _ => "You're all set!",
    };

    public string OnboardingStepDescription => CurrentOnboardingStep switch
    {
        0 => $"Your smart window switcher — manage every open window from one keyboard-driven overlay.\n\nPress {FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVKey)} at any time, from any app, to open or close it.",
        1 => "Type anything to filter open windows in real time — no mouse needed.\n\nNo match? BetterWinTab also searches your installed apps. Press Enter to launch one directly from the overlay.",
        2 => "Group windows into custom folders for any project or context.\n\nSmart folders auto-populate by process name or title rule. Drag a window card onto a sidebar folder to assign it manually.",
        3 => "Click a card to switch to that window instantly.\n\nRight-click a card for more options: pin it so it always appears first, minimize it, or close it — all without leaving the overlay.",
        4 => "← → ↑ ↓  navigate cards   ·   Enter  switch to selected   ·   Esc  close overlay\n\nIn the Clipboard panel ↑↓ moves between entries and Enter copies the selected one.\n\nDrag any window card onto a sidebar folder to move it in there.",
        _ => "Customize everything in ⚙ Settings: change the global hotkey, pick an accent color, and enable auto-start on login.\n\nAll settings save automatically. Enjoy BetterWinTab!",
    };

    [RelayCommand]
    private void NextOnboardingStep()
    {
        if (CurrentOnboardingStep < 5) { CurrentOnboardingStep++; return; }
        IsOnboardingVisible = false;
        _settings.HasCompletedOnboarding = true;
        SaveSettings();
    }

    [RelayCommand]
    private void SkipOnboarding()
    {
        IsOnboardingVisible = false;
        _settings.HasCompletedOnboarding = true;
        SaveSettings();
    }

    [ObservableProperty]
    private bool _isAddFolderPanelVisible;

    [ObservableProperty]
    private bool _isEditFolderPanelVisible;

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    [ObservableProperty]
    private string _editFolderName = string.Empty;

    [ObservableProperty]
    private string _editFolderIcon = "\uE8B7";

    [ObservableProperty]
    private string _editFolderBgColor = "";

    [ObservableProperty]
    private bool _editFolderIsSmart;

    [ObservableProperty]
    private string? _editFolderProcessFilter;

    /// <summary>
    /// The folder currently being edited (via right-click → Edit).
    /// </summary>
    private FolderItemViewModel? _editingFolder;

    // ── Settings Panel Properties ──

    [ObservableProperty]
    private bool _isSettingsPanelVisible;

    [ObservableProperty]
    private bool _settingsRunAtStartup;

    // ── Hotkey settings ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplayText))]
    private bool _isRecordingHotkey;

    /// <summary>
    /// Human-readable description of the current hotkey (or a prompt when recording).
    /// </summary>
    public string HotkeyDisplayText =>
        IsRecordingHotkey
            ? "▶  Press your key combo now..."
            : FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVKey);

    [RelayCommand]
    public void StartRecordHotkey() => IsRecordingHotkey = true;

    [RelayCommand]
    public void CancelRecordHotkey() => IsRecordingHotkey = false;

    /// <summary>
    /// Called by the view after the user presses a key combo during recording.
    /// Persists the new hotkey immediately and notifies the HotkeyService.
    /// </summary>
    public void ApplyNewHotkey(uint modifiers, uint vKey)
    {
        _settings.HotkeyModifiers = modifiers;
        _settings.HotkeyVKey      = vKey;
        IsRecordingHotkey         = false;
        OnPropertyChanged(nameof(HotkeyDisplayText));
        App.Current.Hotkey?.Configure(modifiers, vKey);
        SaveSettings();
    }

    /// <summary>
    /// Formats <paramref name="modifiers"/> + <paramref name="vKey"/> into a readable string
    /// such as "Ctrl + Tab" or "Ctrl + Alt + F".
    /// </summary>
    private static string FormatHotkey(uint modifiers, uint vKey)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        parts.Add(vKey switch
        {
            0x09                           => "Tab",
            0x20                           => "Space",
            0x1B                           => "Esc",
            0x0D                           => "Enter",
            0x2E                           => "Del",
            0xC0                           => "`",
            0xBD                           => "-",
            0xBB                           => "=",
            >= 0x41 and <= 0x5A            => ((char)vKey).ToString(),  // A–Z
            >= 0x30 and <= 0x39            => ((char)vKey).ToString(),  // 0–9
            >= 0x70 and <= 0x7B            => $"F{vKey - 0x6F}",       // F1–F12
            >= 0x60 and <= 0x69            => $"Num{vKey - 0x60}",     // Numpad 0–9
            _                              => $"0x{vKey:X2}"
        });
        return string.Join(" + ", parts);
    }

    [ObservableProperty]
    private string _settingsAccentColor = "#39FF14";

    [ObservableProperty]
    private string _settingsAccentDimColor = "#1A8A0A";

    [ObservableProperty]
    private string _settingsAccentSubtleColor = "#0D3D06";

    [ObservableProperty]
    private string _settingsBackgroundColor = "#000000";

    [ObservableProperty]
    private string _settingsSurfaceColor = "#0A0A0A";

    [ObservableProperty]
    private string _settingsCardColor = "#111111";

    [ObservableProperty]
    private string _settingsBorderColor = "#1A1A1A";

    [ObservableProperty]
    private string _settingsTextPrimaryColor = "#FFFFFF";

    [ObservableProperty]
    private string _settingsTextSecondaryColor = "#AAAAAA";

    [ObservableProperty]
    private string _settingsTextMutedColor = "#666666";

    [ObservableProperty]
    private string _settingsDangerColor = "#FF3344";

    [ObservableProperty]
    private string _settingsFolderHoverColor = "#1A39FF14";

    [ObservableProperty]
    private string _settingsFolderSelectedColor = "#2939FF14";

    [ObservableProperty]
    private string _settingsWindowHoverBorderColor = "#6639FF14";

    [ObservableProperty]
    private string _settingsWindowHoverBackgroundColor = "#0C39FF14";

    [ObservableProperty]
    private string? _selectedProcessFilter;

    [ObservableProperty]
    private bool _isSmartFolder;

    /// <summary>Currently highlighted clipboard item (for arrow-key navigation and keyboard copy).</summary>
    [ObservableProperty]
    private ClipboardItem? _selectedClipboardItem;

    /// <summary>Controls visibility of the "copied" success toast.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipboardToastVisibility))]
    private bool _clipboardCopiedToastVisible;

    /// <summary>Visibility helper for the clipboard copy toast.</summary>
    public Visibility ClipboardToastVisibility => ClipboardCopiedToastVisible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True when the selected folder is a Clipboard folder (shows clipboard UI instead of windows).</summary>
    public bool IsClipboardFolderSelected => SelectedFolder?.Model?.Type == FolderType.Clipboard;

    /// <summary>Clipboard folder visibility helper.</summary>
    public Visibility ClipboardPanelVisibility => IsClipboardFolderSelected ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WindowGridVisibility => IsClipboardFolderSelected ? Visibility.Collapsed : Visibility.Visible;

    // ── Smart Rules folder creation/editing ──

    [ObservableProperty]
    private bool _isSmartRulesFolder;

    [ObservableProperty]
    private string _ruleOperator = "OR";

    public ObservableCollection<FolderRuleCondition> EditingRuleConditions { get; } = new();

    [ObservableProperty]
    private string _newRuleField = "ProcessName";

    [ObservableProperty]
    private string _newRuleComparison = "Equals";

    [ObservableProperty]
    private string _newRuleValue = string.Empty;

    public List<string> RuleFieldOptions { get; } = new() { "ProcessName", "Title", "ClassName" };
    public List<string> RuleComparisonOptions { get; } = new() { "Equals", "Contains", "StartsWith", "EndsWith", "Regex" };
    public List<string> RuleOperatorOptions { get; } = new() { "OR", "AND" };

    [ObservableProperty]
    private Visibility _hasNoWindows = Visibility.Collapsed;

    [ObservableProperty]
    private string _selectedIcon = "\uE8B7"; // Default folder icon

    [ObservableProperty]
    private string _selectedBgColor = ""; // No background by default

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    [NotifyPropertyChangedFor(nameof(SearchClearVisibility))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private LaunchItemViewModel? _selectedLaunchItem;

    public bool IsSearchActive => !string.IsNullOrEmpty(SearchQuery);
    public Visibility SearchClearVisibility => IsSearchActive ? Visibility.Visible : Visibility.Collapsed;
    public string SearchResultCount => IsSearchActive ? $"{Windows.Count} result{(Windows.Count == 1 ? "" : "s")}" : string.Empty;

    // Launch panel visibility helpers
    public Visibility ShowEmptyDefault      => !IsSearchActive && SelectedFolder != ClipboardFolderVM                        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowLaunchSuggestions => IsSearchActive && Windows.Count == 0 && LaunchResults.Count > 0               ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowRunFallback       => IsSearchActive && Windows.Count == 0 && LaunchResults.Count == 0              ? Visibility.Visible : Visibility.Collapsed;
    public string RunFallbackLabel          => $"Press Enter to run \"{SearchQuery}\"";

    /// <summary>
    /// True when the selected folder can be deleted (not the "All Windows" folder).
    /// </summary>
    public bool CanDeleteSelectedFolder =>
        SelectedFolder?.Model?.Type != FolderType.All &&
        SelectedFolder?.Model?.Type != FolderType.Clipboard &&
        SelectedFolder?.Model?.Type != FolderType.RecycleBin;

    private readonly List<WindowInfo> _cachedFolderWindows = new();
    private int _windowGridColumnCount = 1;

    /// <summary>
    /// Remembers which folder was active before starting a search,
    /// so we can restore it when the user presses Escape.
    /// </summary>
    private FolderItemViewModel? _folderBeforeSearch;

    public ObservableCollection<string> AvailableProcesses { get; } = new();

    /// <summary>
    /// Available icons for folder creation.
    /// </summary>
    public List<string> AvailableIcons { get; } = new()
    {
        "\uE8B7", // Folder
        "\uE71D", // Globe
        "\uE756", // Star
        "\uE774", // Browser
        "\uE943", // Code
        "\uE7F4", // Device
        "\uE8F1", // Music
        "\uE8B2", // Film
        "\uE77B", // People
        "\uE723", // Gear
        "\uE783", // Gaming
        "\uE753", // Heart
        "\uE7C3", // Lightning
        "\uE716", // Clipboard
        "\uE8A5", // Document
        "\uE8FC", // Lock
    };

    /// <summary>
    /// Background colors for folders — vivid enough to contrast against pure black.
    /// First entry is empty string = no color.
    /// </summary>
    public List<string> AvailableColors { get; } = new()
    {
        "",       // No color
        "#4A2D6B", // Deep Purple
        "#2D5A4A", // Emerald
        "#6B2D2D", // Deep Rose
        "#5A4A2D", // Amber
        "#2D3E6B", // Navy Blue
        "#4A6B2D", // Forest
        "#6B2D4A", // Magenta
        "#2D5A6B", // Ocean
    };

    /// <summary>
    /// User-created custom theme presets (loaded from saved settings).
    /// </summary>
    public ObservableCollection<ThemePreset> CustomPresets { get; } = new();

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    public MainViewModel()
    {
        _windowService = new WindowEnumerationService();
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        IsOnboardingVisible = !_settings.HasCompletedOnboarding;
        _folderService = new FolderService(_windowService);

        Title = "BetterWinTab";

        // Load saved folders
        _folderService.LoadFromSettings(_settings);

        // If no custom folders exist, create some defaults
        if (_folderService.Folders.Count <= 1)
        {
            _folderService.CreateSmartProcessFolder("VS Code", "Code", "\uE943");
            _folderService.CreateSmartProcessFolder("Browser", "chrome", "\uE774");
            _folderService.CreateSmartProcessFolder("Explorer", "explorer", "\uE8B7");
            SaveSettings();
        }

        // Ensure clipboard folder always exists when clipboard history is enabled
        if (_settings.ClipboardHistoryEnabled &&
            !_folderService.Folders.Any(f => f.Type == FolderType.Clipboard))
        {
            _folderService.CreateClipboardFolder();
            SaveSettings();
        }

        // Ensure Recycle Bin folder always exists (pinned special folder, cannot be deleted)
        if (!_folderService.Folders.Any(f => f.Type == FolderType.RecycleBin))
        {
            _folderService.CreateRecycleBinFolder();
            SaveSettings();
        }

        SyncFolders();

        if (Folders.Count > 0)
        {
            SelectedFolder = Folders[0];
        }

        // Load user-saved custom presets
        foreach (var p in _settings.CustomPresets)
            CustomPresets.Add(p);

        // ── Auto-update: check GitHub Releases on startup ─────────────────────
        // Capture the UI dispatcher here (constructor always runs on UI thread).
        var uiDispatcher = DispatcherQueue.GetForCurrentThread();
        _updateService.StateChanged += () =>
            uiDispatcher?.TryEnqueue(() =>
            {
                IsUpdateAvailable = _updateService.IsUpdateAvailable;
            });
        _ = _updateService.CheckAsync();
    }

    partial void OnSelectedFolderChanged(FolderItemViewModel? value)
    {
        // Keep the ListView selection in sync — but only when the newly selected
        // folder is a regular (non-clipboard) one.  If clipboard is selected, clear
        // the ListView highlight without triggering the reverse write-back.
        if (!_syncingSelection)
        {
            _syncingSelection = true;
            SelectedNonClipboardFolder = (value?.Model?.Type != FolderType.Clipboard &&
                                          value?.Model?.Type != FolderType.RecycleBin) ? value : null;
            _syncingSelection = false;
        }

        if (value != null)
        {
            // If the user manually switches folder, discard the saved "before search" folder
            _folderBeforeSearch = null;
            // Clear search when switching folders so we see all windows in the new folder
            SearchQuery = string.Empty;
            RefreshWindows();
        }

        // Notify clipboard/recycle bin visibility properties
        OnPropertyChanged(nameof(ShowEmptyDefault));
        OnPropertyChanged(nameof(IsClipboardFolderSelected));
        OnPropertyChanged(nameof(ClipboardPanelVisibility));
        OnPropertyChanged(nameof(WindowGridVisibility));
        OnPropertyChanged(nameof(ClipboardFolderIsSelected));
        OnPropertyChanged(nameof(RecycleBinFolderIsSelected));
    }

    partial void OnSelectedNonClipboardFolderChanged(FolderItemViewModel? value)
    {
        // When the user clicks a folder in the ListView, propagate to SelectedFolder.
        // Guard against the sync initiated in OnSelectedFolderChanged.
        if (!_syncingSelection && value != null)
        {
            _syncingSelection = true;
            SelectedFolder = value;
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// When "Smart Folder (process)" is toggled ON, turn off the advanced rules toggle
    /// so both options are never active simultaneously.
    /// </summary>
    partial void OnIsSmartFolderChanged(bool value)
    {
        if (value) IsSmartRulesFolder = false;
    }

    /// <summary>
    /// When "Advanced Rules" is toggled ON, turn off the simple smart-folder toggle.
    /// </summary>
    partial void OnIsSmartRulesFolderChanged(bool value)
    {
        if (value) IsSmartFolder = false;
    }

    /// <summary>
    /// Syncs the FolderService folders to view model items.
    /// </summary>
    private void SyncFolders()
    {
        Folders.Clear();
        NonClipboardFolders.Clear();
        ClipboardFolderVM = null;
        RecycleBinFolderVM = null;

        foreach (var folder in _folderService.Folders)
        {
            var vm = new FolderItemViewModel(folder);
            Folders.Add(vm);
            if (folder.Type == FolderType.Clipboard)
                ClipboardFolderVM = vm;
            else if (folder.Type == FolderType.RecycleBin)
                RecycleBinFolderVM = vm;
            else
                NonClipboardFolders.Add(vm);
        }
    }

    /// <summary>
    /// Refreshes the window list for the currently selected folder.
    /// Caches the raw window list then applies the current search filter.
    /// </summary>
    [RelayCommand]
    public void RefreshWindows()
    {
        _folderService.RefreshAllFolders();

        // Enrich ALL windows first so that the VirtualDesktopService discovers
        // every desktop in the system. This ensures HasMultipleDesktops is correct
        // regardless of which folder is currently selected.
        var allFolder = _folderService.Folders.FirstOrDefault(f => f.Type == FolderType.All);
        if (allFolder != null)
        {
            _virtualDesktopService.EnrichWindows(allFolder.Windows.ToList());

            // Enrich pin status: prefer handle-based tracking (same session),
            // then fall back to saved title-pattern matching (cross-session).
            foreach (var w in allFolder.Windows)
            {
                w.IsPinned = _sessionPinnedHandles.Contains(w.Handle)
                    || _settings.PinnedWindows.Any(p => p.Matches(w));
            }
        }

        _cachedFolderWindows.Clear();
        var selectedModel = SelectedFolder?.Model;
        if (selectedModel != null)
        {
            foreach (var w in selectedModel.Windows)
                _cachedFolderWindows.Add(w);
        }

        // WindowInfo objects are shared references across folders, so they
        // already carry DesktopId / IsOnCurrentDesktop / DesktopNumber
        // from the all-windows enrichment pass above.

        // Always reset selection — old WindowItemViewModel instances are gone after clear
        SelectedWindow = null;

        ApplySearchFilter();
    }

    /// <summary>
    /// Filters the Windows collection from the folder cache using the current SearchQuery.
    /// Uses fuzzy matching with fallback to exact substring for typo tolerance.
    /// </summary>
    private void ApplySearchFilter()
    {
        var q = SearchQuery.Trim();

        List<WindowInfo> source;
        if (string.IsNullOrEmpty(q))
        {
            source = _cachedFolderWindows;
        }
        else
        {
            // Fuzzy match against title and process name, keep the best score
            // Multi-word token matching: each space-separated word must match at least
            // one of: Title or ProcessName. ClassName is excluded because many Electron
            // apps share Chrome_WidgetWin_1, causing false positives.
            source = _cachedFolderWindows
                .Select(w =>
                {
                    var (matched, score) = FuzzyMatcher.MultiWordMatch(
                        new[] { w.Title, w.ProcessName }, q);
                    return (window: w, matched, score);
                })
                .Where(x => x.matched)
                .OrderByDescending(x => x.score)
                .Select(x => x.window)
                .ToList();
        }

        Windows.Clear();
        bool multiDesk = _virtualDesktopService.HasMultipleDesktops;

        // Sort pinned windows first, then the rest
        var sorted = source.OrderByDescending(w => w.IsPinned).ToList();
        foreach (var w in sorted)
            Windows.Add(new WindowItemViewModel(w, multiDesk));

        SelectedWindow = Windows.Count > 0 ? Windows[0] : null;
        HasNoWindows = Windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Populate launch suggestions when there are no matching windows
        LaunchResults.Clear();
        if (Windows.Count == 0 && !string.IsNullOrEmpty(q))
        {
            foreach (var item in _launchService.Search(q))
                LaunchResults.Add(new LaunchItemViewModel(item));
            SelectedLaunchItem = LaunchResults.Count > 0 ? LaunchResults[0] : null;
        }
        else
        {
            SelectedLaunchItem = null;
        }

        OnPropertyChanged(nameof(ShowEmptyDefault));
        OnPropertyChanged(nameof(ShowLaunchSuggestions));
        OnPropertyChanged(nameof(ShowRunFallback));
        OnPropertyChanged(nameof(RunFallbackLabel));
        OnPropertyChanged(nameof(SearchResultCount));

        // Notify listeners (View) to re-register DWM thumbnails
        WindowsRefreshed?.Invoke();
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            // If starting a new search, remember current folder and switch to All Windows.
            // If we were on the Clipboard folder, do NOT save it as the restore target —
            // searching should stay on All Windows even after the search is cleared.
            if (_folderBeforeSearch == null && SelectedFolder != Folders[0])
            {
                _folderBeforeSearch = IsClipboardFolderSelected ? null : SelectedFolder;
                // Switch to All Windows without clearing search (skip OnSelectedFolderChanged reset)
                _selectedFolder = Folders[0];
                // Sync the non-clipboard ListView selection
                _syncingSelection = true;
                SelectedNonClipboardFolder = Folders[0]; // Folders[0] is also in NonClipboardFolders
                _syncingSelection = false;
                OnPropertyChanged(nameof(SelectedFolder));
                // Notify clipboard visibility properties (panel must hide, window grid must show)
                OnPropertyChanged(nameof(IsClipboardFolderSelected));
                OnPropertyChanged(nameof(ClipboardPanelVisibility));
                OnPropertyChanged(nameof(WindowGridVisibility));
                OnPropertyChanged(nameof(ClipboardFolderIsSelected));
                RefreshWindows();
                return; // RefreshWindows already calls ApplySearchFilter
            }
        }
        ApplySearchFilter();
    }

    /// <summary>
    /// Clears the search query and restores the folder that was active before the search.
    /// </summary>
    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = string.Empty;

        // Restore the folder that was selected before searching
        if (_folderBeforeSearch != null)
        {
            var folderToRestore = Folders.FirstOrDefault(f => f.Model.Id == _folderBeforeSearch.Model.Id);
            _folderBeforeSearch = null;
            if (folderToRestore != null)
            {
                SelectedFolder = folderToRestore;
                return; // OnSelectedFolderChanged will call RefreshWindows
            }
        }
    }

    // ── Launch (app launcher fallback) ──

    /// <summary>
    /// Navigates the launch suggestion list upward.
    /// </summary>
    public void NavigateLaunchUp()
    {
        if (LaunchResults.Count == 0) return;
        var idx = SelectedLaunchItem == null ? 0 : LaunchResults.IndexOf(SelectedLaunchItem);
        SelectedLaunchItem = LaunchResults[Math.Max(0, idx - 1)];
    }

    /// <summary>
    /// Navigates the launch suggestion list downward.
    /// </summary>
    public void NavigateLaunchDown()
    {
        if (LaunchResults.Count == 0) return;
        var idx = SelectedLaunchItem == null ? -1 : LaunchResults.IndexOf(SelectedLaunchItem);
        SelectedLaunchItem = LaunchResults[Math.Min(LaunchResults.Count - 1, idx + 1)];
    }

    /// <summary>
    /// Launches the selected suggestion item, or runs the raw search query via the Shell
    /// when there are no suggestions (Run-dialog style, no internet search).
    /// </summary>
    [RelayCommand]
    public void LaunchOrRun()
    {
        if (SelectedLaunchItem != null)
            _launchService.Launch(SelectedLaunchItem.Model);
        else if (IsSearchActive)
            _launchService.RunQuery(SearchQuery);
    }

    /// <summary>
    /// Toggle the overlay visibility.
    /// </summary>
    [RelayCommand]
    public void ToggleOverlay()
    {
        IsOverlayVisible = !IsOverlayVisible;
        if (IsOverlayVisible)
        {
            RefreshWindows();
            RefreshAvailableProcesses();
        }
    }

    /// <summary>
    /// Switch to the selected window and hide overlay.
    /// If the window is on a different virtual desktop, moves it to the current desktop first.
    /// </summary>
    [RelayCommand]
    public void SwitchToWindow()
    {
        if (SelectedWindow == null) return;

        var handle = SelectedWindow.Model.Handle;

        // Move window to current desktop if it's on another one
        if (!SelectedWindow.IsOnCurrentDesktop)
        {
            _virtualDesktopService.SwitchToWindowDesktop(handle);
        }

        // Focus is applied by App.HideOverlayAndSwitchTo after the overlay has hidden,
        // so that Windows does not reassign focus when HideOverlay fires.
        IsOverlayVisible = false;
    }

    /// <summary>
    /// Closes the specified window (or the currently selected one) via Win32.
    /// </summary>
    public void CloseWindow(WindowItemViewModel? target = null)
    {
        var w = target ?? SelectedWindow;
        if (w == null) return;

        NativeMethods.PostMessage(w.Model.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        // Remove from visible list immediately for snappy UX
        var idx = Windows.IndexOf(w);
        Windows.Remove(w);
        if (Windows.Count > 0)
            SelectedWindow = Windows[Math.Min(idx, Windows.Count - 1)];
        else
            SelectedWindow = null;

        HasNoWindows = Windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WindowsRefreshed?.Invoke();
    }

    /// <summary>
    /// Minimizes the specified window (or the currently selected one).
    /// </summary>
    public void MinimizeWindow(WindowItemViewModel? target = null)
    {
        var w = target ?? SelectedWindow;
        if (w == null) return;

        NativeMethods.ShowWindow(w.Model.Handle, NativeMethods.SW_MINIMIZE);
        w.IsMinimized = true;
    }

    /// <summary>
    /// Assigns a window to a folder by process name (creates a smart folder filter).
    /// If the target is the All Windows folder, no-op.
    /// </summary>
    public void AssignWindowToFolder(WindowItemViewModel window, FolderItemViewModel folder)
    {
        if (folder.Model.Type == FolderType.All) return;

        // For manual folders, add the window's handle to the manual collection.
        if (folder.Model.Type == FolderType.Manual)
        {
            folder.Model.ManualWindowHandles.Add(window.Model.Handle);
            // Refresh to immediately show it if we are on that folder
            if (SelectedFolder == folder)
            {
                RefreshWindows();
            }
            return;
        }

        // For any other folder type (SmartProcess, SmartRules, etc.),
        // create a new smart process folder from the dragged window's process.
        CreateSmartFolderFromProcess(window.Model.ProcessName);
    }

    // ── Window Pinning ──────────────────────────────────────────────

    /// <summary>
    /// Toggles the pinned state of a window. Pinned windows appear first
    /// in every folder and survive filtering/search.
    /// </summary>
    public void TogglePinWindow(WindowItemViewModel? target = null)
    {
        var w = target ?? SelectedWindow;
        if (w == null) return;

        var model = w.Model;
        model.IsPinned = !model.IsPinned;

        // Track pinned handles for accurate same-session identification
        if (model.IsPinned)
            _sessionPinnedHandles.Add(model.Handle);
        else
            _sessionPinnedHandles.Remove(model.Handle);

        // Update persisted pins
        if (model.IsPinned)
        {
            // Add pin identifier (use title contains for flexibility)
            var pinId = new PinnedWindowId
            {
                ProcessName = model.ProcessName,
                TitlePattern = $"*{ExtractStableTitlePart(model.Title)}*"
            };
            _settings.PinnedWindows.Add(pinId);
        }
        else
        {
            // Remove matching pin
            _settings.PinnedWindows.RemoveAll(p => p.Matches(model));
        }

        SaveSettings();
        // Re-sort to reflect pin change
        ApplySearchFilter();
    }

    /// <summary>
    /// Extracts a stable, identifying portion of a window title.
    /// Drops the last segment (typically the generic app name like "Visual Studio Code")
    /// and keeps the unique document / project segments to avoid matching other windows
    /// of the same application.
    /// </summary>
    private static string ExtractStableTitlePart(string title)
    {
        var parts = title.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
            return string.Join(" - ", parts[..^1]).Trim(); // drop generic app name
        if (parts.Length == 2)
            return parts[0].Trim(); // use the unique first segment
        return title.Length > 40 ? title[..40] : title;
    }

    // ── Clipboard Commands ──────────────────────────────────────────

    /// <summary>
    /// Selects the Clipboard folder (called by the pinned sidebar button).
    /// </summary>
    [RelayCommand]
    public void SelectClipboardFolder()
    {
        if (ClipboardFolderVM != null)
            SelectedFolder = ClipboardFolderVM;
    }

    /// <summary>
    /// Opens the Windows Recycle Bin via Explorer and hides the overlay.
    /// </summary>
    [RelayCommand]
    public void SelectRecycleBinFolder()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        App.Current.HideOverlay();
    }

    /// <summary>Copies the given item to the system clipboard.</summary>
    [RelayCommand]
    public void CopyClipboardItem(ClipboardItem item)
    {
        if (item == null) return;
        _clipboardService.CopyToClipboard(item);
    }

    /// <summary>
    /// Moves keyboard selection one step down in clipboard history.
    /// Wraps around to the top.
    /// </summary>
    public void NavigateClipboardDown()
    {
        if (ClipboardHistory.Count == 0) return;
        int idx = SelectedClipboardItem == null ? -1 : ClipboardHistory.IndexOf(SelectedClipboardItem);
        SelectedClipboardItem = ClipboardHistory[(idx + 1) % ClipboardHistory.Count];
    }

    /// <summary>
    /// Moves keyboard selection one step up in clipboard history.
    /// Wraps around to the bottom.
    /// </summary>
    public void NavigateClipboardUp()
    {
        if (ClipboardHistory.Count == 0) return;
        int idx = SelectedClipboardItem == null ? 0 : ClipboardHistory.IndexOf(SelectedClipboardItem);
        SelectedClipboardItem = ClipboardHistory[(idx - 1 + ClipboardHistory.Count) % ClipboardHistory.Count];
    }

    /// <summary>
    /// Copies the currently selected clipboard item and shows a success toast.
    /// Called by Enter, Ctrl+C, and click events.
    /// </summary>
    public void CopySelectedClipboardItem()
    {
        if (SelectedClipboardItem == null) return;
        _clipboardService.CopyToClipboard(SelectedClipboardItem);
        ShowClipboardCopiedToast();
    }

    /// <summary>
    /// Shows the "elemento copiado con éxito" toast for 2.5 seconds.
    /// </summary>
    private void ShowClipboardCopiedToast()
    {
        ClipboardCopiedToastVisible = true;
        _toastTimer?.Dispose();
        _toastTimer = new System.Threading.Timer(_ =>
        {
            _dispatcherQueue?.TryEnqueue(() => ClipboardCopiedToastVisible = false);
        }, null, dueTime: 2500, period: System.Threading.Timeout.Infinite);
    }


    /// <summary>
    /// Removes a single item from clipboard history.
    /// </summary>
    [RelayCommand]
    public void RemoveClipboardItem(ClipboardItem item)
    {
        if (item == null) return;
        _clipboardService.RemoveItem(item);
    }

    /// <summary>
    /// Clears all clipboard history.
    /// </summary>
    [RelayCommand]
    public void ClearClipboardHistory()
    {
        _clipboardService.ClearHistory();
    }

    /// <summary>
    /// Starts clipboard monitoring. Must be called with the overlay HWND.
    /// </summary>
    public void StartClipboardService(IntPtr hwnd, DispatcherQueue? dispatcherQueue = null)
    {
        _dispatcherQueue = dispatcherQueue;
        if (_settings.ClipboardHistoryEnabled)
        {
            _clipboardService.Start(hwnd, _settings.ClipboardHistoryMaxItems, dispatcherQueue);
        }
    }

    /// <summary>
    /// Process a Win32 message for clipboard updates.
    /// Returns true if handled.
    /// </summary>
    public bool ProcessClipboardMessage(uint msg)
    {
        return _clipboardService.ProcessMessage(msg);
    }

    // ── Smart Rules Folder Commands ─────────────────────────────────

    /// <summary>
    /// Adds a rule condition to the editing list.
    /// </summary>
    [RelayCommand]
    public void AddRuleCondition()
    {
        if (string.IsNullOrWhiteSpace(NewRuleValue)) return;

        var field = Enum.TryParse<RuleField>(NewRuleField, out var f) ? f : RuleField.ProcessName;
        var comp = Enum.TryParse<RuleComparison>(NewRuleComparison, out var c) ? c : RuleComparison.Equals;

        EditingRuleConditions.Add(new FolderRuleCondition
        {
            Field = field,
            Comparison = comp,
            Value = NewRuleValue
        });

        NewRuleValue = string.Empty;
    }

    /// <summary>
    /// Removes a rule condition from the editing list.
    /// </summary>
    [RelayCommand]
    public void RemoveRuleCondition(FolderRuleCondition condition)
    {
        EditingRuleConditions.Remove(condition);
    }

    /// <summary>
    /// Opens the Settings panel pre-filled with current saved values.
    /// </summary>
    [RelayCommand]
    public void ShowSettingsPanel()
    {
        var a = _settings.Appearance;
        SettingsAccentColor = a.AccentColor;
        SettingsAccentDimColor = a.AccentDimColor;
        SettingsAccentSubtleColor = a.AccentSubtleColor;
        SettingsBackgroundColor = a.BackgroundColor;
        SettingsSurfaceColor = a.SurfaceColor;
        SettingsCardColor = a.CardColor;
        SettingsBorderColor = a.BorderColor;
        SettingsTextPrimaryColor = a.TextPrimaryColor;
        SettingsTextSecondaryColor = a.TextSecondaryColor;
        SettingsTextMutedColor = a.TextMutedColor;
        SettingsDangerColor = a.DangerColor;
        // Migrate legacy neon-green hover colors to the current accent so color pickers
        // and the live preview are always consistent with the rest of the theme.
        SettingsFolderHoverColor           = ResolveHoverFromAccent(a.FolderHoverColor,           a.AccentColor, 0x1A);
        SettingsFolderSelectedColor        = ResolveHoverFromAccent(a.FolderSelectedColor,        a.AccentColor, 0x29);
        SettingsWindowHoverBorderColor     = ResolveHoverFromAccent(a.WindowHoverBorderColor,     a.AccentColor, 0x66);
        SettingsWindowHoverBackgroundColor = ResolveHoverFromAccent(a.WindowHoverBackgroundColor, a.AccentColor, 0x0C);
        SettingsRunAtStartup = _settingsService.GetRunAtStartup();
        ActiveSettingsTab = "General";
        IsSettingsPanelVisible = true;
    }

    /// <summary>
    /// If a hover color still has the neon-green RGB (39FF14) from the old defaults but the
    /// accent has changed, derive a new hover color from the actual accent using the given alpha.
    /// </summary>
    private static string ResolveHoverFromAccent(string hoverHex, string accentHex, byte alpha)
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

    /// <summary>
    /// Saves the appearance settings and applies them.
    /// </summary>
    [RelayCommand]
    public void SaveSettings_UI()
    {
        _settings.Appearance.AccentColor = SettingsAccentColor;
        _settings.Appearance.AccentDimColor = SettingsAccentDimColor;
        _settings.Appearance.AccentSubtleColor = SettingsAccentSubtleColor;
        _settings.Appearance.BackgroundColor = SettingsBackgroundColor;
        _settings.Appearance.SurfaceColor = SettingsSurfaceColor;
        _settings.Appearance.CardColor = SettingsCardColor;
        _settings.Appearance.BorderColor = SettingsBorderColor;
        _settings.Appearance.TextPrimaryColor = SettingsTextPrimaryColor;
        _settings.Appearance.TextSecondaryColor = SettingsTextSecondaryColor;
        _settings.Appearance.TextMutedColor = SettingsTextMutedColor;
        _settings.Appearance.DangerColor = SettingsDangerColor;
        _settings.Appearance.FolderHoverColor = SettingsFolderHoverColor;
        _settings.Appearance.FolderSelectedColor = SettingsFolderSelectedColor;
        _settings.Appearance.WindowHoverBorderColor = SettingsWindowHoverBorderColor;
        _settings.Appearance.WindowHoverBackgroundColor = SettingsWindowHoverBackgroundColor;
        _settings.RunAtStartup = SettingsRunAtStartup;
        _settingsService.SetRunAtStartup(SettingsRunAtStartup);
        SaveSettings();
        IsSettingsPanelVisible = false;

        // Notify the view to apply refreshed theme colors
        AppearanceChanged?.Invoke();
    }

    // ── Settings Navigation ──

    [ObservableProperty]
    private string _activeSettingsTab = "Themes";

    partial void OnActiveSettingsTabChanged(string value)
    {
        OnPropertyChanged(nameof(TabGeneralVisible));
        OnPropertyChanged(nameof(TabThemesVisible));
        OnPropertyChanged(nameof(TabAccentVisible));
        OnPropertyChanged(nameof(TabBgVisible));
        OnPropertyChanged(nameof(TabTextVisible));
        OnPropertyChanged(nameof(TabStatusVisible));
    }

    public Microsoft.UI.Xaml.Visibility TabGeneralVisible => ActiveSettingsTab == "General" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TabThemesVisible  => ActiveSettingsTab == "Themes"  ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TabAccentVisible => ActiveSettingsTab == "Accent" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TabBgVisible     => ActiveSettingsTab == "Bg"     ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TabTextVisible   => ActiveSettingsTab == "Text"   ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TabStatusVisible => ActiveSettingsTab == "Status" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// Resets all appearance settings to defaults.
    /// </summary>
    [RelayCommand]
    public void ResetAppearanceDefaults()
    {
        var d = AppearanceSettings.Default();
        SettingsAccentColor = d.AccentColor;
        SettingsAccentDimColor = d.AccentDimColor;
        SettingsAccentSubtleColor = d.AccentSubtleColor;
        SettingsBackgroundColor = d.BackgroundColor;
        SettingsSurfaceColor = d.SurfaceColor;
        SettingsCardColor = d.CardColor;
        SettingsBorderColor = d.BorderColor;
        SettingsTextPrimaryColor = d.TextPrimaryColor;
        SettingsTextSecondaryColor = d.TextSecondaryColor;
        SettingsTextMutedColor = d.TextMutedColor;
        SettingsDangerColor = d.DangerColor;
        SettingsFolderHoverColor = d.FolderHoverColor;
        SettingsFolderSelectedColor = d.FolderSelectedColor;
        SettingsWindowHoverBorderColor = d.WindowHoverBorderColor;
        SettingsWindowHoverBackgroundColor = d.WindowHoverBackgroundColor;
    }

    [RelayCommand]
    public void ResetAccentSection()
    {
        var d = AppearanceSettings.Default();
        SettingsAccentColor       = d.AccentColor;
        SettingsAccentDimColor    = d.AccentDimColor;
        SettingsAccentSubtleColor = d.AccentSubtleColor;
    }

    [RelayCommand]
    public void ResetBgSection()
    {
        var d = AppearanceSettings.Default();
        SettingsBackgroundColor     = d.BackgroundColor;
        SettingsSurfaceColor        = d.SurfaceColor;
        SettingsCardColor           = d.CardColor;
        SettingsBorderColor         = d.BorderColor;
        SettingsFolderHoverColor    = d.FolderHoverColor;
        SettingsFolderSelectedColor = d.FolderSelectedColor;
        SettingsWindowHoverBorderColor = d.WindowHoverBorderColor;
        SettingsWindowHoverBackgroundColor = d.WindowHoverBackgroundColor;
    }

    [RelayCommand]
    public void ResetTextSection()
    {
        var d = AppearanceSettings.Default();
        SettingsTextPrimaryColor   = d.TextPrimaryColor;
        SettingsTextSecondaryColor = d.TextSecondaryColor;
        SettingsTextMutedColor     = d.TextMutedColor;
    }

    [RelayCommand]
    public void ResetStatusSection()
    {
        var d = AppearanceSettings.Default();
        SettingsDangerColor = d.DangerColor;
    }

    /// <summary>
    /// Cancels settings editing and reverts the live preview to saved values.
    /// </summary>
    [RelayCommand]
    public void CancelSettings()
    {
        IsSettingsPanelVisible = false;
        // Revert any live preview changes
        AppearanceChanged?.Invoke();
    }

    [RelayCommand]
    public void ExitApplication()
    {
        IsSettingsPanelVisible = false;
        App.Current.ExitApplication();
    }

    /// <summary>
    /// Fired when a Settings* editing property changes so the view can preview the color live.
    /// </summary>
    public event Action? AppearancePreviewChanged;

    /// <summary>
    /// Saves the current editing colors as a new custom preset.
    /// </summary>
    [RelayCommand]
    public void SaveCustomPreset()
    {
        var name = (NewPresetName ?? "").Trim();
        if (string.IsNullOrEmpty(name)) name = $"Custom {CustomPresets.Count + 1}";

        var preset = new ThemePreset
        {
            Name = name,
            Appearance = new AppearanceSettings
            {
                AccentColor       = SettingsAccentColor,
                AccentDimColor    = SettingsAccentDimColor,
                AccentSubtleColor = SettingsAccentSubtleColor,
                BackgroundColor   = SettingsBackgroundColor,
                SurfaceColor      = SettingsSurfaceColor,
                CardColor         = SettingsCardColor,
                BorderColor       = SettingsBorderColor,
                TextPrimaryColor  = SettingsTextPrimaryColor,
                TextSecondaryColor = SettingsTextSecondaryColor,
                TextMutedColor    = SettingsTextMutedColor,
                DangerColor       = SettingsDangerColor,
                FolderHoverColor            = SettingsFolderHoverColor,
                FolderSelectedColor         = SettingsFolderSelectedColor,
                WindowHoverBorderColor      = SettingsWindowHoverBorderColor,
                WindowHoverBackgroundColor  = SettingsWindowHoverBackgroundColor,
            }
        };

        CustomPresets.Add(preset);
        _settings.CustomPresets.Add(preset);
        _settingsService.Save(_settings);
        NewPresetName = string.Empty;
    }

    /// <summary>
    /// Removes a user-created custom preset.
    /// </summary>
    [RelayCommand]
    public void DeleteCustomPreset(ThemePreset preset)
    {
        CustomPresets.Remove(preset);
        _settings.CustomPresets.Remove(preset);
        _settingsService.Save(_settings);
    }

    /// <summary>
    /// Applies a theme preset to the settings fields (does not save — user still presses Save).
    /// </summary>
    [RelayCommand]
    public void ApplyPreset(ThemePreset preset)
    {
        SettingsAccentColor = preset.Appearance.AccentColor;
        SettingsAccentDimColor = preset.Appearance.AccentDimColor;
        SettingsAccentSubtleColor = preset.Appearance.AccentSubtleColor;
        SettingsBackgroundColor = preset.Appearance.BackgroundColor;
        SettingsSurfaceColor = preset.Appearance.SurfaceColor;
        SettingsCardColor = preset.Appearance.CardColor;
        SettingsBorderColor = preset.Appearance.BorderColor;
        SettingsTextPrimaryColor = preset.Appearance.TextPrimaryColor;
        SettingsTextSecondaryColor = preset.Appearance.TextSecondaryColor;
        SettingsTextMutedColor = preset.Appearance.TextMutedColor;
        SettingsDangerColor = preset.Appearance.DangerColor;
        // Resolve hover colors: migrate any neon-green defaults to the preset's accent
        SettingsFolderHoverColor           = ResolveHoverFromAccent(preset.Appearance.FolderHoverColor,           preset.Appearance.AccentColor, 0x1A);
        SettingsFolderSelectedColor        = ResolveHoverFromAccent(preset.Appearance.FolderSelectedColor,        preset.Appearance.AccentColor, 0x29);
        SettingsWindowHoverBorderColor     = ResolveHoverFromAccent(preset.Appearance.WindowHoverBorderColor,     preset.Appearance.AccentColor, 0x66);
        SettingsWindowHoverBackgroundColor = ResolveHoverFromAccent(preset.Appearance.WindowHoverBackgroundColor, preset.Appearance.AccentColor, 0x0C);
    }

    /// <summary>
    /// 22 curated built-in theme presets.
    /// </summary>
    public static readonly List<ThemePreset> Presets = new()
    {
        // ── Original 7 ────────────────────────────────────────────
        new ThemePreset { Name = "Neon Green", Appearance = new() {
            AccentColor="#39FF14", AccentDimColor="#1A8A0A", AccentSubtleColor="#0D3D06",
            BackgroundColor="#000000", SurfaceColor="#0A0A0A", CardColor="#111111",
            BorderColor="#1A1A1A", TextPrimaryColor="#FFFFFF", TextSecondaryColor="#AAAAAA",
            TextMutedColor="#666666", DangerColor="#FF3344",
            FolderHoverColor="#1A39FF14", FolderSelectedColor="#2939FF14",
            WindowHoverBorderColor="#6639FF14", WindowHoverBackgroundColor="#0C39FF14" } },

        new ThemePreset { Name = "Cyber Blue", Appearance = new() {
            AccentColor="#00D4FF", AccentDimColor="#0080BB", AccentSubtleColor="#003A55",
            BackgroundColor="#010813", SurfaceColor="#060E1F", CardColor="#0D1529",
            BorderColor="#162040", TextPrimaryColor="#E8F4FF", TextSecondaryColor="#8AB8D4",
            TextMutedColor="#4A7A99", DangerColor="#FF4466",
            FolderHoverColor="#1A00D4FF", FolderSelectedColor="#2900D4FF",
            WindowHoverBorderColor="#6600D4FF", WindowHoverBackgroundColor="#0C00D4FF" } },

        new ThemePreset { Name = "Deep Purple", Appearance = new() {
            AccentColor="#BB86FC", AccentDimColor="#7B50B0", AccentSubtleColor="#3D2060",
            BackgroundColor="#050508", SurfaceColor="#0D0B14", CardColor="#14101E",
            BorderColor="#2C2040", TextPrimaryColor="#EEEEEE", TextSecondaryColor="#B0A8C8",
            TextMutedColor="#6B6080", DangerColor="#CF6679",
            FolderHoverColor="#1ABB86FC", FolderSelectedColor="#29BB86FC",
            WindowHoverBorderColor="#66BB86FC", WindowHoverBackgroundColor="#0CBB86FC" } },

        new ThemePreset { Name = "Crimson", Appearance = new() {
            AccentColor="#FF4444", AccentDimColor="#AA1111", AccentSubtleColor="#440806",
            BackgroundColor="#070000", SurfaceColor="#0F0505", CardColor="#180A0A",
            BorderColor="#2D1010", TextPrimaryColor="#FFE8E8", TextSecondaryColor="#CC9999",
            TextMutedColor="#885555", DangerColor="#FF6B00",
            FolderHoverColor="#1AFF4444", FolderSelectedColor="#29FF4444",
            WindowHoverBorderColor="#66FF4444", WindowHoverBackgroundColor="#0CFF4444" } },

        new ThemePreset { Name = "Amber Gold", Appearance = new() {
            AccentColor="#FFB300", AccentDimColor="#A07000", AccentSubtleColor="#4A3200",
            BackgroundColor="#060400", SurfaceColor="#100C00", CardColor="#1A1400",
            BorderColor="#2D2200", TextPrimaryColor="#FFF8E8", TextSecondaryColor="#CCA855",
            TextMutedColor="#886633", DangerColor="#FF4444",
            FolderHoverColor="#1AFFB300", FolderSelectedColor="#29FFB300",
            WindowHoverBorderColor="#66FFB300", WindowHoverBackgroundColor="#0CFFB300" } },

        new ThemePreset { Name = "Arctic", Appearance = new() {
            AccentColor="#64D8CB", AccentDimColor="#2A9990", AccentSubtleColor="#0D4440",
            BackgroundColor="#04080C", SurfaceColor="#080E14", CardColor="#0D1720",
            BorderColor="#152030", TextPrimaryColor="#E8F8FF", TextSecondaryColor="#8ABCCC",
            TextMutedColor="#4A7A8A", DangerColor="#FF5370",
            FolderHoverColor="#1A64D8CB", FolderSelectedColor="#2964D8CB",
            WindowHoverBorderColor="#6664D8CB", WindowHoverBackgroundColor="#0C64D8CB" } },

        new ThemePreset { Name = "Monochrome", Appearance = new() {
            AccentColor="#DDDDDD", AccentDimColor="#888888", AccentSubtleColor="#333333",
            BackgroundColor="#000000", SurfaceColor="#0C0C0C", CardColor="#141414",
            BorderColor="#222222", TextPrimaryColor="#FFFFFF", TextSecondaryColor="#999999",
            TextMutedColor="#555555", DangerColor="#CC3333",
            FolderHoverColor="#1ADDDDDD", FolderSelectedColor="#29DDDDDD",
            WindowHoverBorderColor="#66DDDDDD", WindowHoverBackgroundColor="#0CDDDDDD" } },

        // ── New 15 ────────────────────────────────────────────────
        // 01 Dark Modern (Cian)
        new ThemePreset { Name = "Dark Cian", Appearance = new() {
            AccentColor="#00ADB5", AccentDimColor="#007A80", AccentSubtleColor="#003840",
            BackgroundColor="#222831", SurfaceColor="#1A1E26", CardColor="#2C3140",
            BorderColor="#454D5C", TextPrimaryColor="#EEEEEE", TextSecondaryColor="#AAAAAA",
            TextMutedColor="#666D7A", DangerColor="#FF5252",
            FolderHoverColor="#1A00ADB5", FolderSelectedColor="#2900ADB5",
            WindowHoverBorderColor="#6600ADB5", WindowHoverBackgroundColor="#0C00ADB5" } },

        // 02 Soft Ice Blue (light)
        new ThemePreset { Name = "Ice Blue", Appearance = new() {
            AccentColor="#71C9CE", AccentDimColor="#4AA5AA", AccentSubtleColor="#C0ECEF",
            BackgroundColor="#E3FDFD", SurfaceColor="#CBF1F5", CardColor="#D8F7F8",
            BorderColor="#A6E3E9", TextPrimaryColor="#1A3A40", TextSecondaryColor="#2E5E65",
            TextMutedColor="#4A8A92", DangerColor="#E05050",
            FolderHoverColor="#1A71C9CE", FolderSelectedColor="#2971C9CE",
            WindowHoverBorderColor="#6671C9CE", WindowHoverBackgroundColor="#0C71C9CE" } },

        // 03 Corporate Blue (light)
        new ThemePreset { Name = "Corporate", Appearance = new() {
            AccentColor="#3F72AF", AccentDimColor="#2A5A9A", AccentSubtleColor="#C5D5E8",
            BackgroundColor="#F9F7F7", SurfaceColor="#EEF1F8", CardColor="#DBE2EF",
            BorderColor="#B8C5DA", TextPrimaryColor="#112D4E", TextSecondaryColor="#2A4D6E",
            TextMutedColor="#6A849A", DangerColor="#CC3333",
            FolderHoverColor="#1A3F72AF", FolderSelectedColor="#293F72AF",
            WindowHoverBorderColor="#663F72AF", WindowHoverBackgroundColor="#0C3F72AF" } },

        // 04 Earthy Tones / Nude
        new ThemePreset { Name = "Earthy", Appearance = new() {
            AccentColor="#E3CAA5", AccentDimColor="#CEAB93", AccentSubtleColor="#6A5040",
            BackgroundColor="#1E1510", SurfaceColor="#2A1F18", CardColor="#352820",
            BorderColor="#4A3828", TextPrimaryColor="#FFFBE9", TextSecondaryColor="#E8D5B8",
            TextMutedColor="#AD8B73", DangerColor="#C04040",
            FolderHoverColor="#1AE3CAA5", FolderSelectedColor="#29E3CAA5",
            WindowHoverBorderColor="#66E3CAA5", WindowHoverBackgroundColor="#0CE3CAA5" } },

        // 05 Pastel Lavender (light)
        new ThemePreset { Name = "Lavender", Appearance = new() {
            AccentColor="#424874", AccentDimColor="#5A5E9A", AccentSubtleColor="#C0C4E2",
            BackgroundColor="#F4EEFF", SurfaceColor="#EBE5FF", CardColor="#DCD6F7",
            BorderColor="#C5BCE8", TextPrimaryColor="#2A2C50", TextSecondaryColor="#424874",
            TextMutedColor="#A6B1E1", DangerColor="#CC3355",
            FolderHoverColor="#1A424874", FolderSelectedColor="#29424874",
            WindowHoverBorderColor="#66424874", WindowHoverBackgroundColor="#0C424874" } },

        // 06 Sunset / Warm
        new ThemePreset { Name = "Sunset", Appearance = new() {
            AccentColor="#F9ED69", AccentDimColor="#F08A5D", AccentSubtleColor="#5A2040",
            BackgroundColor="#1A0A14", SurfaceColor="#261420", CardColor="#30182A",
            BorderColor="#441830", TextPrimaryColor="#FFF5E0", TextSecondaryColor="#F4C880",
            TextMutedColor="#B83B5E", DangerColor="#FF4455",
            FolderHoverColor="#1AF9ED69", FolderSelectedColor="#29F9ED69",
            WindowHoverBorderColor="#66F9ED69", WindowHoverBackgroundColor="#0CF9ED69" } },

        // 07 Cyberpunk / High Contrast
        new ThemePreset { Name = "Cyberpunk", Appearance = new() {
            AccentColor="#08D9D6", AccentDimColor="#0599A0", AccentSubtleColor="#0A3040",
            BackgroundColor="#252A34", SurfaceColor="#1A1E27", CardColor="#1E2332",
            BorderColor="#2E3545", TextPrimaryColor="#EAEAEA", TextSecondaryColor="#B0B8CC",
            TextMutedColor="#6A7588", DangerColor="#FF2E63",
            FolderHoverColor="#1A08D9D6", FolderSelectedColor="#2908D9D6",
            WindowHoverBorderColor="#6608D9D6", WindowHoverBackgroundColor="#0C08D9D6" } },

        // 08 Deep Ocean
        new ThemePreset { Name = "Deep Ocean", Appearance = new() {
            AccentColor="#3282B8", AccentDimColor="#1F5A8A", AccentSubtleColor="#0A2840",
            BackgroundColor="#1B262C", SurfaceColor="#0F2030", CardColor="#142840",
            BorderColor="#0F4C75", TextPrimaryColor="#BBE1FA", TextSecondaryColor="#7AB8E0",
            TextMutedColor="#3A7AA8", DangerColor="#FF5050",
            FolderHoverColor="#1A3282B8", FolderSelectedColor="#293282B8",
            WindowHoverBorderColor="#663282B8", WindowHoverBackgroundColor="#0C3282B8" } },

        // 09 Summer Pop / Candy (light)
        new ThemePreset { Name = "Summer Pop", Appearance = new() {
            AccentColor="#95E1D3", AccentDimColor="#5CBFB0", AccentSubtleColor="#C0F0EC",
            BackgroundColor="#FFF8F5", SurfaceColor="#FFF0EE", CardColor="#FFE5E5",
            BorderColor="#F4C5C5", TextPrimaryColor="#3A2A2A", TextSecondaryColor="#C05050",
            TextMutedColor="#F38181", DangerColor="#E84040",
            FolderHoverColor="#1A95E1D3", FolderSelectedColor="#2995E1D3",
            WindowHoverBorderColor="#6695E1D3", WindowHoverBackgroundColor="#0C95E1D3" } },

        // 10 Dark Wine / Crimson
        new ThemePreset { Name = "Dark Wine", Appearance = new() {
            AccentColor="#E84545", AccentDimColor="#A02A2A", AccentSubtleColor="#50182A",
            BackgroundColor="#2B2E4A", SurfaceColor="#232640", CardColor="#2E3155",
            BorderColor="#3A3D60", TextPrimaryColor="#F0E8EC", TextSecondaryColor="#C8A0AA",
            TextMutedColor="#806070", DangerColor="#FF2244",
            FolderHoverColor="#1AE84545", FolderSelectedColor="#29E84545",
            WindowHoverBorderColor="#66E84545", WindowHoverBackgroundColor="#0CE84545" } },

        // 11 Industrial Yellow (light)
        new ThemePreset { Name = "Industrial", Appearance = new() {
            AccentColor="#F4CE14", AccentDimColor="#C8A800", AccentSubtleColor="#F0E090",
            BackgroundColor="#F5F7F8", SurfaceColor="#EAEEF0", CardColor="#DEE3E5",
            BorderColor="#C8CDD0", TextPrimaryColor="#45474B", TextSecondaryColor="#697070",
            TextMutedColor="#8A9090", DangerColor="#CC3333",
            FolderHoverColor="#1AF4CE14", FolderSelectedColor="#29F4CE14",
            WindowHoverBorderColor="#66F4CE14", WindowHoverBackgroundColor="#0CF4CE14" } },

        // 12 Pure Black & Red Blood
        new ThemePreset { Name = "Blood Red", Appearance = new() {
            AccentColor="#FF0000", AccentDimColor="#950101", AccentSubtleColor="#3D0000",
            BackgroundColor="#000000", SurfaceColor="#100000", CardColor="#1A0000",
            BorderColor="#3D0000", TextPrimaryColor="#FF4444", TextSecondaryColor="#CC2222",
            TextMutedColor="#881111", DangerColor="#FF6600",
            FolderHoverColor="#1AFF0000", FolderSelectedColor="#29FF0000",
            WindowHoverBorderColor="#66FF0000", WindowHoverBackgroundColor="#0CFF0000" } },

        // 13 Fire & Steel
        new ThemePreset { Name = "Fire & Steel", Appearance = new() {
            AccentColor="#FFD460", AccentDimColor="#F07B3F", AccentSubtleColor="#4A3010",
            BackgroundColor="#2D4059", SurfaceColor="#22334A", CardColor="#1A2838",
            BorderColor="#3A5070", TextPrimaryColor="#F0EDE8", TextSecondaryColor="#C8B890",
            TextMutedColor="#8A7A60", DangerColor="#EA5455",
            FolderHoverColor="#1AFFD460", FolderSelectedColor="#29FFD460",
            WindowHoverBorderColor="#66FFD460", WindowHoverBackgroundColor="#0CFFD460" } },

        // 14 Forest / Nature
        new ThemePreset { Name = "Forest", Appearance = new() {
            AccentColor="#7FBF00", AccentDimColor="#427A43", AccentSubtleColor="#1A3A18",
            BackgroundColor="#010F02", SurfaceColor="#0A180A", CardColor="#122014",
            BorderColor="#1A3020", TextPrimaryColor="#F2E3BB", TextSecondaryColor="#C0B87A",
            TextMutedColor="#808060", DangerColor="#CC4422",
            FolderHoverColor="#1A7FBF00", FolderSelectedColor="#297FBF00",
            WindowHoverBorderColor="#667FBF00", WindowHoverBackgroundColor="#0C7FBF00" } },

        // 15 Midnight Rose (extra)
        new ThemePreset { Name = "Midnight Rose", Appearance = new() {
            AccentColor="#FF6FA6", AccentDimColor="#CC3A70", AccentSubtleColor="#60112E",
            BackgroundColor="#0A0A12", SurfaceColor="#12121E", CardColor="#1A1A2A",
            BorderColor="#282838", TextPrimaryColor="#FFE8F0", TextSecondaryColor="#CCA0B4",
            TextMutedColor="#806070", DangerColor="#FF4400",
            FolderHoverColor="#1AFF6FA6", FolderSelectedColor="#29FF6FA6",
            WindowHoverBorderColor="#66FF6FA6", WindowHoverBackgroundColor="#0CFF6FA6" } },
    };

    /// <summary>
    /// Fired when appearance settings are saved. The View uses this to update resource brushes.
    /// </summary>
    public event Action? AppearanceChanged;

    /// <summary>
    /// Navigate folders up (wraps from first to last). Skips the Clipboard folder — that
    /// is only reachable via its dedicated pinned button.
    /// </summary>
    [RelayCommand]
    public void NavigateFolderUp()
    {
        if (NonClipboardFolders.Count == 0) return;

        var idx = NonClipboardFolders.IndexOf(SelectedFolder!);
        // If clipboard (or nothing) is currently selected, jump to the last regular folder
        if (idx < 0) idx = 0;
        SelectedFolder = NonClipboardFolders[(idx - 1 + NonClipboardFolders.Count) % NonClipboardFolders.Count];
    }

    /// <summary>
    /// Navigate folders down (wraps from last to first). Skips the Clipboard folder — that
    /// is only reachable via its dedicated pinned button.
    /// </summary>
    [RelayCommand]
    public void NavigateFolderDown()
    {
        if (NonClipboardFolders.Count == 0) return;

        var idx = NonClipboardFolders.IndexOf(SelectedFolder!);
        // If clipboard (or nothing) is currently selected, jump to the first regular folder
        if (idx < 0) idx = -1;
        SelectedFolder = NonClipboardFolders[(idx + 1) % NonClipboardFolders.Count];
    }

    /// <summary>
    /// Navigate windows left.
    /// </summary>
    [RelayCommand]
    public void NavigateWindowLeft()
    {
        if (SelectedWindow == null || Windows.Count == 0) return;

        var idx = Windows.IndexOf(SelectedWindow);
        if (idx > 0)
        {
            SelectedWindow = Windows[idx - 1];
        }
    }

    /// <summary>
    /// Navigate windows right.
    /// </summary>
    [RelayCommand]
    public void NavigateWindowRight()
    {
        if (SelectedWindow == null || Windows.Count == 0) return;

        var idx = Windows.IndexOf(SelectedWindow);
        if (idx < Windows.Count - 1)
        {
            SelectedWindow = Windows[idx + 1];
        }
    }

    /// <summary>
    /// Navigate windows up (grid row above — move left by the number of columns).
    /// Preserves current column when possible, clamping if the target row is shorter.
    /// </summary>
    [RelayCommand]
    public void NavigateWindowUp()
    {
        if (SelectedWindow == null || Windows.Count == 0) return;

        var idx = Windows.IndexOf(SelectedWindow);
        int cols = Math.Max(1, _windowGridColumnCount);
        int currentRow = idx / cols;
        int currentCol = idx % cols;
        int targetRow = currentRow - 1;
        if (targetRow < 0) return;

        int targetStart = targetRow * cols;
        int targetEnd = Math.Min(targetStart + cols - 1, Windows.Count - 1);
        int newIdx = Math.Min(targetStart + currentCol, targetEnd);
        SelectedWindow = Windows[newIdx];
    }

    /// <summary>
    /// Navigate windows down (grid row below).
    /// </summary>
    [RelayCommand]
    public void NavigateWindowDown()
    {
        if (SelectedWindow == null || Windows.Count == 0) return;

        var idx = Windows.IndexOf(SelectedWindow);
        int cols = Math.Max(1, _windowGridColumnCount);
        int currentRow = idx / cols;
        int currentCol = idx % cols;
        int targetRow = currentRow + 1;

        int targetStart = targetRow * cols;
        if (targetStart >= Windows.Count) return;

        int targetEnd = Math.Min(targetStart + cols - 1, Windows.Count - 1);
        int newIdx = Math.Min(targetStart + currentCol, targetEnd);
        SelectedWindow = Windows[newIdx];
    }

    /// <summary>
    /// Updates the live column count used for vertical keyboard navigation.
    /// </summary>
    public void SetWindowGridColumnCount(int columns)
    {
        _windowGridColumnCount = Math.Max(1, columns);
    }

    /// <summary>
    /// Provides the overlay window handle so VirtualDesktopService can reliably
    /// determine the current virtual desktop at window-enumeration time.
    /// </summary>
    public void SetOverlayHwnd(IntPtr hwnd)
    {
        _virtualDesktopService.SetOwnerHwnd(hwnd);
    }

    /// <summary>
    /// Show the add folder panel.
    /// </summary>
    [RelayCommand]
    public void ShowAddFolderPanel()
    {
        NewFolderName = string.Empty;
        IsSmartFolder = false;
        IsSmartRulesFolder = false;
        EditingRuleConditions.Clear();
        RuleOperator = "AND";
        NewRuleField = "ProcessName";
        NewRuleComparison = "Contains";
        NewRuleValue = "";
        SelectedProcessFilter = null;
        SelectedIcon = "\uE8B7";
        SelectedBgColor = ""; // No background color by default
        IsAddFolderPanelVisible = true;
        RefreshAvailableProcesses();
    }

    /// <summary>
    /// Hide the add folder panel.
    /// </summary>
    [RelayCommand]
    public void CancelAddFolder()
    {
        IsAddFolderPanelVisible = false;
    }

    /// <summary>
    /// Opens the edit folder panel pre-filled with the folder's current properties.
    /// </summary>
    [RelayCommand]
    public void ShowEditFolderPanel(FolderItemViewModel folder)
    {
        if (folder.Model.Type == FolderType.All || folder.Model.Type == FolderType.Clipboard) return;

        _editingFolder = folder;
        EditFolderName = folder.Name;
        EditFolderIcon = folder.Icon;
        EditFolderBgColor = folder.Model.BackgroundColor;

        // Load composite rules if editing a SmartRules folder
        IsSmartRulesFolder = folder.Model.Type == FolderType.SmartRules;
        EditingRuleConditions.Clear();
        if (folder.Model.Rules != null)
        {
            RuleOperator = folder.Model.Rules.Operator.ToString();
            foreach (var c in folder.Model.Rules.Conditions)
                EditingRuleConditions.Add(new FolderRuleCondition
                {
                    Field = c.Field,
                    Comparison = c.Comparison,
                    Value = c.Value
                });
        }

        // Populate process list first, ensuring the saved process is always selectable
        // even when it is not currently running.
        RefreshAvailableProcesses(ensureProcess: folder.Model.ProcessFilter);

        // Set the smart-process toggle and its selection AFTER the list is ready
        EditFolderIsSmart = folder.Model.Type == FolderType.SmartProcess;
        EditFolderProcessFilter = folder.Model.ProcessFilter;

        IsEditFolderPanelVisible = true;
    }

    /// <summary>
    /// Saves changes to the folder being edited.
    /// </summary>
    [RelayCommand]
    public void SaveEditFolder()
    {
        if (_editingFolder == null || string.IsNullOrWhiteSpace(EditFolderName)) return;

        var model = _editingFolder.Model;
        model.Name = EditFolderName;
        model.Icon = EditFolderIcon;
        model.BackgroundColor = EditFolderBgColor;

        if (IsSmartRulesFolder && EditingRuleConditions.Count > 0)
        {
            model.Type = FolderType.SmartRules;
            model.ProcessFilter = null;
            var op = RuleOperator == "AND" ? Models.RuleOperator.AND : Models.RuleOperator.OR;
            model.Rules = new FolderRuleGroup
            {
                Operator = op,
                Conditions = EditingRuleConditions.ToList()
            };
        }
        else if (EditFolderIsSmart && !string.IsNullOrEmpty(EditFolderProcessFilter))
        {
            model.Type = FolderType.SmartProcess;
            model.ProcessFilter = EditFolderProcessFilter;
            model.Rules = null;
        }
        else
        {
            model.Type = FolderType.Manual;
            model.ProcessFilter = null;
            model.Rules = null;
        }

        EditingRuleConditions.Clear();
        SaveSettings();
        SyncFolders();
        IsEditFolderPanelVisible = false;
        _editingFolder = null;

        // Re-select the edited folder
        if (model != null)
        {
            var updated = Folders.FirstOrDefault(f => f.Model.Id == model.Id);
            if (updated != null) SelectedFolder = updated;
        }
    }

    /// <summary>
    /// Cancels editing a folder.
    /// </summary>
    [RelayCommand]
    public void CancelEditFolder()
    {
        IsEditFolderPanelVisible = false;
        _editingFolder = null;
    }

    /// <summary>
    /// Deletes a specific folder (used by right-click context menu).
    /// </summary>
    [RelayCommand]
    public void DeleteFolder(FolderItemViewModel folder)
    {
        if (folder.Model.Type == FolderType.All ||
            folder.Model.Type == FolderType.Clipboard ||
            folder.Model.Type == FolderType.RecycleBin) return;

        if (_folderService.RemoveFolder(folder.Model.Id))
        {
            SaveSettings();
            SyncFolders();
            if (Folders.Count > 0)
            {
                SelectedFolder = Folders[0];
            }
        }
    }

    /// <summary>
    /// Create a new folder from the panel inputs.
    /// </summary>
    [RelayCommand]
    public void CreateFolder()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName)) return;

        if (IsSmartRulesFolder && EditingRuleConditions.Count > 0)
        {
            // Create a SmartRules folder with composite rules
            var op = RuleOperator == "AND" ? Models.RuleOperator.AND : Models.RuleOperator.OR;
            var ruleGroup = new FolderRuleGroup
            {
                Operator = op,
                Conditions = EditingRuleConditions.ToList()
            };
            _folderService.CreateSmartRulesFolder(NewFolderName, ruleGroup, SelectedIcon, SelectedBgColor);
        }
        else if (IsSmartFolder && !string.IsNullOrEmpty(SelectedProcessFilter))
        {
            _folderService.CreateSmartProcessFolder(NewFolderName, SelectedProcessFilter, SelectedIcon, SelectedBgColor);
        }
        else
        {
            _folderService.CreateManualFolder(NewFolderName, SelectedIcon, SelectedBgColor);
        }

        EditingRuleConditions.Clear();
        SaveSettings();
        SyncFolders();
        IsAddFolderPanelVisible = false;

        // Select the newly created folder
        if (Folders.Count > 0)
        {
            SelectedFolder = Folders[^1];
        }
    }

    /// <summary>
    /// Creates a smart-process folder for the given process name (used by drag-and-drop).
    /// Skips if a folder with the same filter already exists.
    /// </summary>
    public void CreateSmartFolderFromProcess(string processName)
    {
        // Don't create duplicate folders for the same process
        var existing = _folderService.Folders
            .Any(f => f.Type == FolderType.SmartProcess
                && string.Equals(f.ProcessFilter, processName, StringComparison.OrdinalIgnoreCase));
        if (existing) return;

        _folderService.CreateSmartProcessFolder(processName, processName, "\uE8B7");
        SaveSettings();
        SyncFolders();

        // Select the newly created folder
        if (Folders.Count > 0)
            SelectedFolder = Folders[^1];
    }

    /// <summary>
    /// Delete the selected folder. Cannot delete the "All Windows" folder.
    /// </summary>
    [RelayCommand]
    public void DeleteSelectedFolder()
    {
        if (SelectedFolder?.Model == null) return;
        if (SelectedFolder.Model.Type == FolderType.All ||
            SelectedFolder.Model.Type == FolderType.Clipboard ||
            SelectedFolder.Model.Type == FolderType.RecycleBin) return;

        if (_folderService.RemoveFolder(SelectedFolder.Model.Id))
        {
            SaveSettings();
            SyncFolders();
            if (Folders.Count > 0)
            {
                SelectedFolder = Folders[0];
            }
        }
    }

    /// <summary>
    /// Moves a folder from one index to another (used by drag-and-drop).
    /// The "All Windows" folder at index 0 cannot be moved.
    /// </summary>
    public void MoveFolder(int oldIndex, int newIndex)
    {
        // Guard: can't move "All Windows" (index 0) or move into its slot
        if (oldIndex <= 0 || newIndex <= 0) return;
        if (oldIndex == newIndex) return;
        if (oldIndex >= Folders.Count || newIndex >= Folders.Count) return;

        // Move in the FolderService model list
        _folderService.Folders.Move(oldIndex, newIndex);

        // Sync view models
        SyncFolders();
        SaveSettings();

        // Re-select the moved folder
        if (newIndex < Folders.Count)
            SelectedFolder = Folders[newIndex];
    }

    /// <summary>
    /// Syncs the current Folders order back to the FolderService after a drag-and-drop reorder.
    /// Called after ListView's CanReorderItems has already moved items in the ObservableCollection.
    /// </summary>
    public void SyncFolderOrderToService()
    {
        _folderService.Folders.Clear();
        foreach (var vm in Folders)
        {
            _folderService.Folders.Add(vm.Model);
        }
        SaveSettings();
    }

    /// <summary>
    /// Refreshes the list of available processes for the smart folder selector.
    /// </summary>
    private void RefreshAvailableProcesses(string? ensureProcess = null)
    {
        AvailableProcesses.Clear();
        foreach (var name in _windowService.GetRunningProcessNames())
        {
            AvailableProcesses.Add(name);
        }

        // When editing, the saved process may not be running right now — add it anyway
        // so the ComboBox can still display (and keep) the current selection.
        if (!string.IsNullOrEmpty(ensureProcess) &&
            !AvailableProcesses.Contains(ensureProcess, StringComparer.OrdinalIgnoreCase))
        {
            AvailableProcesses.Insert(0, ensureProcess);
        }
    }

    private void SaveSettings()
    {
        _folderService.SaveToSettings(_settings);
        _settingsService.Save(_settings);
    }

    /// <summary>
    /// Exposes the current appearance settings for the View to apply theme updates.
    /// </summary>
    public AppearanceSettings GetAppearanceSettings() => _settings.Appearance;

    /// <summary>
    /// Exposes the static preset list for XAML binding.
    /// </summary>
    public List<ThemePreset> AvailablePresets => Presets;

    // Fire live preview whenever any settings color property changes
    partial void OnSettingsAccentColorChanged(string value)        => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsAccentDimColorChanged(string value)     => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsAccentSubtleColorChanged(string value)  => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsBackgroundColorChanged(string value)    => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsSurfaceColorChanged(string value)       => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsCardColorChanged(string value)          => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsBorderColorChanged(string value)        => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsTextPrimaryColorChanged(string value)   => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsTextSecondaryColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsTextMutedColorChanged(string value)     => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsDangerColorChanged(string value)        => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsFolderHoverColorChanged(string value)    => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsFolderSelectedColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsWindowHoverBorderColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsWindowHoverBackgroundColorChanged(string value) => AppearancePreviewChanged?.Invoke();
}

