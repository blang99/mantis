using Grasshopper.Kernel;
using Mantis.Plugin.AI;
using Mantis.Plugin.ErrorHealing;
using Mantis.Plugin.Explain;
using Mantis.Plugin.Knowledge;
using Mantis.Plugin.ScriptBuilder;

namespace Mantis.Plugin;

public class MantisService : IDisposable
{
    private readonly LlmProviderManager _providerManager;
    private readonly ComponentRegistry _registry;
    private readonly PromptBuilder _promptBuilder;
    private readonly ConversationManager _conversation;
    private readonly ResponseParser _parser;
    private readonly ComponentFactory _factory;
    private readonly LayoutEngine _layout;
    private readonly CanvasBuilder _batchBuilder;
    private readonly StreamingCanvasBuilder _streamingBuilder;
    private readonly CanvasSerializer _serializer;
    private readonly ErrorMonitor _errorMonitor;
    private readonly DiagnosticBuilder _diagnosticBuilder;
    private readonly HealApplier _healApplier;
    private readonly ExplainService _explainService;
    private bool _streamingEventsWired;

    // Instance GUIDs of the components placed by the most recent build. Used by
    // IterateAsync to remove the previous Mantis graph before rebuilding the
    // complete updated script, so iterations build ON TOP without duplicating.
    private readonly List<Guid> _lastBuildGuids = new();

    public event Action<string>? OnStatus;
    public event Action<string>? OnAdvice;
    public event Action<string>? OnError;
    public event Action<int, string>? OnComponentPlaced;
    public event Action? OnBuildComplete;

    /// <summary>
    /// Fired when a multi-solution request yields 2+ buildable alternatives.
    /// The UI surfaces these as comparison tabs; the user picks one to apply
    /// via <see cref="ApplySolutionAsync"/>.
    /// </summary>
    public event Action<MultiSolutionResponse>? OnSolutionsReady;

    // Holds the most recent multi-solution set so the UI can apply a chosen
    // alternative by index without re-querying the LLM.
    private MultiSolutionResponse? _pendingSolutions;

    public ConversationManager Conversation => _conversation;
    public ErrorMonitor ErrorMonitor => _errorMonitor;
    public LlmProviderManager ProviderManager => _providerManager;

    /// <summary>Whether the active provider has a valid API key configured.</summary>
    public bool HasApiKey => _providerManager.Active.HasApiKey;

    public MantisService()
    {
        _providerManager = new LlmProviderManager();
        _registry = new ComponentRegistry();
        _promptBuilder = new PromptBuilder(_registry);
        _conversation = new ConversationManager();
        _parser = new ResponseParser();
        _factory = new ComponentFactory(_registry);
        _layout = new LayoutEngine();
        _batchBuilder = new CanvasBuilder(_factory, _layout);
        _streamingBuilder = new StreamingCanvasBuilder(_factory, _layout);
        _serializer = new CanvasSerializer();
        _errorMonitor = new ErrorMonitor();
        _diagnosticBuilder = new DiagnosticBuilder();
        _healApplier = new HealApplier(_batchBuilder);
        _explainService = new ExplainService(_providerManager, _promptBuilder);
    }

    public void Initialize()
    {
        _registry.Initialize();
        OnStatus?.Invoke($"Loaded {_registry.All.Count} components");
    }

    /// <summary>Set API key for the currently active provider.</summary>
    public void SetApiKey(string apiKey) =>
        _providerManager.Active.SetApiKey(apiKey);

    /// <summary>Set API key for a specific provider by name.</summary>
    public void SetApiKey(string providerName, string apiKey) =>
        _providerManager.SetApiKey(providerName, apiKey);

    /// <summary>Switch the active LLM provider.</summary>
    public void SetProvider(string providerName)
    {
        _providerManager.SetProvider(providerName);
        OnStatus?.Invoke($"Provider: {_providerManager.ActiveProviderName}");
    }

    /// <summary>Set the model for the active provider.</summary>
    public void SetModel(string modelId)
    {
        _providerManager.Active.SelectedModel = modelId;
    }

    public async Task GenerateAsync(
        string userPrompt, GH_Document document,
        bool streaming = true, CancellationToken ct = default)
    {
        // AUTO-ITERATE: if a previous Mantis build is still on the canvas, treat
        // this request as a follow-up that BUILDS ON TOP of it. Without this, a
        // request like "add a fillet" goes through a fresh Generate with no
        // canvas context, producing a disconnected component with no memory of
        // the existing script. The "Iterate Canvas" toggle stays as a manual
        // override for extending graphs Mantis did not author.
        if (HasPreviousBuildOnCanvas(document))
        {
            await IterateAsync(userPrompt, document, ct);
            return;
        }

        _conversation.AddUserMessage(userPrompt);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(
            PromptMode.Generate, null, userPrompt, _providerManager.Active.ContextWindowTokens);
        var messages = _conversation.GetMessagesForApi();

        OnStatus?.Invoke("Generating script...");

        try
        {
            var fullResponse = new System.Text.StringBuilder();

            await foreach (var chunk in _providerManager.Active.StreamAsync(systemPrompt, messages, ct))
            {
                fullResponse.Append(chunk);
                if (streaming) _parser.Feed(chunk);
            }

            var json = fullResponse.ToString();
            _conversation.AddAssistantMessage(json);

            var script = _parser.ParseComplete(json);
            if (script == null)
            {
                // Show what the AI returned for debugging
                var preview = json.Length > 300 ? json[..300] + "..." : json;
                OnAdvice?.Invoke($"AI response (could not parse as script):\n{preview}");
                OnError?.Invoke("Could not parse the response as a valid script. Try a larger model (llama3 8B+) or rephrase your request.");
                return;
            }

            // Empty script — the AI returned valid JSON but with no components.
            // This typically happens with very small models (≤4B params) that
            // can't grasp the catalog. Surface a clear error so the user can pivot.
            if (script.Components.Count == 0)
            {
                var preview = json.Length > 200 ? json[..200] + "..." : json;
                OnAdvice?.Invoke($"AI returned an empty script. Raw response:\n{preview}");
                OnError?.Invoke(
                    "The model produced no components for this request.\n\n" +
                    "Try one of these:\n" +
                    "  - Switch to a larger model (llama3 8B, qwen2.5 14B, or Claude/Gemini)\n" +
                    "  - Rephrase the request more concretely (e.g. \"create a circle with radius 5\")\n" +
                    "  - Break complex requests into smaller steps");
                return;
            }

            // ROBUSTNESS: catch hallucinated component names and let the model
            // correct them ONCE before building, so a single bad name doesn't
            // break a whole sub-chain of a complex graph.
            script = await ValidateAndCorrectAsync(script, PromptMode.Generate, null, userPrompt, ct);

            // Show what was generated
            var summary = $"Built \"{script.SolutionName}\" — {script.Components.Count} components, {script.Connections.Count} connections";
            OnStatus?.Invoke(summary);
            if (!string.IsNullOrWhiteSpace(script.Advice))
                OnAdvice?.Invoke(script.Advice);

            // Show required plugins if any
            if (script.RequiredPlugins.Count > 0)
            {
                var pluginList = string.Join(", ", script.RequiredPlugins);
                OnAdvice?.Invoke(
                    $"This script requires plugins not installed by default:\n" +
                    $"{pluginList}\n\n" +
                    "Download them from food4rhino.com and install via Rhino Package Manager.");
            }

            if (streaming)
            {
                await BuildScriptLiveAsync(script, document, ct);
            }
            else
            {
                var result = _batchBuilder.Build(script, document);
                OnStatus?.Invoke($"Placed {result.PlacedComponents} components, {result.WiredConnections} connections");
                foreach (var error in result.Errors)
                    OnError?.Invoke(error);
                OnBuildComplete?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            OnStatus?.Invoke("Generation cancelled.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Generation failed: {ex.Message}");
        }
        finally
        {
            _parser.Reset();
        }
    }

    /// <summary>
    /// Place a parsed script on the canvas one component at a time with small
    /// delays so the user watches it build live (the Track 2 experience).
    /// Shared by single-shot generation and multi-solution "apply".
    /// </summary>
    private async Task BuildScriptLiveAsync(
        ScriptDefinition script, GH_Document document, CancellationToken ct)
    {
        _streamingBuilder.BeginBuild(document);

        // Wire events only once to avoid duplicates
        if (!_streamingEventsWired)
        {
            _streamingBuilder.OnComponentPlaced += (id, name) => OnComponentPlaced?.Invoke(id, name);
            _streamingBuilder.OnBuildComplete += () => OnBuildComplete?.Invoke();
            _streamingBuilder.OnBuildError += msg => OnError?.Invoke(msg);
            _streamingEventsWired = true;
        }

        // ── LIVE PLACEMENT: place each component with a visible delay so the
        //    user sees them appear one-by-one on the canvas, like Raven. ──
        const int componentDelayMs = 350;
        const int connectionDelayMs = 150;

        foreach (var comp in script.Components)
        {
            if (ct.IsCancellationRequested) break;
            await _streamingBuilder.PlaceComponentAsync(comp);
            await Task.Delay(componentDelayMs, ct);
        }

        foreach (var conn in script.Connections)
        {
            if (ct.IsCancellationRequested) break;
            await _streamingBuilder.AddConnectionAsync(conn);
            await Task.Delay(connectionDelayMs, ct);
        }

        await _streamingBuilder.FinalizeBuildAsync(script);

        // Remember what we just placed so a follow-up iteration can replace it
        // cleanly (build on top without duplicating the previous graph).
        _lastBuildGuids.Clear();
        _lastBuildGuids.AddRange(_streamingBuilder.PlacedGuids);
    }

    /// <summary>
    /// Remove the components placed by the previous Mantis build (tracked by
    /// instance GUID) so an iteration can rebuild the complete updated script
    /// without leaving duplicates behind. Wrapped in an undo record and leaves
    /// any user-authored objects untouched.
    /// </summary>
    private void ClearPreviousBuild(GH_Document document)
    {
        if (_lastBuildGuids.Count == 0) return;

        document.UndoUtil.RecordEvent(
            new Grasshopper.Kernel.Undo.GH_UndoRecord("Mantis: Replace Previous Script"));

        foreach (var guid in _lastBuildGuids)
        {
            var obj = document.FindObject(guid, true);
            if (obj != null)
                document.RemoveObject(obj, false);
        }

        _lastBuildGuids.Clear();
        document.NewSolution(false);
    }

    /// <summary>
    /// True when at least one component from the previous Mantis build still
    /// exists on the canvas. Drives the auto-iterate decision: a follow-up
    /// request builds on top of the existing script instead of starting fresh.
    /// Returns false after "New Chat" (which clears the tracked GUIDs) or if the
    /// user deleted the generated graph.
    /// </summary>
    private bool HasPreviousBuildOnCanvas(GH_Document document)
    {
        if (_lastBuildGuids.Count == 0) return false;
        foreach (var guid in _lastBuildGuids)
        {
            if (document.FindObject(guid, true) != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detect component names the model invented that don't resolve to a real
    /// catalog component, then ask the model — exactly once — to correct them
    /// using the closest valid names. Returns the corrected script if the retry
    /// reduced the number of unknown names, otherwise the original (so we still
    /// build whatever IS valid). This is the main robustness lever for complex
    /// requests: without it, one hallucinated name silently drops a component and
    /// cascades into broken wiring across a whole sub-chain.
    /// </summary>
    private async Task<ScriptDefinition> ValidateAndCorrectAsync(
        ScriptDefinition script, PromptMode mode, string? canvasState, string? userRequest, CancellationToken ct)
    {
        var unresolved = script.Components
            .Select(c => c.Name)
            .Where(n => !_factory.CanResolve(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unresolved.Count == 0) return script;

        OnStatus?.Invoke($"Fixing {unresolved.Count} unrecognized component name(s)...");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Your previous JSON used component names that are NOT in the catalog and cannot be placed:");
        foreach (var name in unresolved)
        {
            var suggestions = _registry.SuggestNames(name, 5);
            var hint = suggestions.Count > 0
                ? string.Join(", ", suggestions)
                : "(no close match — choose a different catalog component that achieves the same result)";
            sb.AppendLine($"  - \"{name}\"  -> closest valid catalog names: {hint}");
        }
        sb.AppendLine("Re-output the COMPLETE corrected JSON using ONLY exact catalog names. Keep every valid component, id, and connection unchanged. Output ONLY the JSON object.");

        try
        {
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                mode, canvasState, userRequest, _providerManager.Active.ContextWindowTokens);
            _conversation.AddUserMessage(sb.ToString());
            var messages = _conversation.GetMessagesForApi();
            var corrected = await _providerManager.Active.SendAsync(systemPrompt, messages, ct);
            _conversation.AddAssistantMessage(corrected);

            var correctedScript = _parser.ParseComplete(corrected);
            if (correctedScript == null || correctedScript.Components.Count == 0)
                return script;

            var stillBad = correctedScript.Components.Count(c => !_factory.CanResolve(c.Name));
            return stillBad < unresolved.Count ? correctedScript : script;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return script; // correction is best-effort; fall back to the original
        }
    }

    public async Task GenerateMultiSolutionAsync(
        string userPrompt, GH_Document document, CancellationToken ct = default)
    {
        _conversation.AddUserMessage(userPrompt);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(
            PromptMode.MultiSolution, null, userPrompt, _providerManager.Active.ContextWindowTokens);
        var messages = _conversation.GetMessagesForApi();

        OnStatus?.Invoke("Generating multiple solutions...");

        try
        {
            var response = await _providerManager.Active.SendAsync(systemPrompt, messages, ct);
            _conversation.AddAssistantMessage(response);

            // Preferred path: 2+ distinct buildable alternatives → comparison tabs.
            var multi = _parser.ParseMultiSolution(response);
            if (multi != null && multi.Solutions.Count > 0)
            {
                _pendingSolutions = multi;
                if (!string.IsNullOrWhiteSpace(multi.ComparisonSummary))
                    OnAdvice?.Invoke(multi.ComparisonSummary);
                OnSolutionsReady?.Invoke(multi);
                OnStatus?.Invoke($"{multi.Solutions.Count} solutions ready — pick one to build.");
                return;
            }

            // Fallback: the model returned a single script despite the multi prompt.
            var script = _parser.ParseComplete(response);
            if (script != null && script.Components.Count > 0)
            {
                _pendingSolutions = null;
                if (!string.IsNullOrWhiteSpace(script.Advice))
                    OnAdvice?.Invoke(script.Advice);
                OnStatus?.Invoke("One solution returned — building it.");
                await BuildScriptLiveAsync(script, document, ct);
            }
            else
            {
                var preview = response.Length > 300 ? response[..300] + "..." : response;
                OnAdvice?.Invoke($"AI response (could not parse as script):\n{preview}");
                OnError?.Invoke("Could not parse the response as a valid script. Try a larger model (llama3 8B+) or rephrase your request.");
            }
        }
        catch (OperationCanceledException)
        {
            OnStatus?.Invoke("Generation cancelled.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Multi-solution generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Build one of the alternatives from the most recent multi-solution set,
    /// chosen by index. Wrapped so the UI can offer "Apply" buttons per tab.
    /// </summary>
    public async Task ApplySolutionAsync(
        int index, GH_Document document, CancellationToken ct = default)
    {
        if (_pendingSolutions == null ||
            index < 0 || index >= _pendingSolutions.Solutions.Count)
        {
            OnError?.Invoke("That solution is no longer available — generate again.");
            return;
        }

        var script = _pendingSolutions.Solutions[index];
        OnStatus?.Invoke($"Applying \"{script.SolutionName}\"...");

        try
        {
            await BuildScriptLiveAsync(script, document, ct);
        }
        catch (OperationCanceledException)
        {
            OnStatus?.Invoke("Apply cancelled.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Apply failed: {ex.Message}");
        }
    }

    public async Task IterateAsync(
        string userPrompt, GH_Document document, CancellationToken ct = default)
    {
        var canvasState = _serializer.Serialize(document);
        _conversation.AddUserMessage(userPrompt);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(
            PromptMode.Iterate, canvasState, userPrompt, _providerManager.Active.ContextWindowTokens);
        var messages = _conversation.GetMessagesForApi();

        OnStatus?.Invoke("Iterating on current script...");

        try
        {
            var response = await _providerManager.Active.SendAsync(systemPrompt, messages, ct);
            _conversation.AddAssistantMessage(response);

            var script = _parser.ParseComplete(response);
            if (script == null || script.Components.Count == 0)
            {
                var preview = response.Length > 300 ? response[..300] + "..." : response;
                OnAdvice?.Invoke($"AI response (could not parse as script):\n{preview}");
                OnError?.Invoke("Could not parse the response as a valid script. Try a larger model (llama3 8B+) or rephrase your request.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(script.Advice))
                OnAdvice?.Invoke(script.Advice);

            // ROBUSTNESS: correct any hallucinated component names once before we
            // tear down and rebuild, so iterations stay buildable too.
            script = await ValidateAndCorrectAsync(script, PromptMode.Iterate, canvasState, userPrompt, ct);

            // The model returns the COMPLETE updated graph (existing + changes).
            // Remove the previous Mantis graph first so we build on top without
            // duplicating, then live-build (which also runs the repair pass that
            // guarantees a working, warning-free file).
            ClearPreviousBuild(document);
            await BuildScriptLiveAsync(script, document, ct);

            OnStatus?.Invoke($"Iteration complete: {script.Components.Count} components");
        }
        catch (OperationCanceledException)
        {
            OnStatus?.Invoke("Iteration cancelled.");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Iteration failed: {ex.Message}");
        }
    }

    public async Task HealErrorsAsync(GH_Document document, CancellationToken ct = default)
    {
        var errors = _errorMonitor.CurrentErrors;
        if (errors.Count == 0)
        {
            OnStatus?.Invoke("No errors to heal.");
            return;
        }

        var diagnosticContext = _diagnosticBuilder.BuildDiagnosticContext(errors, document);
        var canvasState = _serializer.Serialize(document);
        var systemPrompt = _promptBuilder.BuildSystemPrompt(
            PromptMode.Heal, canvasState, null, _providerManager.Active.ContextWindowTokens);
        var messages = new List<ChatMessage>
        {
            ChatMessage.User($"Fix these errors:\n{diagnosticContext}")
        };

        OnStatus?.Invoke($"Healing {errors.Count} errors...");

        try
        {
            var response = await _providerManager.Active.SendAsync(systemPrompt, messages, ct);
            var fix = _parser.ParseComplete(response);
            if (fix == null)
            {
                OnError?.Invoke("Failed to parse healing response.");
                return;
            }

            OnAdvice?.Invoke(fix.Advice);
            var result = _healApplier.ApplyFix(fix, document);
            OnStatus?.Invoke($"Healing complete: {result.PlacedComponents} components replaced");
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Healing failed: {ex.Message}");
        }
    }

    public async Task<string> ExplainComponentAsync(
        IGH_DocumentObject obj, CancellationToken ct = default)
    {
        OnStatus?.Invoke($"Explaining {obj.Name}...");
        return await _explainService.ExplainComponent(obj, ct);
    }

    public async Task<string> ExplainChainAsync(
        IGH_DocumentObject obj, CancellationToken ct = default)
    {
        OnStatus?.Invoke($"Explaining chain from {obj.Name}...");
        return await _explainService.ExplainChain(obj, ct);
    }

    public void StartErrorMonitoring(GH_Document document) =>
        _errorMonitor.StartMonitoring(document);

    public void NewConversation()
    {
        _conversation.Clear();
        _explainService.ClearCache();
        // Forget the previous build so the next request starts fresh instead of
        // auto-iterating on a graph the user has mentally "closed the book" on.
        _lastBuildGuids.Clear();
    }

    public void Dispose()
    {
        _providerManager.Dispose();
        _streamingBuilder.Dispose();
        _errorMonitor.Dispose();
    }
}
