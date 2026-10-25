namespace BetterWinTab.Services.Calls.Ai;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatModel(string Id, string Name, string? ContextLength = null);

/// <summary>Result of model discovery; an empty list means the user must enter a model id manually.</summary>
public sealed record ChatModelCatalog(IReadOnlyList<ChatModel> Models, bool RequiresManualModelId)
{
    public static ChatModelCatalog Manual { get; } = new(Array.Empty<ChatModel>(), true);
}

public sealed record ChatRequest(
    string ModelId,
    IReadOnlyList<ChatMessage> Messages,
    int MaxTokens = 2048,
    double? Temperature = null);

/// <summary>Abstraction over secure, user-approved provider credential storage.</summary>
public interface IApiKeyProvider
{
    string? GetApiKey(string providerId);
}

/// <summary>Static provider and model metadata used by the Calls model picker.</summary>
public sealed record ChatProviderDescriptor(string Id, string DisplayName, bool SupportsModelDiscovery);

public static class ChatProviderCatalog
{
    // Provider model IDs are deliberately local so the picker works without an API-key-gated
    // model-list request. Recheck provider documentation before updating this curated catalog.
    public static IReadOnlyList<ChatProviderDescriptor> Providers { get; } =
    [
        new("deepseek", "DeepSeek", false),
        new("openai", "OpenAI", false),
        new("anthropic", "Anthropic", false),
        new("openrouter", "OpenRouter", false)
    ];

    public static IReadOnlyList<ChatModel> GetModels(string providerId) => providerId.ToLowerInvariant() switch
    {
        "deepseek" =>
        [
            new("deepseek-flash", "DeepSeek V4.1 Flash"),
            new("deepseek-v4-pro", "DeepSeek V4 Pro")
        ],
        "openai" =>
        [
            new("gpt-6-astra", "GPT-6 Astra"),
            new("gpt-6.1-sol", "GPT-6.1 Sol"),
            new("gpt-6-luna", "GPT-6 Luna"),
            new("gpt-6-sol", "GPT-6 Sol")
        ],
        "anthropic" =>
        [
            new("claude-fable-5-1", "Claude Fable 5.1"),
            new("claude-mythos-5-1", "Claude Mythos 5.1"),
            new("claude-fable-5", "Claude Fable 5"),
            new("claude-mythos-5", "Claude Mythos 5"),
            new("claude-opus-5-5", "Claude Opus 5.5"),
            new("claude-opus-5", "Claude Opus 5"),
            new("claude-opus-4-8", "Claude Opus 4.8"),
            new("claude-opus-4-7", "Claude Opus 4.7"),
            new("claude-opus-4-6", "Claude Opus 4.6"),
            new("claude-sonnet-5-5", "Claude Sonnet 5.5"),
            new("claude-sonnet-5", "Claude Sonnet 5"),
            new("claude-sonnet-4-6", "Claude Sonnet 4.6"),
            new("claude-haiku-5-5", "Claude Haiku 5.5"),
            new("claude-haiku-4-5-20251001", "Claude Haiku 4.5")
        ],
        _ => Array.Empty<ChatModel>()
    };
}

public interface IChatProvider
{
    string ProviderId { get; }

    Task<ChatModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
