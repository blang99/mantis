using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mantis.Plugin.AI;

public class OpenAIClient : ILlmClient
{
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-4o-mini";
    private const int MaxTokens = 8192;
    private const int MaxRetries = 3;

    private readonly HttpClient _http;
    private string _apiKey = "";
    private string _model = DefaultModel;

    public OpenAIClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public string ProviderName => "OpenAI";

    // GPT-4o / GPT-4.1 families are 128K-token context — full catalog.
    public int ContextWindowTokens => 128_000;

    public string SelectedModel
    {
        get => _model;
        set => _model = value;
    }

    public List<ModelOption> AvailableModels => new()
    {
        new ModelOption
        {
            Id = "gpt-4o-mini",
            DisplayName = "GPT-4o Mini",
            Description = "Fast, cheap (recommended)",
            IsFree = false
        },
        new ModelOption
        {
            Id = "gpt-4o",
            DisplayName = "GPT-4o",
            Description = "Flagship multimodal",
            IsFree = false
        },
        new ModelOption
        {
            Id = "gpt-4.1",
            DisplayName = "GPT-4.1",
            Description = "Latest reasoning",
            IsFree = false
        },
        new ModelOption
        {
            Id = "gpt-4.1-mini",
            DisplayName = "GPT-4.1 Mini",
            Description = "Latest fast model",
            IsFree = false
        },
        new ModelOption
        {
            Id = "o1-mini",
            DisplayName = "o1-mini",
            Description = "Reasoning, fast",
            IsFree = false
        },
        new ModelOption
        {
            Id = "o3-mini",
            DisplayName = "o3-mini",
            Description = "Latest reasoning",
            IsFree = false
        }
    };

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey?.Trim() ?? "";
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!HasApiKey)
            throw new InvalidOperationException(
                "No OpenAI API key configured.\n" +
                "Get one at https://platform.openai.com/api-keys\n" +
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
            try { return await SendOnceAsync(systemPrompt, messages, cancellationToken); }
            catch (OpenAIRateLimitException ex)
            {
                lastException = ex;
                if (attempt < MaxRetries - 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cancellationToken);
            }
            catch (InvalidOperationException) { throw; }
        }
        throw new InvalidOperationException(
            $"OpenAI rate-limited after {MaxRetries} retries.\n\nLast error: {lastException?.Message}");
    }

    private async Task<string> SendOnceAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var allMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        allMessages.AddRange(
            messages.Select(m => (object)new { role = m.Role, content = m.Content }));

        // o1 / o3 models don't support response_format or temperature
        bool isReasoning = _model.StartsWith("o1") || _model.StartsWith("o3");

        object request = isReasoning
            ? new { model = _model, messages = allMessages, max_completion_tokens = MaxTokens }
            : new
            {
                model = _model,
                max_tokens = MaxTokens,
                temperature = 0.3,
                messages = allMessages,
                response_format = new { type = "json_object" }
            };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = content };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = ExtractError(body);
            var statusCode = (int)response.StatusCode;
            if (statusCode == 429) throw new OpenAIRateLimitException(errorMsg);
            if (statusCode == 401)
                throw new InvalidOperationException(
                    $"OpenAI API key invalid (401).\n\n{errorMsg}\n\nGet a key at https://platform.openai.com/api-keys");
            if (statusCode == 404)
                throw new InvalidOperationException($"OpenAI model '{_model}' not found (404). {errorMsg}");
            throw new InvalidOperationException($"OpenAI error {statusCode}: {errorMsg}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return "";
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not parse OpenAI response: {ex.Message}");
        }
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "empty body";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                return message ?? body[..Math.Min(300, body.Length)];
            }
        }
        catch { }
        return body.Length > 500 ? body[..500] + "..." : body;
    }

    public void Dispose() => _http.Dispose();

    private class OpenAIRateLimitException : Exception
    {
        public OpenAIRateLimitException(string message) : base(message) { }
    }
}
