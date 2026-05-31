using BetterWinTab.Models;
using BetterWinTab.Services;

namespace BetterWinTab.ViewModels;

public partial class OnboardingViewModel : BaseViewModel
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;

    public OnboardingViewModel(AppSettings settings, SettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;
        IsOnboardingVisible = !_settings.HasCompletedOnboarding;
    }

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
    public string OnboardingNextButtonText => CurrentOnboardingStep == 5 ? "Get Started  \U0001F389" : "Next  \u2192";

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
        0 => $"Your smart window switcher — manage every open window from one keyboard-driven overlay.\n\nPress {KeyboardHelper.FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVKey)} at any time, from any app, to open or close it.",
        1 => "Type anything to filter open windows in real time — no mouse needed.\n\nNo match? BetterWinTab also searches your installed apps. Press Enter to launch one directly from the overlay.",
        2 => "Group windows into custom folders for any project or context.\n\nSmart folders auto-populate by process name or title rule. Drag a window card onto a sidebar folder to assign it manually.",
        3 => "Click a card to switch to that window instantly.\n\nRight-click a card for more options: pin it so it always appears first, minimize it, or close it — all without leaving the overlay.",
        4 => "\u2190 \u2192 \u2191 \u2193  navigate cards   \u00B7   Enter  switch to selected   \u00B7   Esc  close overlay\n\nIn the Clipboard panel \u2191\u2193 moves between entries and Enter copies the selected one.\n\nDrag any window card onto a sidebar folder to move it in there.",
        _ => "Customize everything in \u2699 Settings: change the global hotkey, pick an accent color, and enable auto-start on login.\n\nAll settings save automatically. Enjoy BetterWinTab!",
    };

    [RelayCommand]
    private void NextOnboardingStep()
    {
        if (CurrentOnboardingStep < 5) { CurrentOnboardingStep++; return; }
        IsOnboardingVisible = false;
        _settings.HasCompletedOnboarding = true;
        _settingsService.Save(_settings);
    }

    [RelayCommand]
    private void SkipOnboarding()
    {
        IsOnboardingVisible = false;
        _settings.HasCompletedOnboarding = true;
        _settingsService.Save(_settings);
    }
}
