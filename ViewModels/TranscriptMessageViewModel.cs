using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace BetterWinTab.ViewModels;

/// <summary>A stable conversation row; streaming updates replace its text in place.</summary>
public sealed class TranscriptMessageViewModel : ObservableObject
{
    public string Id { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string Speaker { get; private set; } = string.Empty;
    public double StartSeconds { get; private set; }
    public bool IsProvisional { get; private set; }
    public string Timestamp => $"{(int)(Math.Max(0, StartSeconds) / 60):00}:{(int)Math.Max(0, StartSeconds) % 60:00}";
    public string Avatar { get; private set; } = "?";
    public SolidColorBrush SpeakerBrush { get; private set; } = MakeBrush(0);
    public string Status { get; private set; } = string.Empty;
    public Visibility StatusVisibility => Status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public double TextOpacity => IsProvisional ? 0.78 : 1;

    internal void Update(string id, string source, double startSeconds, string text,
        string? speakerId, string? speaker, bool provisional, bool incomplete = false, bool uncertain = false)
    {
        Id = id;
        Source = source;
        StartSeconds = startSeconds;
        Text = text;
        IsProvisional = provisional;
        var microphone = source.Equals("microphone", StringComparison.OrdinalIgnoreCase);
        Speaker = microphone ? "You" : string.IsNullOrWhiteSpace(speaker) ? "Speaker" : speaker;
        Avatar = microphone ? "Y" : GetAvatar(Speaker);
        SpeakerBrush = MakeBrush(microphone ? 0 : GetSpeakerColor(speakerId ?? Speaker));
        var state = provisional ? "Recognizing…" : incomplete ? "Incomplete" : string.Empty;
        Status = uncertain ? (state.Length > 0 ? state + " · Voice uncertain" : "Voice uncertain") : state;
        OnPropertyChanged(string.Empty);
    }

    private static string GetAvatar(string speaker)
    {
        var suffix = speaker.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "?";
        return int.TryParse(suffix, out _) ? suffix : speaker[..1].ToUpperInvariant();
    }

    private static int GetSpeakerColor(string identity)
    {
        // A deterministic identity hash keeps colours stable when reopening a recording.
        uint hash = 2166136261;
        foreach (var character in identity) hash = (hash ^ character) * 16777619;
        return 1 + (int)(hash % 5);
    }

    private static SolidColorBrush MakeBrush(int index)
    {
        var rgb = index switch
        {
            0 => (94, 223, 222),
            1 => (153, 195, 249),
            2 => (239, 187, 112),
            3 => (149, 213, 168),
            4 => (237, 165, 183),
            _ => (203, 193, 151)
        };
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)rgb.Item1, (byte)rgb.Item2, (byte)rgb.Item3));
    }
}
