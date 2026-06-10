using System.Reflection;
using System.Text.Json;
using Grasshopper;
using Grasshopper.Kernel;

namespace Mantis.Plugin.Knowledge;

public class ComponentRegistry
{
    private readonly Dictionary<string, ComponentInfo> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, ComponentInfo> _byGuid = new();
    private bool _initialized;

    public IReadOnlyCollection<ComponentInfo> All => _byName.Values;

    // The pre-install "suggest" layer (harvested from food4rhino / grasshopperdocs). MANTIS
    // can only BUILD with installed components (the runtime scan covers those); these let it
    // RECOMMEND the right add-on when the native catalog can't express the request.
    private readonly List<PluginEntry> _plugins = new();
    private readonly List<RhinoCommand> _commands = new();

    /// <summary>Popular downloadable Grasshopper plugins MANTIS knows about (metadata only).</summary>
    public IReadOnlyList<PluginEntry> PopularPlugins => _plugins;

    /// <summary>Rhino command reference (for Ask mode / future Rhino-side actions).</summary>
    public IReadOnlyList<RhinoCommand> RhinoCommands => _commands;

    public void Initialize()
    {
        if (_initialized) return;
        LoadEmbeddedCatalog();
        _plugins.AddRange(LoadEmbeddedJson<PluginEntry>("PluginRegistry.json"));
        _commands.AddRange(LoadEmbeddedJson<RhinoCommand>("RhinoCommands.json"));
        ScanInstalledComponents();
        _initialized = true;
    }

    /// <summary>Deserialize an embedded JSON array resource (case-insensitive). Never throws.</summary>
    private static List<T> LoadEmbeddedJson<T>(string endsWith)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(endsWith));
            if (name == null) return new();
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) return new();
            return JsonSerializer.Deserialize<List<T>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Compact "plugins you can SUGGEST" block for the prompt — name + capability + a few hero
    /// components. Used ONLY to recommend an install via requiredPlugins[]; never for building
    /// (the runtime scan covers installed components). Kept short to limit prompt bloat.
    /// </summary>
    public string BuildPluginSuggestions()
    {
        if (_plugins.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var p in _plugins)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            sb.Append("- ").Append(p.Name);
            if (!string.IsNullOrWhiteSpace(p.Capability)) sb.Append(": ").Append(p.Capability);
            if (p.HeroComponents.Count > 0)
                sb.Append(" (e.g. ").Append(string.Join(", ", p.HeroComponents.Take(4))).Append(')');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Compact Rhino command reference for Ask mode — "Category: Cmd1, Cmd2, …" per line, so
    /// MANTIS can name real Rhino commands when answering "how do I X in Rhino" questions.
    /// </summary>
    public string BuildCommandReference()
    {
        if (_commands.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var grp in _commands
                     .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                     .GroupBy(c => string.IsNullOrWhiteSpace(c.Category) ? "Other" : c.Category))
        {
            sb.Append(grp.Key).Append(": ");
            sb.AppendLine(string.Join(", ", grp.Select(c => c.Name)));
        }
        return sb.ToString();
    }

    public ComponentInfo? FindByName(string name)
    {
        if (_byName.TryGetValue(name, out var info))
            return info;

        // Strip parenthetical nicknames: "Circle CNR (Circle)" → "Circle CNR"
        var cleanName = name;
        var parenIdx = name.IndexOf('(');
        if (parenIdx > 0)
            cleanName = name[..parenIdx].Trim();
        if (cleanName != name && _byName.TryGetValue(cleanName, out info))
            return info;

        // Fuzzy match: try nickname
        foreach (var comp in _byName.Values)
        {
            if (string.Equals(comp.NickName, name, StringComparison.OrdinalIgnoreCase))
                return comp;
            if (string.Equals(comp.NickName, cleanName, StringComparison.OrdinalIgnoreCase))
                return comp;
        }

        // Fuzzy match: query contains catalog name (e.g. "Circle CNR (Circle)" contains "Circle CNR")
        foreach (var comp in _byName.Values)
        {
            if (name.Contains(comp.Name, StringComparison.OrdinalIgnoreCase))
                return comp;
        }

        // Fuzzy match: catalog name contains query
        foreach (var comp in _byName.Values)
        {
            if (comp.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                return comp;
            if (cleanName.Length > 2 && comp.Name.Contains(cleanName, StringComparison.OrdinalIgnoreCase))
                return comp;
        }

        return null;
    }

    public ComponentInfo? FindByGuid(Guid guid) =>
        _byGuid.TryGetValue(guid, out var info) ? info : null;

    public List<ComponentInfo> FindByCategory(string category) =>
        _byName.Values
            .Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Return up to <paramref name="max"/> catalog names most similar to a
    /// (possibly hallucinated) query name. Used to build a correction prompt when
    /// the model emits a component name that does not resolve. Ranks by exact
    /// match, substring containment (both directions), nickname, and shared word
    /// tokens.
    /// </summary>
    public List<string> SuggestNames(string query, int max = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<string>();
        query = query.Trim();
        var qTokens = query.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        return _byName.Values
            .Select(c => new { c.Name, score = ScoreName(c, query, qTokens) })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static int ScoreName(ComponentInfo c, string query, string[] qTokens)
    {
        int score = 0;
        if (c.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) score += 100;
        if (c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 20;
        if (query.Contains(c.Name, StringComparison.OrdinalIgnoreCase)) score += 15;
        if (!string.IsNullOrEmpty(c.NickName) &&
            c.NickName.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 10;
        foreach (var t in qTokens)
        {
            if (t.Length < 2) continue;
            if (c.Name.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 5;
        }
        return score;
    }

    public string BuildCatalogForPrompt(IEnumerable<string>? categories = null)
    {
        var components = categories != null
            ? _byName.Values.Where(c => categories.Contains(c.Category, StringComparer.OrdinalIgnoreCase))
            : _byName.Values;

        var lines = new List<string>();

        // Always include special components at the top (they aren't in the registry)
        lines.Add("\n## Special Components (always available)");
        lines.Add("- **Number Slider** — Adjustable number input with min/max/value");
        lines.Add("  Inputs: [] → Outputs: [V:Number]");
        lines.Add("- **Panel** — Text display panel");
        lines.Add("  Inputs: [text] → Outputs: [text]");
        lines.Add("- **Boolean Toggle** — True/False switch");
        lines.Add("  Inputs: [] → Outputs: [V:Boolean]");

        foreach (var group in components.GroupBy(c => c.Category).OrderBy(g => g.Key))
        {
            lines.Add($"\n## {group.Key}");
            foreach (var comp in group.OrderBy(c => c.Name))
            {
                var inputs = string.Join(", ", comp.Inputs.Select(i => $"{i.NickName}:{i.TypeName}"));
                var outputs = string.Join(", ", comp.Outputs.Select(o => $"{o.NickName}:{o.TypeName}"));
                lines.Add($"- **{comp.Name}** ({comp.NickName}) — {comp.Description}");
                lines.Add($"  Inputs: [{inputs}] → Outputs: [{outputs}]");
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Compact single-line-per-component catalog for LLM prompts.
    /// Saves ~60% tokens vs the full catalog. Emits EVERY known component —
    /// use this for large-context cloud models (Claude/OpenAI/Gemini/OpenRouter).
    /// </summary>
    public string BuildCompactCatalog()
    {
        var lines = new List<string>();
        AppendCatalogHeader(lines);
        AppendGroupedComponents(lines, _byName.Values);
        AppendPopularPluginsFooter(lines);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Relevance-trimmed catalog for small-context LOCAL models (Ollama), where
    /// the full ~1000+ component catalog would consume the entire context window
    /// and starve the response. Always keeps the curated core (the embedded
    /// catalog) so common workhorses are never lost, then adds the non-core
    /// components most relevant to <paramref name="request"/> (matched on name,
    /// nickname, category, and description), capped at <paramref name="maxExtra"/>.
    /// Falls back to core-only when the request is empty (e.g. error healing).
    /// </summary>
    public string BuildRelevantCatalog(string? request, int maxExtra = 120)
    {
        var selected = new HashSet<ComponentInfo>(_byName.Values.Where(c => c.IsCore));

        if (!string.IsNullOrWhiteSpace(request))
        {
            var qTokens = request
                .Split(new[] { ' ', ',', '.', '-', '_', '\n', '\r', '\t', '(', ')', '"', '\'' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 3)
                .ToArray();

            var extras = _byName.Values
                .Where(c => !c.IsCore)
                .Select(c => new { c, score = ScoreRelevance(c, request, qTokens) })
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.c.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxExtra)
                .Select(x => x.c);

            foreach (var c in extras)
                selected.Add(c);
        }

        var lines = new List<string>();
        AppendCatalogHeader(lines);
        lines.Add($"(Showing {selected.Count} components most relevant to your request — ask for a specific component by name if you need one not listed.)");
        AppendGroupedComponents(lines, selected);
        AppendPopularPluginsFooter(lines);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Score a non-core component's relevance to the request. Higher = more
    /// relevant. Matches request tokens against the component's name, nickname,
    /// category, and description so a request like "sweep a profile" surfaces
    /// Sweep components even though they're not in the curated core.
    /// </summary>
    private static int ScoreRelevance(ComponentInfo c, string request, string[] qTokens)
    {
        int score = 0;
        if (!string.IsNullOrEmpty(c.Name) && request.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
            score += 40;
        if (!string.IsNullOrEmpty(c.NickName) && c.NickName.Length >= 3 &&
            request.Contains(c.NickName, StringComparison.OrdinalIgnoreCase))
            score += 15;

        foreach (var t in qTokens)
        {
            if (c.Name.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 10;
            else if (!string.IsNullOrEmpty(c.NickName) &&
                     c.NickName.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 6;
            if (!string.IsNullOrEmpty(c.Category) &&
                c.Category.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 3;
            if (!string.IsNullOrEmpty(c.Description) &&
                c.Description.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 2;
        }
        return score;
    }

    private static void AppendCatalogHeader(List<string> lines)
    {
        // Format legend — teaches the model to read indices and required/optional.
        lines.Add("FORMAT: Name | in: <index>:<nick>(<type>) ... | out: <index>:<nick>(<type>) ...");
        lines.Add("Wire to inputs/outputs by their 0-based INDEX. '?' = OPTIONAL input (safe to leave unconnected); every input WITHOUT '?' is REQUIRED and must receive data (a wire or an inline value).");
        lines.Add("");

        // Special components first
        lines.Add("Number Slider | in: (none) | out: 0:N(Number) — adjustable number; set min/max/value/decimals");
        lines.Add("Panel | in: 0:T(Text)? | out: 0:T(Text) — display text");
        lines.Add("Boolean Toggle | in: (none) | out: 0:B(Boolean) — true/false switch");
    }

    private static void AppendGroupedComponents(List<string> lines, IEnumerable<ComponentInfo> components)
    {
        foreach (var group in components.GroupBy(c => c.Category).OrderBy(g => g.Key))
        {
            lines.Add($"--- {group.Key} ---");
            foreach (var comp in group.OrderBy(c => c.Name))
                lines.Add(FormatComponentLine(comp));
        }
    }

    private static string FormatComponentLine(ComponentInfo comp)
    {
        var ins = comp.Inputs.Count > 0
            ? string.Join(" ", comp.Inputs.Select((i, idx) => $"{idx}:{i.NickName}({i.TypeName}){(i.Optional ? "?" : "")}"))
            : "(none)";
        var outs = comp.Outputs.Count > 0
            ? string.Join(" ", comp.Outputs.Select((o, idx) => $"{idx}:{o.NickName}({o.TypeName})"))
            : "(none)";
        var plugin = comp.PluginName != "Grasshopper" && !string.IsNullOrEmpty(comp.PluginName)
            ? $" [{comp.PluginName}]" : "";
        return $"{comp.Name} | in: {ins} | out: {outs}{plugin}";
    }

    private static void AppendPopularPluginsFooter(List<string> lines)
    {
        // Add well-known plugin components that may not be installed
        lines.Add("--- Popular Plugins (may need install from food4rhino.com) ---");
        lines.Add("If the task needs these, list plugin name in requiredPlugins[]:");
        lines.Add("LunchBox: Diamond Panels, Hexagonal Panels, Random Points, etc.");
        lines.Add("Weaverbird: Mesh Thicken, Catmull-Clark, Loop Subdivision, etc.");
        lines.Add("Kangaroo 2: Bouncy Solver, Anchor, Spring, Load, etc.");
        lines.Add("Pufferfish: Tween Curves, Morph to Surface, etc.");
        lines.Add("Heteroptera: Boundary Surface, Mesh Pipe, etc.");
        lines.Add("Dendro: Volume, Mesh to Volume, etc.");
        lines.Add("Ladybug Tools: Sun Path, Radiation Analysis, etc.");
        lines.Add("Elk: OpenStreetMap, Topography, etc.");
        lines.Add("Human: Custom Preview, Gradient, Mesh Color, etc.");
    }

    private void LoadEmbeddedCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("BuiltInComponents.json"));

        if (resourceName == null) return;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return;

        var components = JsonSerializer.Deserialize<List<ComponentInfo>>(stream);
        if (components == null) return;

        foreach (var comp in components)
        {
            // The embedded catalog is the curated "core" set — always kept when we
            // trim the catalog for small-context local models.
            comp.IsCore = true;
            Register(comp);
        }
    }

    private void ScanInstalledComponents()
    {
        try
        {
            var server = Instances.ComponentServer;
            if (server == null) return;

            foreach (var proxy in server.ObjectProxies)
            {
                if (_byGuid.ContainsKey(proxy.Guid)) continue;

                try
                {
                    var obj = Instances.ComponentServer.EmitObject(proxy.Guid);
                    if (obj is not GH_Component comp) continue;

                    comp.CreateAttributes();

                    var info = new ComponentInfo
                    {
                        Guid = proxy.Guid,
                        Name = comp.Name,
                        NickName = comp.NickName,
                        Category = comp.Category,
                        Subcategory = comp.SubCategory,
                        Description = comp.Description,
                        PluginName = proxy.Location != null ? Path.GetFileNameWithoutExtension(proxy.Location) : "Unknown"
                    };

                    foreach (var input in comp.Params.Input)
                    {
                        info.Inputs.Add(new ParamInfo
                        {
                            Name = input.Name,
                            NickName = input.NickName,
                            Description = input.Description,
                            TypeName = input.TypeName,
                            Access = input.Access.ToString(),
                            Optional = input.Optional
                        });
                    }

                    foreach (var output in comp.Params.Output)
                    {
                        info.Outputs.Add(new ParamInfo
                        {
                            Name = output.Name,
                            NickName = output.NickName,
                            Description = output.Description,
                            TypeName = output.TypeName
                        });
                    }

                    Register(info);
                }
                catch
                {
                    // Skip components that fail to instantiate
                }
            }
        }
        catch
        {
            // ComponentServer not available (e.g. during testing)
        }
    }

    private void Register(ComponentInfo info)
    {
        _byName[info.Name] = info;
        if (info.Guid != Guid.Empty)
            _byGuid[info.Guid] = info;
    }
}
