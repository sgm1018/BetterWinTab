namespace BetterWinTab.Models.Calls;

/// <summary>A report generated from a saved template and its prompt.</summary>
public sealed record GeneratedDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TemplateId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string RecordingId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Prompt template used to turn a call transcript into a report.</summary>
public sealed record ReportTemplate
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    /// <summary>Supports {title} and {transcript} placeholders.</summary>
    public string UserPromptTemplate { get; init; } = string.Empty;

    public static ReportTemplate MeetingNotes { get; } = new()
    {
        Id = "meeting-notes",
        Name = "Meeting notes",
        SystemPrompt = "Write clear, factual meeting notes. Do not invent details. Mark missing information as unspecified.",
        UserPromptTemplate = "Create a structured report for the call titled \"{title}\". Include a concise summary, key discussion points, decisions, open questions, and action items with owners and dates when stated. Preserve names and facts as given.\n\nTranscript:\n{transcript}"
    };

    public string BuildPrompt(string title, string transcript)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(UserPromptTemplate);
        return UserPromptTemplate
            .Replace("{title}", title ?? string.Empty, StringComparison.Ordinal)
            .Replace("{transcript}", transcript ?? string.Empty, StringComparison.Ordinal);
    }
}
