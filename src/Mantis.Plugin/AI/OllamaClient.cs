using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Mantis.Plugin.AI;

public class OllamaClient : ILlmClient
{
    private const string DefaultBaseUrl = "http://localhost:11434";
    private const string DefaultModel = "llama3.1:8b";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private string _model = DefaultModel;
    private List<ModelOption>? _detectedModels;

    // Fallback model list when Ollama is unreachable during init
    private static readonly List<ModelOption> FallbackModels = new()
    {
        new ModelOption { Id = "llama3.1:8b", DisplayName = "Llama 3.1 8B", Description = "Fast local", IsFree = true },
        new ModelOption { Id = "qwen2.5:14b", DisplayName = "Qwen 2.5 14B", Description = "Strong local", IsFree = true },
        new ModelOption { Id = "deepseek-r1:8b", DisplayName = "DeepSeek R1 8B", Description = "Reasoning", IsFree = true },
        new ModelOption { Id = "mistral:7b", DisplayName = "Mistral 7B", Description = "Lightweight", IsFree = true },
        new ModelOption { Id = "gemma3:4b", DisplayName = "Gemma 3 4B", Description = "Compact", IsFree = true }
    };

    public OllamaClient(string baseUrl = DefaultBaseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };

        // Auto-select the largest installed model on construction. Small
        // models (≤4B) can't reliably produce structured Grasshopper JSON,
        // so default to the most capable one the user has pulled.
        TrySelectLargestInstalledModel();
    }

    private void TrySelectLargestInstalledModel()
    {
        try
        {
            var models = DetectModelsSync();
            if (models.Count == 0) return;

            // Pick the model with the largest param count. Description is
            // formatted like "8B — local" or "3.2B — local" by ParseModelList.
            var best = models
                .OrderByDescending(m => ExtractParamCount(m.Description))
                .FirstOrDefault();
            if (best != null && ExtractParamCount(best.Description) > 0)
                _model = best.Id;
        }
        catch
        {
            // Fall back to DefaultModel — TestConnectionAsync surfaces errors later
        }
    }

    private static double ExtractParamCount(string description)
    {
        if (string.IsNullOrEmpty(description)) return 0;
        var match = System.Text.RegularExpressions.Regex.Match(description, @"(\d+(?:\.\d+)?)\s*B");
        return match.Success && double.TryParse(match.Groups[1].Value, out var n) ? n : 0;
    }

    public string ProviderName => "Ollama";

    // Local models run with num_ctx=32768 (see StreamAsync). The full ~1000+
    // component catalog alone would nearly fill that and starve the response, so
    // this value keeps Ollama below the trim threshold → relevance-trimmed catalog.
    public int ContextWindowTokens => 32_768;

    public string SelectedModel
    {
        get => _model;
        set => _model = value;
    }

    public List<ModelOption> AvailableModels
    {
        get
        {
            // Only treat a cached result as valid if it actually has models. Caching an
            // EMPTY detected list (Ollama running but nothing pulled) and returning it on
            // the next call is what made callers index [0] into an empty list and crash
            // panel start-up. So we never cache or return an empty list.
            if (_detectedModels is { Count: > 0 }) return _detectedModels;

            try
            {
                var detected = DetectModelsSync();
                if (detected.Count > 0) { _detectedModels = detected; return _detectedModels; }
            }
            catch
            {
                // Ollama not running — fall through to the built-in fallback list.
            }

            return FallbackModels;
        }
    }

    /// <summary>True only when a live Ollama server reported at least one pulled model.</summary>
    public bool HasLocalModels => _detectedModels is { Count: > 0 };

    public bool HasApiKey => true; // No key needed

    public void SetApiKey(string apiKey)
    {
        // No-op: Ollama runs locally and requires no API key.
    }

    /// <summary>Returns a warning if the selected model might produce poor results.</summary>
    public string? GetModelQualityWarning()
    {
        // Models under ~7B parameters struggle with structured JSON output
        var smallModels = new[] { "llama3.2", "gemma2:2b", "phi3:mini", "tinyllama", "qwen2.5:3b", "gemma3:1b", "gemma3:4b" };
        var modelBase = _model.Split(':')[0].ToLowerInvariant();

        if (smallModels.Any(s => modelBase.StartsWith(s.Split(':')[0])))
        {
            // Check if the specific tag is small
            if (_model.Contains("3.2") || _model.Contains("2b") || _model.Contains("1b") || _model.Contains("3b") || _model.Contains("4b") || _model.Contains("mini"))
                return $"⚠ {_model} is a small model and may produce unreliable scripts. For best results, use llama3 (8B) or larger.";
        }

        return null;
    }

    /// <summary>
    /// Refresh the detected model list from the running Ollama instance.
    /// Call this after user starts Ollama or pulls a new model.
    /// </summary>
    public async Task<List<ModelOption>> RefreshModelsAsync(CancellationToken ct = default)
    {
        _detectedModels = await DetectModelsAsync(ct);
        return _detectedModels;
    }

    /// <summary>Test whether Ollama is reachable at the configured URL.</summary>
    public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("/api/tags", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var models = doc.RootElement.GetProperty("models");
                var count = models.GetArrayLength();
                return (true, $"Connected — {count} model{(count == 1 ? "" : "s")} available");
            }
            return (false, $"Ollama responded with {(int)response.StatusCode}");
        }
        catch (HttpRequestException)
        {
            return (false, $"Cannot reach Ollama at {_baseUrl}. Is it running?\n\nStart it with:  ollama serve");
        }
        catch (TaskCanceledException)
        {
            return (false, $"Connection to Ollama timed out at {_baseUrl}");
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ── Pre-flight: check Ollama is running ──
        var (connected, connMsg) = await TestConnectionAsync(cancellationToken);
        if (!connected)
            throw new InvalidOperationException(
                $"Ollama is not running.\n{connMsg}");

        // ── Check the selected model is available ──
        var models = await DetectModelsAsync(cancellationToken);
        var modelAvailable = models.Any(m => m.Id == _model);
        if (!modelAvailable && models.Count > 0)
        {
            // Try partial match (user might have "llama3.1:8b" but Ollama reports "llama3.1:8b" or "llama3.1:latest")
            var partial = models.FirstOrDefault(m =>
                m.Id.StartsWith(_model.Split(':')[0], StringComparison.OrdinalIgnoreCase));
            if (partial != null)
            {
                _model = partial.Id;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Model '{_model}' is not installed in Ollama.\n\n" +
                    $"Pull it first:  ollama pull {_model}\n\n" +
                    $"Available models: {string.Join(", ", models.Select(m => m.Id))}");
            }
        }
        else if (models.Count == 0)
        {
            throw new InvalidOperationException(
                "Ollama has no models installed.\n\n" +
                $"Pull one first:  ollama pull {_model}");
        }

        // ── Build request ──
        var allMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        allMessages.AddRange(messages.Select(m =>
        {
            // Ollama carries images in a top-level `images` array on the message.
            // Omit the field entirely for text-only turns so the wire shape is
            // byte-identical to before (a null array would still serialize).
            var images = VisionPayload.OllamaImages(m);
            return images is null
                ? (object)new { role = m.Role, content = m.Content }
                : (object)new { role = m.Role, content = m.Content, images };
        }));

        var request = new
        {
            model = _model,
            stream = true,
            format = "json",              // Force JSON output mode
            messages = allMessages,
            options = new { num_ctx = 32768 }  // Large context for full catalog
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"{_baseUrl}/api/chat";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to Ollama at {_baseUrl}.\n" +
                $"Error: {ex.Message}\n\n" +
                "Make sure Ollama is running:  ollama serve", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Ollama returned error {(int)response.StatusCode}:\n{errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // Check for errors in the response
            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(
                    $"Ollama error: {error.GetString()}");
            }

            if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                break;

            if (root.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var text))
            {
                var chunk = text.GetString();
                if (!string.IsNullOrEmpty(chunk))
                    yield return chunk;
            }
        }
    }

    public async Task<string> SendAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in StreamAsync(systemPrompt, messages, cancellationToken))
        {
            sb.Append(chunk);
        }
        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();

    // ── Private helpers ──

    private async Task<List<ModelOption>> DetectModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("/api/tags", ct);
            if (!response.IsSuccessStatusCode) return new List<ModelOption>();

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseModelList(json);
        }
        catch
        {
            return new List<ModelOption>();
        }
    }

    private List<ModelOption> DetectModelsSync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = _http.GetAsync("/api/tags", cts.Token).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return new List<ModelOption>();

            var json = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            return ParseModelList(json);
        }
        catch
        {
            return new List<ModelOption>();
        }
    }

    private static List<ModelOption> ParseModelList(string json)
    {
        var result = new List<ModelOption>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("models", out var models))
            return result;

        foreach (var model in models.EnumerateArray())
        {
            var name = model.GetProperty("name").GetString() ?? "";
            if (string.IsNullOrEmpty(name)) continue;

            // Get size info if available
            var sizeDesc = "";
            if (model.TryGetProperty("size", out var size))
            {
                var sizeGb = size.GetInt64() / (1024.0 * 1024.0 * 1024.0);
                sizeDesc = sizeGb >= 1 ? $"{sizeGb:F1}GB" : $"{sizeGb * 1024:F0}MB";
            }

            // Get parameter count from details
            var paramSize = "";
            if (model.TryGetProperty("details", out var details) &&
                details.TryGetProperty("parameter_size", out var ps))
            {
                paramSize = ps.GetString() ?? "";
            }

            var desc = !string.IsNullOrEmpty(paramSize)
                ? $"{paramSize} — local"
                : !string.IsNullOrEmpty(sizeDesc)
                    ? $"{sizeDesc} — local"
                    : "Local model";

            // Clean up the display name
            var displayName = name.Contains(':')
                ? name.Split(':')[0].Replace("-", " ")
                : name.Replace("-", " ");

            // Capitalize first letter of each word
            displayName = string.Join(" ",
                displayName.Split(' ').Select(w =>
                    w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

            result.Add(new ModelOption
            {
                Id = name,
                DisplayName = displayName,
                Description = desc,
                IsFree = true,
                SupportsVision = IsVisionModel(name)
            });
        }

        return result;
    }

    /// <summary>
    /// Best-effort detection of locally-installed vision models by name fragment.
    /// Ollama's /api/tags doesn't expose modality, so we match the well-known
    /// multimodal families (llava, qwen-VL, llama3.2-vision, moondream, etc.).
    /// This is what lights up the attach-image affordance on the offline path —
    /// MANTIS's unique answer to Raven's cloud-only image-to-script.
    /// </summary>
    private static bool IsVisionModel(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("llava")
            || n.Contains("vision")
            || n.Contains("-vl")
            || n.Contains("vl:")
            || n.Contains("moondream")
            || n.Contains("bakllava")
            || n.Contains("minicpm-v")
            || n.Contains("llama3.2-vision");
    }
}
