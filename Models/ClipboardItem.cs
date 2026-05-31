using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BetterWinTab.Models;

/// <summary>
/// Represents a single clipboard history entry.
/// </summary>
public class ClipboardItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Text content (for text clipboard entries).</summary>
    public string? Text { get; set; }

    /// <summary>True if the entry contains an image.</summary>
    public bool IsImage { get; set; }

    private BitmapImage? _imageSource;
    /// <summary>Decoded bitmap for image clipboard entries. Loaded asynchronously after capture.</summary>
    public BitmapImage? ImageSource
    {
        get => _imageSource;
        set
        {
            _imageSource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Preview text: truncated text or "[Image]" for image entries.</summary>
    public string Preview => IsImage
        ? "[Image]"
        : (Text?.Length > 120 ? Text[..117] + "..." : Text ?? "(empty)");

    /// <summary>Single-line preview for compact display.</summary>
    public string SingleLinePreview
    {
        get
        {
            if (IsImage) return "[Image]";
            var line = Text?.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ") ?? "(empty)";
            return line.Length > 80 ? line[..77] + "..." : line;
        }
    }

    /// <summary>When this item was captured.</summary>
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    /// <summary>Human-readable time ago string.</summary>
    public string TimeAgo
    {
        get
        {
            var diff = DateTime.Now - CapturedAt;
            if (diff.TotalSeconds < 60) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            return CapturedAt.ToString("MMM d, HH:mm");
        }
    }

    /// <summary>Character count for text items.</summary>
    public string SizeInfo => IsImage ? "Image" : $"{Text?.Length ?? 0} chars";

    /// <summary>Icon glyph for the clipboard entry type.</summary>
    public string TypeIcon => IsImage ? "\uE8B9" : "\uE8C8";

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            _isPinned = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
        }
    }

    public string PinnedPreview
    {
        get
        {
            if (IsImage) return "[Image]";
            var text = Text ?? "(empty)";
            return text.Length > 40 ? text[..37] + "..." : text;
        }
    }
}
