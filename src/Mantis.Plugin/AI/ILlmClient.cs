using System.Runtime.CompilerServices;

namespace Mantis.Plugin.AI;

public interface ILlmClient : IDisposable
{
    string ProviderName { get; }
    string SelectedModel { get; set; }
    List<ModelOption> AvailableModels { get; }
    bool HasApiKey { get; }
    void SetApiKey(string apiKey);

    /// <summary>
    /// Approximate context window (in tokens) of the currently selected model.
    /// Drives adaptive catalog sizing: large-context cloud models get the full
    /// component catalog; small-context local models get a relevance-trimmed one.
    /// </summary>
    int ContextWindowTokens { get; }

    IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    Task<string> SendAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}

public class ModelOption
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsFree { get; set; }
}
