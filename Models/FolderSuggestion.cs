namespace BetterWinTab.Models;

/// <summary>A smart-folder candidate inferred from currently open windows.</summary>
public sealed record FolderSuggestion(string ProcessName, int WindowCount, string SampleTitle)
{
    public string Summary => WindowCount == 1
        ? $"{ProcessName} · 1 window"
        : $"{ProcessName} · {WindowCount} windows";
}
