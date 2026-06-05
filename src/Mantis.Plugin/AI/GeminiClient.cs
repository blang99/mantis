using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mantis.Plugin.AI;

public class GeminiClient : ILlmClient
{
    // The v1beta endpoint is what all current docs use. v1 is older.
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string DefaultModel = "gemini-2.0-flash";
    private const int MaxOutputTokens = 8192;
    private const int MaxRetries = 3;

    private readonly HttpClient _http;
    private string _apiKey = "";
    private string _model = DefaultModel;

    public GeminiClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public string ProviderName => "Gemini";

    // Gemini 1.5/2.x models expose 1M+ token context — full catalog.
    public int ContextWindowTokens => 1_000_000;

    public string SelectedModel
    {
        get => _model;
        set => _model = value;
    }

    public List<ModelOption> AvailableModels => new()
    {
        new ModelOption
        {
            Id = "gemini-2.0-flash",
            DisplayName = "Gemini 2.0 Flash",
            Description = "Fast & free (default)",
            IsFree = true,
            SupportsVision = true
        },
        new ModelOption
        {
            Id = "gemini-2.5-flash",
            DisplayName = "Gemini 2.5 Flash",
            Description = "Latest fast model",
            IsFree = true,
            SupportsVision = true
        },
        new ModelOption
        {
            Id = "gemini-2.5-pro",
            DisplayName = "Gemini 2.5 Pro",
            Description = "Most capable",
            IsFree = false,
            SupportsVision = true
        },
        new ModelOption
        {
            Id = "gemini-1.5-flash",
            DisplayName = "Gemini 1.5 Flash (legacy)",
            Description = "Stable fallback",
            IsFree = true,
            SupportsVision = true
        }
    };

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey?.Trim() ?? "";
    }

    /// <summary>
    /// Use the non-streaming endpoint (generateContent) and yield the whole
    /// result as one chunk. This is much more reliable than streamGenerateContent
    /// + SSE which has flaky model support, and the chat UI doesn't lose
    /// anything since we don't display partial generations to the user.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException(
                "No Gemini API key configured.\n" +
                "Get a free key at https://aistudio.google.com/apikey\n" +
                "Then click Settings → API Keys to enter it.");

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
            catch (GeminiRateLimitException ex)
            {
                lastException = ex;
                if (attempt < MaxRetries - 1)
                {
                    // Exponential backoff: 2s, 4s, 8s
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (GeminiNotFoundException ex)
            {
                // Try fallback to a different model on 404
                if (attempt == 0 && _model != "gemini-1.5-flash")
                {
                    var originalModel = _model;
                    _model = "gemini-1.5-flash";  // Stable fallback
                    try
                    {
                        var result = await SendOnceAsync(systemPrompt, messages, cancellationToken);
                        return result;
                    }
                    catch
                    {
                        _model = originalModel;
                        throw new InvalidOperationException(
                            $"Gemini model '{originalModel}' not found, and fallback to gemini-1.5-flash also failed.\n\n" +
                            $"Original error: {ex.Message}\n\n" +
                            "Possible fixes:\n" +
                            "  • Verify your API key at https://aistudio.google.com/apikey\n" +
                            "  • Ensure the Generative Language API is enabled in your Google Cloud project\n" +
                            "  • Try switching the model in Settings");
                    }
                }
                throw new InvalidOperationException(ex.Message);
            }
            catch (InvalidOperationException)
            {
                throw;  // Already a friendly message
            }
        }

        throw new InvalidOperationException(
            $"Gemini rate-limited after {MaxRetries} retries.\n\n" +
            "The free tier allows 15 requests/minute. Wait a moment and try again, " +
            "or switch to Ollama for unlimited local generation.\n\n" +
            $"Last error: {lastException?.Message}");
    }

    private async Task<string> SendOnceAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // Non-streaming endpoint — much more reliable than streamGenerateContent
        var url = $"{BaseUrl}/{_model}:generateContent?key={_apiKey}";

        var contents = messages.Select(m => new
        {
            role = m.Role == "assistant" ? "model" : "user",
            parts = VisionPayload.GeminiParts(m)
        }).ToArray();

        var request = new
        {
            contents,
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            generationConfig = new
            {
                maxOutputTokens = MaxOutputTokens,
                temperature = 0.3,         // Lower temp for more reliable JSON
                responseMimeType = "application/json"  // Force JSON output mode
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(url, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var msg = ExtractGeminiError(responseBody, (int)response.StatusCode);

            if ((int)response.StatusCode == 429)
                throw new GeminiRateLimitException(msg);
            if ((int)response.StatusCode == 404)
                throw new GeminiNotFoundException(msg);
            if ((int)response.StatusCode == 400)
                throw new InvalidOperationException(
                    $"Gemini rejected the request (400 Bad Request).\n\n{msg}\n\n" +
                    "This usually means: invalid API key, malformed request, or content blocked by safety filters.");
            if ((int)response.StatusCode == 403)
                throw new InvalidOperationException(
                    $"Gemini denied access (403 Forbidden).\n\n{msg}\n\n" +
                    "Likely causes:\n" +
                    "  • API key is invalid or expired\n" +
                    "  • Generative Language API not enabled in your Google Cloud project\n" +
                    "  • Key restrictions block this request\n\n" +
                    "Get a fresh key at https://aistudio.google.com/apikey");

            throw new InvalidOperationException(
                $"Gemini API error {(int)response.StatusCode}:\n{msg}");
        }

        // Parse the response body to extract the text
        return ExtractTextFromResponse(responseBody);
    }

    private static string ExtractTextFromResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
                return "";

            var first = candidates[0];
            if (!first.TryGetProperty("content", out var contentEl) ||
                !contentEl.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
                return "";

            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text))
                    sb.Append(text.GetString());
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not parse Gemini response.\n\nError: {ex.Message}\n\nRaw response: {responseBody[..Math.Min(500, responseBody.Length)]}");
        }
    }

    /// <summary>Extract a human-readable error from the Gemini error response body.</summary>
    private static string ExtractGeminiError(string body, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(body))
            return $"HTTP {statusCode} with empty body";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                var status = err.TryGetProperty("status", out var st) ? st.GetString() : null;
                return $"{status ?? "ERROR"}: {message ?? "no message"}";
            }
        }
        catch
        {
            // Body wasn't valid JSON — return raw
        }
        return body.Length > 500 ? body[..500] + "..." : body;
    }

    /// <summary>Test whether the API key works by listing available models.</summary>
    public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return (false, "No API key configured");

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={_apiKey}";
            using var response = await _http.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode}: {ExtractGeminiError(body, (int)response.StatusCode)}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("models", out var models))
            {
                var count = models.GetArrayLength();
                return (true, $"Connected — {count} models available");
            }
            return (true, "Connected (no model list returned)");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void Dispose() => _http.Dispose();

    // ── Custom exceptions for retry logic ──

    private class GeminiRateLimitException : Exception
    {
        public GeminiRateLimitException(string message) : base(message) { }
    }

    private class GeminiNotFoundException : Exception
    {
        public GeminiNotFoundException(string message) : base(message) { }
    }
}
