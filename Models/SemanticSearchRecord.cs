namespace BetterWinTab.Models;

public sealed record SemanticSearchRecord(
    string Id,
    string Text,
    string Type,
    object Value);