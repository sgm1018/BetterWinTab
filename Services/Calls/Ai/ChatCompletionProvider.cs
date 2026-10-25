using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BetterWinTab.Services.Calls.Ai;

/// <summary>OpenAI-compatible Chat Completions adapter for OpenAI, DeepSeek, and OpenRouter.</summary>
public sealed class ChatCompletionProvider : IChatProvider
{
    private readonly HttpClient _httpClient;
    private readonly IApiKeyProvider _credentialStore;
    private readonly Uri _baseUri;

    public ChatCompletionProvider(string providerId, Uri baseUri, IApiKeyProvider credentialStore, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(baseUri);
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        ProviderId = providerId.Trim().ToLowerInvariant();
        _baseUri = EnsureTrailingSlash(baseUri);
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public string ProviderId { get; }

    public static ChatCompletionProvider CreateOpenAI(IApiKeyProvider credentials, HttpClient? client = null)
        => new("openai", new Uri("https://api.openai.com/v1/"), credentials, client);

    public static ChatCompletionProvider CreateDeepSeek(IApiKeyProvider credentials, HttpClient? client = null)
        => new("deepseek", new Uri("https://api.deepseek.com/"), credentials, client);

    public static ChatCompletionProvider CreateOpenRouter(IApiKeyProvider credentials, HttpClient? client = null)
        => new("openrouter", new Uri("https://openrouter.ai/api/v1/"), credentials, client);

    public async Task<ChatModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _credentialStore.GetApiKey(ProviderId);
        if (string.IsNullOrWhiteSpace(apiKey))
            return ChatModelCatalog.Manual;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return ChatModelCatalog.Manual;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return ChatModelCatalog.Manual;

        var models = new List<ChatModel>();
        foreach (var item in data.EnumerateArray())
        {
            var id = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            models.Add(new ChatModel(id, GetString(item, "name") ?? id,
                GetString(item, "context_length") ?? GetString(item, "context_window")));
        }

        return models.Count == 0 ? ChatModelCatalog.Manual : new ChatModelCatalog(models, false);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ModelId))
            throw new ArgumentException("A model id is required.", nameof(request));
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var apiKey = _credentialStore.GetApiKey(ProviderId);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key is configured for {ProviderId}.");

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.ModelId,
            ["messages"] = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = true
        };
        if (request.Temperature.HasValue)
            body["temperature"] = request.Temperature.Value;

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line[5..].Trim();
            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                yield break;
            if (data.Length == 0)
                continue;

            using var chunk = JsonDocument.Parse(data);
            if (!chunk.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String
                && content.GetString() is { Length: > 0 } text)
                yield return text;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"AI provider returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }

    private static string? GetString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static Uri EnsureTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
