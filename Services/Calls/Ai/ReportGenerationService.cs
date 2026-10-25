using System.Text;
using BetterWinTab.Models.Calls;

namespace BetterWinTab.Services.Calls.Ai;

/// <summary>Builds a report prompt from a call transcript, streams the response, then persists the report.</summary>
public sealed class ReportGenerationService
{
    private readonly ChatProviderFactory _providerFactory;
    private readonly GeneratedDocumentStore _documentStore;

    public ReportGenerationService(ChatProviderFactory providerFactory, GeneratedDocumentStore documentStore)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
    }

    public async Task<GeneratedDocument> GenerateAsync(
        string title,
        string transcript,
        ReportTemplate template,
        string providerId,
        string modelId,
        IProgress<string>? textProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var prompt = template.BuildPrompt(title, transcript);
        var provider = _providerFactory.Create(providerId);
        var request = new ChatRequest(modelId,
        [
            new ChatMessage("system", template.SystemPrompt),
            new ChatMessage("user", prompt)
        ]);
        var content = new StringBuilder();
        await foreach (var delta in provider.StreamAsync(request, cancellationToken).WithCancellation(cancellationToken))
        {
            content.Append(delta);
            textProgress?.Report(delta);
        }

        var document = new GeneratedDocument
        {
            TemplateId = template.Id,
            Title = string.IsNullOrWhiteSpace(title) ? template.Name : title.Trim(),
            Content = content.ToString(),
            Prompt = prompt,
            TemplateName = template.Name,
            ProviderId = provider.ProviderId,
            ModelId = modelId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await _documentStore.SaveAsync(document, cancellationToken);
        return document;
    }
}
