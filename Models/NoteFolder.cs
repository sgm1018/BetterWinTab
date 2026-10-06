namespace BetterWinTab.Models;

public class NoteFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
