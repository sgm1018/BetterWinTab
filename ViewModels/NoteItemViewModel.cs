using System.Net;
using System.Text.RegularExpressions;

namespace BetterWinTab.ViewModels;

/// <summary>
/// Sidebar entry for a single note.
/// </summary>
public partial class NoteItemViewModel : BaseViewModel
{
    public Note Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _noteTitle;

    [ObservableProperty]
    private string _preview;

    [ObservableProperty]
    private string _updatedLabel = string.Empty;

    public string DisplayTitle => string.IsNullOrWhiteSpace(NoteTitle) ? "Untitled" : NoteTitle;

    public NoteItemViewModel(Note model)
    {
        Model = model;
        _noteTitle = model.Title;
        _preview = BuildPreview(model.Html);
        RefreshUpdatedLabel();
    }

    public void ApplyModelChanges(string? previewText = null)
    {
        NoteTitle = Model.Title;
        Preview = previewText != null ? Truncate(Collapse(previewText)) : BuildPreview(Model.Html);
        RefreshUpdatedLabel();
    }

    public void RefreshUpdatedLabel()
    {
        var span = DateTime.Now - Model.UpdatedAt;
        UpdatedLabel = span.TotalMinutes < 1 ? "Just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 && Model.UpdatedAt.Date == DateTime.Today ? Model.UpdatedAt.ToString("HH:mm")
            : Model.UpdatedAt.Year == DateTime.Now.Year ? Model.UpdatedAt.ToString("d MMM")
            : Model.UpdatedAt.ToString("d MMM yyyy");
    }

    private static string BuildPreview(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = Regex.Replace(html, @"<(br|/p|/h\d|/li|/div|/blockquote|/pre)[^>]*>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        return Truncate(Collapse(WebUtility.HtmlDecode(text)));
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string Truncate(string text) => text.Length > 120 ? text[..120] : text;
}
