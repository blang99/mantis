using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mantis.Plugin.AI;

public class ClaudeClient : ILlmClient
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-sonnet-4-7";
    private const string AdvancedModel = "claude-opus-4-7";
    private const int MaxTokens = 8192;
    private const int MaxRetries = 3;

    private readonly HttpClient _http;
    private string _apiKey = "";
    private string _model = DefaultModel;

    public ClaudeClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public string ProviderName => "Claude";

    // All Claude 3+ models expose a 200K-token context window — full catalog.
    public int ContextWindowTokens => 200_000;

    public string SelectedModel
    {
        get => _model;
        set => _model = value;
    }

    public List<ModelOption> AvailableModels => new()
    {
        new ModelOption
        {
            Id = "claude-sonnet-4-7",
            DisplayName = "Claude Sonnet 4.7",
            Description = "Best balance (recommended)",
            IsFree = false,
            SupportsVision = true
        },
        new ModelOption
        {
            Id = "claude-opus-4-7",
            DisplayName = "Claude Opus 4.7",
            Description = "Most capable, slower",
            IsFree = false,
            SupportsVision = true
        },
        new ModelOption
        {
            Id = "claude-haiku-4-7",
            DisplayName = "Claude Haiku 4.7",
            Description = "Fastest, cheap",
            IsFree = false,
            SupportsVision = true
        },
        new ModelOption
        {
            Id = "claude-sonnet-4-6-20250514",
            DisplayName = "Claude Sonnet 4.6 (legacy)",
            Description = "Previous generation",
            IsFree = false,
            SupportsVision = true
        },
        new ModelOption
        {
            Id = "claude-opus-4-6-20250514",
            DisplayName = "Claude Opus 4.6 (legacy)",
            Description = "Previous capable model",
            IsFree = false,
            SupportsVision = true
        }
    };

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey?.Trim() ?? "";
        if (_http.DefaultRequestHeaders.Contains("x-api-key"))
            _http.DefaultRequestHeaders.Remove("x-api-key");
        if (!string.IsNullOrEmpty(_apiKey))
            _http.DefaultRequestHeaders.Add("x-api-key", _apiKey);
    }

    public void UseAdvancedModel(bool advanced) =>
        _model = advanced ? AdvancedModel : DefaultModel;

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!HasApiKey)
            throw new InvalidOperationException(
                "No Claude API key configured.\n" +
                "Get one at https://console.anthropic.com/settings/keys\n" +
                "Then click Settings → API Keys to enter it.");

        // Use non-streaming + retry for reliability. The full response comes
        // back as one chunk — fine since we batch-place components anyway.
        var result = await SendWithRetryAsync(systemPrompt, messages, cancellationToken);
        if (!string.IsNullOrEmpty(result))
            yield return result;
    }

    public async Task<string> SendAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        return await SendWithRetryAsync(systemPrompt, messages, cancellationToken);
    }

    private async Task<string> SendWithRetryAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                return await SendOnceAsync(systemPrompt, messages, cancellationToken);
            }
            catch (ClaudeRateLimitException ex)
            {
                lastException = ex;
                if (attempt < MaxRetries - 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cancellationToken);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Claude rate-limited after {MaxRetries} retries.\n\n" +
            $"Last error: {lastException?.Message}\n\n" +
            "Try again in a moment, or switch to a different provider.");
    }

    private async Task<string> SendOnceAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _model,
            max_tokens = MaxTokens,
            system = systemPrompt,
            messages = messages.Select(m => new { role = m.Role, content = VisionPayload.ClaudeContent(m) }).ToArray()
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(ApiUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = ExtractClaudeError(body);
            var statusCode = (int)response.StatusCode;

            if (statusCode == 429)
                throw new ClaudeRateLimitException(errorMsg);
            if (statusCode == 401)
                throw new InvalidOperationException(
                    $"Claude API key invalid (401).\n\n{errorMsg}\n\n" +
                    "Get a fresh key at https://console.anthropic.com/settings/keys");
            if (statusCode == 404)
                throw new InvalidOperationException(
                    $"Claude model '{_model}' not found (404).\n\n{errorMsg}\n\n" +
                    "Switch to a different model in Settings.");
            throw new InvalidOperationException($"Claude error {statusCode}: {errorMsg}");
        }

        // Parse the response
        try
        {
            using var doc = JsonDocument.Parse(body);
            var contentArr = doc.RootElement.GetProperty("content");
            var sb = new StringBuilder();
            foreach (var block in contentArr.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                    block.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString());
                }
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not parse Claude response: {ex.Message}\n\nRaw: {body[..Math.Min(500, body.Length)]}");
        }
    }

    private static string ExtractClaudeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "empty response body";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var type = err.TryGetProperty("type", out var t) ? t.GetString() : null;
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                return $"{type ?? "error"}: {message ?? "no message"}";
            }
        }
        catch { }
        return body.Length > 500 ? body[..500] + "..." : body;
    }

    public void Dispose() => _http.Dispose();

    private class ClaudeRateLimitException : Exception
    {
        public ClaudeRateLimitException(string message) : base(message) { }
    }
}
