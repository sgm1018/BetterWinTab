using System.Collections.ObjectModel;
using BetterWinTab.Models;
using BetterWinTab.Services;

namespace BetterWinTab.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;

    public SettingsViewModel(AppSettings settings, SettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;

        foreach (var p in _settings.CustomPresets)
            CustomPresets.Add(p);
    }

    [ObservableProperty]
    private bool _isSettingsPanelVisible;

    [ObservableProperty]
    private bool _settingsRunAtStartup;

    [ObservableProperty]
    private bool _settingsClipboardEnabled;

    public event Action? ClipboardEnabledChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyDisplayText))]
    private bool _isRecordingHotkey;

    public string HotkeyDisplayText =>
        IsRecordingHotkey
            ? "\u25B6  Press your key combo now..."
            : KeyboardHelper.FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVKey);

    [RelayCommand]
    public void StartRecordHotkey() => IsRecordingHotkey = true;

    [RelayCommand]
    public void CancelRecordHotkey() => IsRecordingHotkey = false;

    public void ApplyNewHotkey(uint modifiers, uint vKey)
    {
        _settings.HotkeyModifiers = modifiers;
        _settings.HotkeyVKey = vKey;
        IsRecordingHotkey = false;
        OnPropertyChanged(nameof(HotkeyDisplayText));
        App.Current.Hotkey?.Configure(modifiers, vKey);
        SaveSettings();
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

    public Visibility TabGeneralVisible => ActiveSettingsTab == "General" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TabThemesVisible => ActiveSettingsTab == "Themes" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TabAccentVisible => ActiveSettingsTab == "Accent" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TabBgVisible => ActiveSettingsTab == "Bg" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TabTextVisible => ActiveSettingsTab == "Text" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TabStatusVisible => ActiveSettingsTab == "Status" ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    public ObservableCollection<ThemePreset> CustomPresets { get; } = new();

    public List<ThemePreset> AvailablePresets => Presets;

    public event Action? AppearanceChanged;
    public event Action? AppearancePreviewChanged;

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
        SettingsFolderHoverColor = ThemeApplier.ResolveHoverFromAccent(a.FolderHoverColor, a.AccentColor, 0x1A);
        SettingsFolderSelectedColor = ThemeApplier.ResolveHoverFromAccent(a.FolderSelectedColor, a.AccentColor, 0x29);
        SettingsWindowHoverBorderColor = ThemeApplier.ResolveHoverFromAccent(a.WindowHoverBorderColor, a.AccentColor, 0x66);
        SettingsWindowHoverBackgroundColor = ThemeApplier.ResolveHoverFromAccent(a.WindowHoverBackgroundColor, a.AccentColor, 0x0C);
        SettingsRunAtStartup = _settingsService.GetRunAtStartup();
        SettingsClipboardEnabled = _settings.ClipboardHistoryEnabled;
        ActiveSettingsTab = "General";
        IsSettingsPanelVisible = true;
    }

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
        var clipboardChanged = _settings.ClipboardHistoryEnabled != SettingsClipboardEnabled;
        _settings.ClipboardHistoryEnabled = SettingsClipboardEnabled;
        SaveSettings();
        IsSettingsPanelVisible = false;
        AppearanceChanged?.Invoke();
        if (clipboardChanged) ClipboardEnabledChanged?.Invoke();
    }

    [RelayCommand]
    public void CancelSettings()
    {
        IsSettingsPanelVisible = false;
        AppearanceChanged?.Invoke();
    }

    [RelayCommand]
    public void ExitApplication()
    {
        IsSettingsPanelVisible = false;
        App.Current.ExitApplication();
    }

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
        SettingsAccentColor = d.AccentColor;
        SettingsAccentDimColor = d.AccentDimColor;
        SettingsAccentSubtleColor = d.AccentSubtleColor;
    }

    [RelayCommand]
    public void ResetBgSection()
    {
        var d = AppearanceSettings.Default();
        SettingsBackgroundColor = d.BackgroundColor;
        SettingsSurfaceColor = d.SurfaceColor;
        SettingsCardColor = d.CardColor;
        SettingsBorderColor = d.BorderColor;
        SettingsFolderHoverColor = d.FolderHoverColor;
        SettingsFolderSelectedColor = d.FolderSelectedColor;
        SettingsWindowHoverBorderColor = d.WindowHoverBorderColor;
        SettingsWindowHoverBackgroundColor = d.WindowHoverBackgroundColor;
    }

    [RelayCommand]
    public void ResetTextSection()
    {
        var d = AppearanceSettings.Default();
        SettingsTextPrimaryColor = d.TextPrimaryColor;
        SettingsTextSecondaryColor = d.TextSecondaryColor;
        SettingsTextMutedColor = d.TextMutedColor;
    }

    [RelayCommand]
    public void ResetStatusSection()
    {
        var d = AppearanceSettings.Default();
        SettingsDangerColor = d.DangerColor;
    }

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
                AccentColor = SettingsAccentColor,
                AccentDimColor = SettingsAccentDimColor,
                AccentSubtleColor = SettingsAccentSubtleColor,
                BackgroundColor = SettingsBackgroundColor,
                SurfaceColor = SettingsSurfaceColor,
                CardColor = SettingsCardColor,
                BorderColor = SettingsBorderColor,
                TextPrimaryColor = SettingsTextPrimaryColor,
                TextSecondaryColor = SettingsTextSecondaryColor,
                TextMutedColor = SettingsTextMutedColor,
                DangerColor = SettingsDangerColor,
                FolderHoverColor = SettingsFolderHoverColor,
                FolderSelectedColor = SettingsFolderSelectedColor,
                WindowHoverBorderColor = SettingsWindowHoverBorderColor,
                WindowHoverBackgroundColor = SettingsWindowHoverBackgroundColor,
            }
        };

        CustomPresets.Add(preset);
        _settings.CustomPresets.Add(preset);
        _settingsService.Save(_settings);
        NewPresetName = string.Empty;
    }

    [RelayCommand]
    public void DeleteCustomPreset(ThemePreset preset)
    {
        CustomPresets.Remove(preset);
        _settings.CustomPresets.Remove(preset);
        _settingsService.Save(_settings);
    }

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
        SettingsFolderHoverColor = ThemeApplier.ResolveHoverFromAccent(preset.Appearance.FolderHoverColor, preset.Appearance.AccentColor, 0x1A);
        SettingsFolderSelectedColor = ThemeApplier.ResolveHoverFromAccent(preset.Appearance.FolderSelectedColor, preset.Appearance.AccentColor, 0x29);
        SettingsWindowHoverBorderColor = ThemeApplier.ResolveHoverFromAccent(preset.Appearance.WindowHoverBorderColor, preset.Appearance.AccentColor, 0x66);
        SettingsWindowHoverBackgroundColor = ThemeApplier.ResolveHoverFromAccent(preset.Appearance.WindowHoverBackgroundColor, preset.Appearance.AccentColor, 0x0C);
    }

    public AppearanceSettings GetAppearanceSettings() => _settings.Appearance;

    partial void OnSettingsAccentColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsAccentDimColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsAccentSubtleColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsBackgroundColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsSurfaceColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsCardColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsBorderColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsTextPrimaryColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsTextSecondaryColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsTextMutedColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsDangerColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsFolderHoverColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsFolderSelectedColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsWindowHoverBorderColorChanged(string value) => AppearancePreviewChanged?.Invoke();
    partial void OnSettingsWindowHoverBackgroundColorChanged(string value) => AppearancePreviewChanged?.Invoke();

    partial void OnSettingsClipboardEnabledChanged(bool value)
    {
        _settings.ClipboardHistoryEnabled = value;
        _settingsService.Save(_settings);
        ClipboardEnabledChanged?.Invoke();
    }

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
    }

    public static readonly List<ThemePreset> Presets = new()
    {
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

        new ThemePreset { Name = "Dark Cian", Appearance = new() {
            AccentColor="#00ADB5", AccentDimColor="#007A80", AccentSubtleColor="#003840",
            BackgroundColor="#222831", SurfaceColor="#1A1E26", CardColor="#2C3140",
            BorderColor="#454D5C", TextPrimaryColor="#EEEEEE", TextSecondaryColor="#AAAAAA",
            TextMutedColor="#666D7A", DangerColor="#FF5252",
            FolderHoverColor="#1A00ADB5", FolderSelectedColor="#2900ADB5",
            WindowHoverBorderColor="#6600ADB5", WindowHoverBackgroundColor="#0C00ADB5" } },

        new ThemePreset { Name = "Ice Blue", Appearance = new() {
            AccentColor="#71C9CE", AccentDimColor="#4AA5AA", AccentSubtleColor="#C0ECEF",
            BackgroundColor="#E3FDFD", SurfaceColor="#CBF1F5", CardColor="#D8F7F8",
            BorderColor="#A6E3E9", TextPrimaryColor="#1A3A40", TextSecondaryColor="#2E5E65",
            TextMutedColor="#4A8A92", DangerColor="#E05050",
            FolderHoverColor="#1A71C9CE", FolderSelectedColor="#2971C9CE",
            WindowHoverBorderColor="#6671C9CE", WindowHoverBackgroundColor="#0C71C9CE" } },

        new ThemePreset { Name = "Corporate", Appearance = new() {
            AccentColor="#3F72AF", AccentDimColor="#2A5A9A", AccentSubtleColor="#C5D5E8",
            BackgroundColor="#F9F7F7", SurfaceColor="#EEF1F8", CardColor="#DBE2EF",
            BorderColor="#B8C5DA", TextPrimaryColor="#112D4E", TextSecondaryColor="#2A4D6E",
            TextMutedColor="#6A849A", DangerColor="#CC3333",
            FolderHoverColor="#1A3F72AF", FolderSelectedColor="#293F72AF",
            WindowHoverBorderColor="#663F72AF", WindowHoverBackgroundColor="#0C3F72AF" } },

        new ThemePreset { Name = "Earthy", Appearance = new() {
            AccentColor="#E3CAA5", AccentDimColor="#CEAB93", AccentSubtleColor="#6A5040",
            BackgroundColor="#1E1510", SurfaceColor="#2A1F18", CardColor="#352820",
            BorderColor="#4A3828", TextPrimaryColor="#FFFBE9", TextSecondaryColor="#E8D5B8",
            TextMutedColor="#AD8B73", DangerColor="#C04040",
            FolderHoverColor="#1AE3CAA5", FolderSelectedColor="#29E3CAA5",
            WindowHoverBorderColor="#66E3CAA5", WindowHoverBackgroundColor="#0CE3CAA5" } },

        new ThemePreset { Name = "Lavender", Appearance = new() {
            AccentColor="#424874", AccentDimColor="#5A5E9A", AccentSubtleColor="#C0C4E2",
            BackgroundColor="#F4EEFF", SurfaceColor="#EBE5FF", CardColor="#DCD6F7",
            BorderColor="#C5BCE8", TextPrimaryColor="#2A2C50", TextSecondaryColor="#424874",
            TextMutedColor="#A6B1E1", DangerColor="#CC3355",
            FolderHoverColor="#1A424874", FolderSelectedColor="#29424874",
            WindowHoverBorderColor="#66424874", WindowHoverBackgroundColor="#0C424874" } },

        new ThemePreset { Name = "Sunset", Appearance = new() {
            AccentColor="#F9ED69", AccentDimColor="#F08A5D", AccentSubtleColor="#5A2040",
            BackgroundColor="#1A0A14", SurfaceColor="#261420", CardColor="#30182A",
            BorderColor="#441830", TextPrimaryColor="#FFF5E0", TextSecondaryColor="#F4C880",
            TextMutedColor="#B83B5E", DangerColor="#FF4455",
            FolderHoverColor="#1AF9ED69", FolderSelectedColor="#29F9ED69",
            WindowHoverBorderColor="#66F9ED69", WindowHoverBackgroundColor="#0CF9ED69" } },

        new ThemePreset { Name = "Cyberpunk", Appearance = new() {
            AccentColor="#08D9D6", AccentDimColor="#0599A0", AccentSubtleColor="#0A3040",
            BackgroundColor="#252A34", SurfaceColor="#1A1E27", CardColor="#1E2332",
            BorderColor="#2E3545", TextPrimaryColor="#EAEAEA", TextSecondaryColor="#B0B8CC",
            TextMutedColor="#6A7588", DangerColor="#FF2E63",
            FolderHoverColor="#1A08D9D6", FolderSelectedColor="#2908D9D6",
            WindowHoverBorderColor="#6608D9D6", WindowHoverBackgroundColor="#0C08D9D6" } },

        new ThemePreset { Name = "Deep Ocean", Appearance = new() {
            AccentColor="#3282B8", AccentDimColor="#1F5A8A", AccentSubtleColor="#0A2840",
            BackgroundColor="#1B262C", SurfaceColor="#0F2030", CardColor="#142840",
            BorderColor="#0F4C75", TextPrimaryColor="#BBE1FA", TextSecondaryColor="#7AB8E0",
            TextMutedColor="#3A7AA8", DangerColor="#FF5050",
            FolderHoverColor="#1A3282B8", FolderSelectedColor="#293282B8",
            WindowHoverBorderColor="#663282B8", WindowHoverBackgroundColor="#0C3282B8" } },

        new ThemePreset { Name = "Summer Pop", Appearance = new() {
            AccentColor="#95E1D3", AccentDimColor="#5CBFB0", AccentSubtleColor="#C0F0EC",
            BackgroundColor="#FFF8F5", SurfaceColor="#FFF0EE", CardColor="#FFE5E5",
            BorderColor="#F4C5C5", TextPrimaryColor="#3A2A2A", TextSecondaryColor="#C05050",
            TextMutedColor="#F38181", DangerColor="#E84040",
            FolderHoverColor="#1A95E1D3", FolderSelectedColor="#2995E1D3",
            WindowHoverBorderColor="#6695E1D3", WindowHoverBackgroundColor="#0C95E1D3" } },

        new ThemePreset { Name = "Dark Wine", Appearance = new() {
            AccentColor="#E84545", AccentDimColor="#A02A2A", AccentSubtleColor="#50182A",
            BackgroundColor="#2B2E4A", SurfaceColor="#232640", CardColor="#2E3155",
            BorderColor="#3A3D60", TextPrimaryColor="#F0E8EC", TextSecondaryColor="#C8A0AA",
            TextMutedColor="#806070", DangerColor="#FF2244",
            FolderHoverColor="#1AE84545", FolderSelectedColor="#29E84545",
            WindowHoverBorderColor="#66E84545", WindowHoverBackgroundColor="#0CE84545" } },

        new ThemePreset { Name = "Industrial", Appearance = new() {
            AccentColor="#F4CE14", AccentDimColor="#C8A800", AccentSubtleColor="#F0E090",
            BackgroundColor="#F5F7F8", SurfaceColor="#EAEEF0", CardColor="#DEE3E5",
            BorderColor="#C8CDD0", TextPrimaryColor="#45474B", TextSecondaryColor="#697070",
            TextMutedColor="#8A9090", DangerColor="#CC3333",
            FolderHoverColor="#1AF4CE14", FolderSelectedColor="#29F4CE14",
            WindowHoverBorderColor="#66F4CE14", WindowHoverBackgroundColor="#0CF4CE14" } },

        new ThemePreset { Name = "Blood Red", Appearance = new() {
            AccentColor="#FF0000", AccentDimColor="#950101", AccentSubtleColor="#3D0000",
            BackgroundColor="#000000", SurfaceColor="#100000", CardColor="#1A0000",
            BorderColor="#3D0000", TextPrimaryColor="#FF4444", TextSecondaryColor="#CC2222",
            TextMutedColor="#881111", DangerColor="#FF6600",
            FolderHoverColor="#1AFF0000", FolderSelectedColor="#29FF0000",
            WindowHoverBorderColor="#66FF0000", WindowHoverBackgroundColor="#0CFF0000" } },

        new ThemePreset { Name = "Fire & Steel", Appearance = new() {
            AccentColor="#FFD460", AccentDimColor="#F07B3F", AccentSubtleColor="#4A3010",
            BackgroundColor="#2D4059", SurfaceColor="#22334A", CardColor="#1A2838",
            BorderColor="#3A5070", TextPrimaryColor="#F0EDE8", TextSecondaryColor="#C8B890",
            TextMutedColor="#8A7A60", DangerColor="#EA5455",
            FolderHoverColor="#1AFFD460", FolderSelectedColor="#29FFD460",
            WindowHoverBorderColor="#66FFD460", WindowHoverBackgroundColor="#0CFFD460" } },

        new ThemePreset { Name = "Forest", Appearance = new() {
            AccentColor="#7FBF00", AccentDimColor="#427A43", AccentSubtleColor="#1A3A18",
            BackgroundColor="#010F02", SurfaceColor="#0A180A", CardColor="#122014",
            BorderColor="#1A3020", TextPrimaryColor="#F2E3BB", TextSecondaryColor="#C0B87A",
            TextMutedColor="#808060", DangerColor="#CC4422",
            FolderHoverColor="#1A7FBF00", FolderSelectedColor="#297FBF00",
            WindowHoverBorderColor="#667FBF00", WindowHoverBackgroundColor="#0C7FBF00" } },

        new ThemePreset { Name = "Midnight Rose", Appearance = new() {
            AccentColor="#FF6FA6", AccentDimColor="#CC3A70", AccentSubtleColor="#60112E",
            BackgroundColor="#0A0A12", SurfaceColor="#12121E", CardColor="#1A1A2A",
            BorderColor="#282838", TextPrimaryColor="#FFE8F0", TextSecondaryColor="#CCA0B4",
            TextMutedColor="#806070", DangerColor="#FF4400",
            FolderHoverColor="#1AFF6FA6", FolderSelectedColor="#29FF6FA6",
            WindowHoverBorderColor="#66FF6FA6", WindowHoverBackgroundColor="#0CFF6FA6" } },
    };
}
