using System.Net.Http;

namespace BetterWinTab.Services.Calls.Ai;

/// <summary>Creates a configured provider without exposing provider-specific construction to callers.</summary>
public sealed class ChatProviderFactory
{
    private readonly IApiKeyProvider _apiKeys;
    private readonly HttpClient? _httpClient;

    public ChatProviderFactory(IApiKeyProvider apiKeys, HttpClient? httpClient = null)
    {
        _apiKeys = apiKeys ?? throw new ArgumentNullException(nameof(apiKeys));
        _httpClient = httpClient;
    }

    public IChatProvider Create(string providerId) => providerId.Trim().ToLowerInvariant() switch
    {
        "deepseek" => ChatCompletionProvider.CreateDeepSeek(_apiKeys, _httpClient),
        "openai" => ChatCompletionProvider.CreateOpenAI(_apiKeys, _httpClient),
        "anthropic" => new AnthropicProvider(_apiKeys, _httpClient),
        "openrouter" => ChatCompletionProvider.CreateOpenRouter(_apiKeys, _httpClient),
        _ => throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Unknown AI provider.")
    };
}
