namespace BetterWinTab.Models;

/// <summary>Local usage signals for an item in search results.</summary>
public sealed class ItemUsage
{
    public int UseCount { get; set; }
    public DateTime LastUsedUtc { get; set; }
}
