using System.Text;
using System.Text.Json;
using Mantis.Plugin.ScriptBuilder;

namespace Mantis.Plugin.AI;

public class ResponseParser
{
    public event Action<ComponentDef>? OnComponentParsed;
    public event Action<ConnectionDef>? OnConnectionParsed;
    public event Action<ScriptDefinition>? OnScriptComplete;
    public event Action<string>? OnAdvice;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public string? LastRawResponse { get; private set; }

    private readonly StringBuilder _buffer = new();
    private bool _inJson;
    private int _braceDepth;

    public void Feed(string chunk)
    {
        foreach (var ch in chunk)
        {
            if (ch == '{' && !_inJson)
            {
                _inJson = true;
                _braceDepth = 0;
            }

            if (_inJson)
            {
                _buffer.Append(ch);
                if (ch == '{') _braceDepth++;
                else if (ch == '}') _braceDepth--;

                if (_braceDepth == 0)
                {
                    TryParseBuffer();
                    _buffer.Clear();
                    _inJson = false;
                }
            }
        }
    }

    public ScriptDefinition? ParseComplete(string json)
    {
        json = ExtractJson(json);
        LastRawResponse = json;

        try
        {
            // Try multi-solution first
            var multi = JsonSerializer.Deserialize<MultiSolutionResponse>(json, _jsonOpts);
            if (multi?.Solutions.Count > 0)
            {
                foreach (var solution in multi.Solutions)
                    OnScriptComplete?.Invoke(solution);
                return multi.Solutions[0];
            }
        }
        catch { }

        try
        {
            var script = JsonSerializer.Deserialize<ScriptDefinition>(json, _jsonOpts);
            if (script?.Components.Count > 0)
            {
                OnScriptComplete?.Invoke(script);
                return script;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Parse a multi-solution response, returning ALL alternatives (unlike
    /// ParseComplete, which collapses to the first). Returns null if the
    /// response isn't a valid multi-solution payload.
    /// </summary>
    public MultiSolutionResponse? ParseMultiSolution(string json)
    {
        json = ExtractJson(json);
        LastRawResponse = json;

        try
        {
            var multi = JsonSerializer.Deserialize<MultiSolutionResponse>(json, _jsonOpts);
            // Only treat it as multi-solution if at least one solution actually
            // carries components — otherwise it's a single-script response that
            // happens to deserialize into an empty Solutions list.
            if (multi != null && multi.Solutions.Any(s => s.Components.Count > 0))
                return multi;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Parse the PLAN pass response into a <see cref="PlanDef"/>. Returns null if the
    /// model didn't return a usable plan (so the build falls back to single-shot).
    /// </summary>
    public PlanDef? ParsePlan(string json)
    {
        json = ExtractJson(json);
        try
        {
            var plan = JsonSerializer.Deserialize<PlanDef>(json, _jsonOpts);
            if (plan != null && plan.Steps.Count > 0)
                return plan;
        }
        catch { }
        return null;
    }

    private static string ExtractJson(string text)
    {
        var stripped = text.Trim();

        // Remove ```json and ``` wrappers
        if (stripped.Contains("```"))
        {
            var lines = stripped.Split('\n');
            var jsonLines = new List<string>();
            bool inBlock = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("```"))
                {
                    inBlock = !inBlock;
                    continue;
                }
                if (inBlock)
                    jsonLines.Add(line);
            }
            if (jsonLines.Count > 0)
                stripped = string.Join("\n", jsonLines);
        }

        // Find the outermost { ... }
        var firstBrace = stripped.IndexOf('{');
        var lastBrace = stripped.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            return stripped.Substring(firstBrace, lastBrace - firstBrace + 1);

        return stripped;
    }

    public void Reset()
    {
        _buffer.Clear();
        _inJson = false;
        _braceDepth = 0;
    }

    private void TryParseBuffer()
    {
        var json = _buffer.ToString();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("components", out _))
            {
                var script = JsonSerializer.Deserialize<ScriptDefinition>(json, _jsonOpts);
                if (script != null) OnScriptComplete?.Invoke(script);
                return;
            }

            if (root.TryGetProperty("solutions", out _))
            {
                var multi = JsonSerializer.Deserialize<MultiSolutionResponse>(json, _jsonOpts);
                if (multi != null)
                {
                    foreach (var solution in multi.Solutions)
                        OnScriptComplete?.Invoke(solution);
                }
                return;
            }
        }
        catch
        {
            // Not valid JSON yet, ignore
        }
    }
}
