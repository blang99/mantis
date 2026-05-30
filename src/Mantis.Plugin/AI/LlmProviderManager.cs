namespace Mantis.Plugin.AI;

public class LlmProviderManager : IDisposable
{
    private readonly Dictionary<string, ILlmClient> _providers = new();
    private ILlmClient _active;

    public LlmProviderManager()
    {
        var claude = new ClaudeClient();
        var gemini = new GeminiClient();
        var openAI = new OpenAIClient();
        var openRouter = new OpenRouterClient();
        var ollama = new OllamaClient();

        // Order matters — first one shown in dropdown is the default highlight.
        // OpenRouter listed first because its free tier (DeepSeek/Llama 3.3)
        // has the most generous rate limits with no setup beyond an API key.
        _providers["OpenRouter"] = openRouter;
        _providers["Claude"] = claude;
        _providers["OpenAI"] = openAI;
        _providers["Gemini"] = gemini;
        _providers["Ollama"] = ollama;

        // Default to Ollama if the user has it running locally (no rate limits,
        // no key needed). Otherwise OpenRouter — best free tier on the cloud side.
        _active = ollama.AvailableModels.Count > 0 && ollama.AvailableModels[0].Description.Contains("local")
            ? ollama
            : openRouter;
    }

    public IReadOnlyList<string> ProviderNames => _providers.Keys.ToList();
    public ILlmClient Active => _active;
    public string ActiveProviderName => _active.ProviderName;

    public void SetProvider(string name)
    {
        if (_providers.TryGetValue(name, out var client))
            _active = client;
    }

    public void SetApiKey(string providerName, string apiKey)
    {
        if (_providers.TryGetValue(providerName, out var client))
            client.SetApiKey(apiKey);
    }

    public ILlmClient GetProvider(string name) =>
        _providers.TryGetValue(name, out var client) ? client : _active;

    public void Dispose()
    {
        foreach (var p in _providers.Values)
            p.Dispose();
    }
}
