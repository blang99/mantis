using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mantis.Plugin.AI;

public class OpenRouterClient : ILlmClient
{
    private const string ApiUrl = "https://openrouter.ai/api/v1/chat/completions";
    private const string DefaultModel = "deepseek/deepseek-chat-v3.1:free";
    private const int MaxTokens = 8192;
    private const int MaxRetries = 3;

    private readonly HttpClient _http;
    private string _apiKey = "";
    private string _model = DefaultModel;

    public OpenRouterClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Add("HTTP-Referer", "https://mantis.grasshopper");
        _http.DefaultRequestHeaders.Add("X-Title", "MANTIS");
    }

    public string ProviderName => "OpenRouter";

    // OpenRouter proxies cloud models that are virtually all 64K-1M context
    // (the curated list here is 128K+). Treat as large-context — full catalog.
    public int ContextWindowTokens => 128_000;

    public string SelectedModel
    {
        get => _model;
        set => _model = value;
    }

    /// <summary>
    /// Curated model list — ordered by reliability for structured JSON output.
    /// Free tier models are listed first so users escape rate limits easily.
    /// </summary>
    public List<ModelOption> AvailableModels => new()
    {
        // ── FREE — most reliable for JSON ──
        new ModelOption
        {
            Id = "deepseek/deepseek-chat-v3.1:free",
            DisplayName = "DeepSeek Chat v3.1 (Free)",
            Description = "Best free model for JSON",
            IsFree = true
        },
        new ModelOption
        {
            Id = "meta-llama/llama-3.3-70b-instruct:free",
            DisplayName = "Llama 3.3 70B (Free)",
            Description = "Strong free model",
            IsFree = true
        },
        new ModelOption
        {
            Id = "qwen/qwen-2.5-coder-32b-instruct:free",
            DisplayName = "Qwen 2.5 Coder 32B (Free)",
            Description = "Code/JSON specialist",
            IsFree = true
        },
        new ModelOption
        {
            Id = "deepseek/deepseek-r1:free",
            DisplayName = "DeepSeek R1 (Free)",
            Description = "Reasoning model",
            IsFree = true
        },
        new ModelOption
        {
            Id = "google/gemini-2.0-flash-exp:free",
            DisplayName = "Gemini 2.0 Flash Exp (Free)",
            Description = "Fast & free",
            IsFree = true
        },
        new ModelOption
        {
            Id = "mistralai/mistral-nemo:free",
            DisplayName = "Mistral Nemo (Free)",
            Description = "Lightweight free",
            IsFree = true
        },
        new ModelOption
        {
            Id = "meta-llama/llama-4-maverick:free",
            DisplayName = "Llama 4 Maverick (Free)",
            Description = "Latest Meta open model",
            IsFree = true
        },
        new ModelOption
        {
            Id = "nvidia/llama-3.1-nemotron-70b-instruct:free",
            DisplayName = "Nemotron 70B (Free)",
            Description = "NVIDIA-tuned Llama",
            IsFree = true
        },

        // ── PAID — premium quality ──
        new ModelOption
        {
            Id = "anthropic/claude-sonnet-4.7",
            DisplayName = "Claude Sonnet 4.7",
            Description = "Anthropic premium",
            IsFree = false
        },
        new ModelOption
        {
            Id = "anthropic/claude-opus-4.7",
            DisplayName = "Claude Opus 4.7",
            Description = "Most capable Claude",
            IsFree = false
        },
        new ModelOption
        {
            Id = "openai/gpt-4o",
            DisplayName = "GPT-4o",
            Description = "OpenAI flagship",
            IsFree = false
        },
        new ModelOption
        {
            Id = "openai/gpt-4o-mini",
            DisplayName = "GPT-4o Mini",
            Description = "Fast OpenAI",
            IsFree = false
        },
        new ModelOption
        {
            Id = "openai/o1-mini",
            DisplayName = "OpenAI o1-mini",
            Description = "Reasoning",
            IsFree = false
        },
        new ModelOption
        {
            Id = "google/gemini-2.5-pro",
            DisplayName = "Gemini 2.5 Pro",
            Description = "Google premium",
            IsFree = false
        },
        new ModelOption
        {
            Id = "deepseek/deepseek-chat",
            DisplayName = "DeepSeek Chat (paid)",
            Description = "Higher rate limits",
            IsFree = false
        },
        new ModelOption
        {
            Id = "x-ai/grok-2-1212",
            DisplayName = "Grok 2",
            Description = "xAI's flagship",
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
                "No OpenRouter API key configured.\n" +
                "Get one at https://openrouter.ai/keys (free tier available)\n" +
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
            catch (OpenRouterRateLimitException ex)
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
            $"OpenRouter rate-limited after {MaxRetries} retries.\n\n" +
            $"Last error: {lastException?.Message}\n\n" +
            "Try a different model — many free options have separate quotas.");
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

        // Use OpenAI-compatible JSON response format
        var request = new
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

            if (statusCode == 429)
                throw new OpenRouterRateLimitException(errorMsg);
            if (statusCode == 401)
                throw new InvalidOperationException(
                    $"OpenRouter API key invalid (401).\n\n{errorMsg}\n\n" +
                    "Get a key at https://openrouter.ai/keys");
            if (statusCode == 402)
                throw new InvalidOperationException(
                    $"OpenRouter credit required (402).\n\n{errorMsg}\n\n" +
                    "Switch to a :free model or add credits at https://openrouter.ai/credits");
            if (statusCode == 404)
                throw new InvalidOperationException(
                    $"Model '{_model}' not found (404).\n\n{errorMsg}\n\n" +
                    "Switch to a different model in Settings.");
            throw new InvalidOperationException($"OpenRouter error {statusCode}: {errorMsg}");
        }

        // Parse OpenAI-compatible response
        try
        {
            using var doc = JsonDocument.Parse(body);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return "";
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not parse OpenRouter response: {ex.Message}\n\nRaw: {body[..Math.Min(500, body.Length)]}");
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

    private class OpenRouterRateLimitException : Exception
    {
        public OpenRouterRateLimitException(string message) : base(message) { }
    }
}
