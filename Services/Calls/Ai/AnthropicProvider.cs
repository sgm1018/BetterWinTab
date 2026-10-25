using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BetterWinTab.Services.Calls.Ai;

/// <summary>Anthropic Messages API adapter with text-only SSE handling.</summary>
public sealed class AnthropicProvider : IChatProvider
{
    private const string ApiVersion = "2023-06-01";
    private readonly HttpClient _httpClient;
    private readonly IApiKeyProvider _credentialStore;
    private readonly Uri _baseUri;

    public AnthropicProvider(IApiKeyProvider credentialStore, HttpClient? httpClient = null, Uri? baseUri = null)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _baseUri = baseUri ?? new Uri("https://api.anthropic.com/");
        if (!_baseUri.AbsoluteUri.EndsWith('/'))
            _baseUri = new Uri(_baseUri.AbsoluteUri + "/");
    }

    public string ProviderId => "anthropic";

    public async Task<ChatModelCatalog> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _credentialStore.GetApiKey(ProviderId);
        if (string.IsNullOrWhiteSpace(apiKey))
            return ChatModelCatalog.Manual;

        var models = new List<ChatModel>();
        string? afterId = null;
        do
        {
            var url = new StringBuilder(new Uri(_baseUri, "v1/models").AbsoluteUri);
            if (!string.IsNullOrEmpty(afterId))
                url.Append("?after_id=").Append(Uri.EscapeDataString(afterId));
            using var request = CreateRequest(HttpMethod.Get, new Uri(url.ToString()), apiKey);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ChatModelCatalog.Manual;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return ChatModelCatalog.Manual;
            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 } id)
                    continue;
                var name = item.TryGetProperty("display_name", out var display) ? display.GetString() : null;
                models.Add(new ChatModel(id, string.IsNullOrWhiteSpace(name) ? id : name));
            }
            var hasMore = json.RootElement.TryGetProperty("has_more", out var hasMoreElement) && hasMoreElement.GetBoolean();
            afterId = hasMore && json.RootElement.TryGetProperty("last_id", out var lastId) ? lastId.GetString() : null;
        } while (afterId is not null);

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
            throw new InvalidOperationException("No API key is configured for Anthropic.");

        var system = string.Join("\n\n", request.Messages.Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).Select(m => m.Content));
        var messages = request.Messages
            .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { role = m.Role, content = m.Content });
        var body = new Dictionary<string, object?>
        {
            ["model"] = request.ModelId,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = true
        };
        if (!string.IsNullOrEmpty(system))
            body["system"] = system;
        if (request.Temperature.HasValue)
            body["temperature"] = request.Temperature.Value;

        using var message = CreateRequest(HttpMethod.Post, new Uri(_baseUri, "v1/messages"), apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream);
        var eventType = string.Empty;
        var dataLines = new List<string>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length == 0)
            {
                var text = ParseEvent(eventType, dataLines);
                if (text is not null)
                    yield return text;
                if (eventType == "message_stop")
                    yield break;
                eventType = string.Empty;
                dataLines.Clear();
                continue;
            }
            if (line.StartsWith("event:", StringComparison.Ordinal))
                eventType = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
                dataLines.Add(line[5..].TrimStart());
        }

        var remaining = ParseEvent(eventType, dataLines);
        if (remaining is not null)
            yield return remaining;
    }

    private static string? ParseEvent(string eventType, IReadOnlyCollection<string> dataLines)
    {
        if (!eventType.Equals("content_block_delta", StringComparison.Ordinal) || dataLines.Count == 0)
            return null;
        using var json = JsonDocument.Parse(string.Join("\n", dataLines));
        var root = json.RootElement;
        if (!root.TryGetProperty("delta", out var delta)
            || !delta.TryGetProperty("type", out var type)
            || type.GetString() != "text_delta"
            || !delta.TryGetProperty("text", out var text))
            return null;
        return text.GetString();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Anthropic returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }
}
