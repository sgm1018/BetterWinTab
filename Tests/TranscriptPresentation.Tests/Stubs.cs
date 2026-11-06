using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Windows.UI
{
    public record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    }
}
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
}
namespace Microsoft.UI.Xaml.Media
{
    public class SolidColorBrush(Windows.UI.Color color)
    {
        public Windows.UI.Color Color { get; } = color;
    }
}
namespace BetterWinTab.ViewModels
{
    public partial class CallsViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isRecording;
        [ObservableProperty] private bool _isTranscribing;
        [ObservableProperty] private int _countdownSeconds;
        [ObservableProperty] private BetterWinTab.Models.AudioRecordingMetadata? _selectedRecording;
        public IAsyncRelayCommand SaveTranscriptCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
        public CallsViewModel() => InitializeTranscriptPresentation();
    }
}
