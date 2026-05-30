using Grasshopper.Kernel;
using Mantis.Plugin.AI;
using Mantis.Plugin.Knowledge;

namespace Mantis.Plugin.Explain;

public class ExplainService
{
    private readonly LlmProviderManager _providerManager;
    private readonly PromptBuilder _promptBuilder;
    private readonly ComponentAnalyzer _analyzer;
    private readonly ChainTracer _tracer;
    private readonly Dictionary<Guid, string> _cache = new();

    public ExplainService(LlmProviderManager providerManager, PromptBuilder promptBuilder)
    {
        _providerManager = providerManager;
        _promptBuilder = promptBuilder;
        _analyzer = new ComponentAnalyzer();
        _tracer = new ChainTracer();
    }

    public async Task<string> ExplainComponent(IGH_DocumentObject obj, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(obj.InstanceGuid, out var cached))
            return cached;

        var analysis = _analyzer.AnalyzeComponent(obj);
        var systemPrompt = _promptBuilder.BuildExplainPrompt();
        var messages = new List<ChatMessage>
        {
            ChatMessage.User($"Explain this Grasshopper component:\n{analysis}")
        };

        var explanation = await _providerManager.Active.SendAsync(systemPrompt, messages, ct);
        _cache[obj.InstanceGuid] = explanation;
        return explanation;
    }

    public async Task<string> ExplainChain(IGH_DocumentObject start, CancellationToken ct = default)
    {
        var chain = _tracer.TraceFullChain(start);
        var analysis = _analyzer.AnalyzeChain(chain);
        var systemPrompt = _promptBuilder.BuildExplainPrompt();
        var messages = new List<ChatMessage>
        {
            ChatMessage.User($"Explain what this Grasshopper component chain does:\n{analysis}")
        };

        return await _providerManager.Active.SendAsync(systemPrompt, messages, ct);
    }

    public void ClearCache() => _cache.Clear();

    public void InvalidateCache(Guid instanceGuid) => _cache.Remove(instanceGuid);
}
