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
    private readonly LaunchService _launchService;
    private readonly VirtualDesktopService _virtualDesktopService;
    private readonly ClipboardService _clipboardService;
    private readonly HashSet<IntPtr> _sessionPinnedHandles = new();
    private DispatcherQueue? _dispatcherQueue;
    private System.Threading.Timer? _toastTimer;
    private readonly UpdateService _updateService;

    public ObservableCollection<FolderItemViewModel> Folders { get; } = new();
    /// <summary>All folders excluding the Clipboard folder — bound to the sidebar ListView.</summary>
    public ObservableCollection<FolderItemViewModel> NonClipboardFolders { get; } = new();
    public ObservableCollection<WindowItemViewModel> Windows { get; } = new();
    public ObservableCollection<LaunchItemViewModel> LaunchResults { get; } = new();
    public ObservableCollection<ClipboardItem> ClipboardHistory => _clipboardService.History;
    public ObservableCollection<ClipboardItem> PinnedClipboardItems { get; } = new();

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

    public OnboardingViewModel Onboarding { get; }

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

    public SettingsViewModel Settings { get; }

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

    private bool _isAppSearchMode;
    public bool IsAppSearchMode => _isAppSearchMode;
    public string SearchModeLabel => _isAppSearchMode ? "Apps" : "All";

    public void SetAppSearchMode(bool enabled)
    {
        _isAppSearchMode = enabled;
        OnPropertyChanged(nameof(IsAppSearchMode));
        OnPropertyChanged(nameof(SearchModeLabel));
        OnPropertyChanged(nameof(ShowLaunchSuggestions));
        OnPropertyChanged(nameof(ShowEmptyDefault));
        OnPropertyChanged(nameof(WindowGridVisibility));
        ApplySearchFilter();
    }

    [ObservableProperty]
    private LaunchItemViewModel? _selectedLaunchItem;

    public bool IsSearchActive => !string.IsNullOrEmpty(SearchQuery);
    public Visibility SearchClearVisibility => IsSearchActive ? Visibility.Visible : Visibility.Collapsed;
    public string SearchResultCount => IsSearchActive ? $"{Windows.Count} result{(Windows.Count == 1 ? "" : "s")}" : string.Empty;

    // Launch panel visibility helpers
    public Visibility ShowEmptyDefault      => !IsSearchActive && SelectedFolder != ClipboardFolderVM && !_isAppSearchMode && !ShowRecycleBinSuggestion ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowLaunchSuggestions => IsSearchActive && Windows.Count == 0 && LaunchResults.Count > 0 && !ShowRecycleBinSuggestion ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShowRunFallback       => IsSearchActive && Windows.Count == 0 && LaunchResults.Count == 0 && !_isAppSearchMode && !ShowRecycleBinSuggestion ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    private bool _showRecycleBinSuggestion;
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



    public MainViewModel()
    {
        _windowService = ServiceContainer.Resolve<WindowEnumerationService>();
        _settingsService = ServiceContainer.Resolve<SettingsService>();
        _settings = _settingsService.Load();
        _folderService = ServiceContainer.Resolve<FolderService>();
        _launchService = ServiceContainer.Resolve<LaunchService>();
        _virtualDesktopService = ServiceContainer.Resolve<VirtualDesktopService>();
        _clipboardService = ServiceContainer.Resolve<ClipboardService>();
        _updateService = ServiceContainer.Resolve<UpdateService>();
        Settings = new SettingsViewModel(_settings, _settingsService);
        Settings.AppearanceChanged += () => AppearanceChanged?.Invoke();
        Settings.ClipboardEnabledChanged += OnClipboardEnabledChanged;
        Onboarding = new OnboardingViewModel(_settings, _settingsService);

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

        ShowRecycleBinSuggestion = RecycleBinMatcher.Matches(q);

        if (_isAppSearchMode && !string.IsNullOrEmpty(q))
        {
            Windows.Clear();
            SelectedWindow = null;
            HasNoWindows = Visibility.Visible;

            LaunchResults.Clear();
            foreach (var item in _launchService.Search(q))
                LaunchResults.Add(new LaunchItemViewModel(item));
            SelectedLaunchItem = LaunchResults.Count > 0 ? LaunchResults[0] : null;

            OnPropertyChanged(nameof(ShowEmptyDefault));
            OnPropertyChanged(nameof(ShowLaunchSuggestions));
            OnPropertyChanged(nameof(ShowRunFallback));
            OnPropertyChanged(nameof(RunFallbackLabel));
            OnPropertyChanged(nameof(SearchResultCount));
            WindowsRefreshed?.Invoke();
            return;
        }

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
        if (string.IsNullOrEmpty(value))
        {
            _isAppSearchMode = false;
            OnPropertyChanged(nameof(IsAppSearchMode));
            OnPropertyChanged(nameof(SearchModeLabel));
        }

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
        _isAppSearchMode = false;
        OnPropertyChanged(nameof(IsAppSearchMode));
        OnPropertyChanged(nameof(SearchModeLabel));
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

    private IntPtr _clipboardHwnd;

    /// <summary>
    /// Starts clipboard monitoring. Must be called with the overlay HWND.
    /// </summary>
    public void StartClipboardService(IntPtr hwnd, DispatcherQueue? dispatcherQueue = null)
    {
        _clipboardHwnd = hwnd;
        _dispatcherQueue = dispatcherQueue;
        if (_settings.ClipboardHistoryEnabled)
        {
            _clipboardService.Start(hwnd, _settings.ClipboardHistoryMaxItems, dispatcherQueue);
        }
        LoadPinnedClipboardItems();
    }

    private void LoadPinnedClipboardItems()
    {
        PinnedClipboardItems.Clear();
        foreach (var text in _settings.PinnedClipboardItems)
        {
            PinnedClipboardItems.Add(new ClipboardItem { Text = text, IsPinned = true });
        }
    }

    [RelayCommand]
    public void TogglePinClipboardItem(ClipboardItem item)
    {
        if (item == null) return;

        if (item.IsPinned)
        {
            item.IsPinned = false;
            PinnedClipboardItems.Remove(item);
            _settings.PinnedClipboardItems.Remove(item.Text ?? "");
        }
        else
        {
            item.IsPinned = true;
            var pinned = new ClipboardItem { Text = item.Text, IsImage = item.IsImage, IsPinned = true };
            PinnedClipboardItems.Add(pinned);
            if (!string.IsNullOrEmpty(item.Text))
                _settings.PinnedClipboardItems.Add(item.Text);
        }
        _settingsService.Save(_settings);
    }

    private void OnClipboardEnabledChanged()
    {
        if (_settings.ClipboardHistoryEnabled && _clipboardHwnd != IntPtr.Zero)
        {
            _clipboardService.Start(_clipboardHwnd, _settings.ClipboardHistoryMaxItems, _dispatcherQueue);
        }
        else
        {
            _clipboardService.Stop();
            _clipboardService.ClearHistory();
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

    private static string ResolveHoverFromAccent(string hoverHex, string accentHex, byte alpha)
        => ThemeApplier.ResolveHoverFromAccent(hoverHex, accentHex, alpha);





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

    public AppearanceSettings GetAppearanceSettings() => _settings.Appearance;
}

