namespace BetterWinTab.Models;

/// <summary>
/// A rich-text note edited in the Notes panel. Content is stored as sanitized HTML
/// produced by the embedded editor (Assets/NotesEditor).
/// </summary>
public class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;

    /// <summary>Editor font family key: "handwritten" (default), "sans", "serif" or "mono".</summary>
    public string Font { get; set; } = "handwritten";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int SortOrder { get; set; }
    public string? FolderId { get; set; }
}
