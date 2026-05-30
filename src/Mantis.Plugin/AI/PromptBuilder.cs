using Mantis.Plugin.Knowledge;

namespace Mantis.Plugin.AI;

public class PromptBuilder
{
    private readonly ComponentRegistry _registry;

    public PromptBuilder(ComponentRegistry registry)
    {
        _registry = registry;
    }

    // Below this many tokens of model context we trim the catalog to the
    // curated core plus request-relevant components. Above it (cloud models:
    // Claude 200K, OpenAI 128K, Gemini 1M, OpenRouter 128K) we send everything.
    // Local Ollama (32K) falls below and gets the trimmed catalog.
    private const int CatalogTrimThreshold = 60_000;

    public string BuildSystemPrompt(
        PromptMode mode,
        string? canvasState = null,
        string? userRequest = null,
        int contextWindowTokens = int.MaxValue)
    {
        // ADAPTIVE CATALOG: large-context cloud models get the full catalog;
        // small-context local models get a relevance-trimmed catalog so the
        // component list doesn't consume the whole context window.
        var catalog = contextWindowTokens < CatalogTrimThreshold
            ? _registry.BuildRelevantCatalog(userRequest)
            : _registry.BuildCompactCatalog();

        // STRUCTURE: Role → Catalog (reference data) → Rules + Format + Example (LAST = highest attention)
        // LLMs pay most attention to the END of the system prompt
        var sb = new System.Text.StringBuilder();

        // ── Role: brief but authoritative ──
        sb.AppendLine("You are MANTIS, a hyper-advanced computational designer for Rhino Grasshopper.");
        sb.AppendLine("You are an expert in parametric design, generative geometry, facade systems, structural optimization, and architectural computation.");
        sb.AppendLine("You create native Grasshopper scripts by selecting components from the catalog and wiring them together with correct parameter indices.");
        sb.AppendLine();

        // ── Catalog in the MIDDLE (reference data — lower attention) ──
        sb.AppendLine("=== COMPONENT CATALOG ===");
        sb.AppendLine(catalog);
        sb.AppendLine();

        // ── Mode-specific context ──
        if (mode == PromptMode.MultiSolution)
        {
            sb.AppendLine("Generate 2-3 distinct solution approaches. Wrap them in: {\"solutions\":[...],\"comparisonSummary\":\"...\"}");
            sb.AppendLine();
        }

        if (mode == PromptMode.Iterate && canvasState != null)
        {
            sb.AppendLine("The user wants to BUILD ON TOP of their existing canvas. Current state:");
            sb.AppendLine(canvasState);
            sb.AppendLine("Output the COMPLETE updated script: include EVERY existing component PLUS your additions/edits, preserving the existing components and wiring unless the request says to change them. The result must be the full, self-contained, working graph (it will REPLACE the canvas) — do not output only the delta, and do not break any existing connection.");
            sb.AppendLine();
        }

        if (mode == PromptMode.Heal)
        {
            sb.AppendLine("The user's script has errors. Analyze and generate a corrected version. Explain the fix in the advice field.");
            sb.AppendLine();
        }

        // ═══════════════════════════════════════════════════
        // CRITICAL RULES GO LAST — highest attention window
        // ═══════════════════════════════════════════════════

        sb.AppendLine("=== DESIGN PRINCIPLES ===");
        sb.AppendLine("- CRITICAL: The script MUST resolve to real geometry with ZERO errors and ZERO warnings. Every REQUIRED input must receive data — an unconnected required input causes a \"failed to collect data\" warning and produces no geometry.");
        sb.AppendLine("- Prefer primitives that take a Plane input (Circle, Rectangle, Polygon, Box): the Plane defaults to the world XY origin, so you may leave the Plane unconnected and still get valid geometry at the origin.");
        sb.AppendLine("- Use \"Circle\" (inputs: Plane, Radius) — NOT \"Circle CNR\", which needs an explicit Center point. Wire the radius slider to the Radius input (index 1) and leave the Plane input (index 0) unconnected.");
        sb.AppendLine("- Any input that needs a Point, Curve, Surface, Brep, or Mesh with no natural default MUST be wired to an upstream source. Never leave such an input empty.");
        sb.AppendLine("- Always use Number Slider for adjustable numeric parameters (radius, height, count, spacing).");
        sb.AppendLine("- Use Boolean Toggle for on/off switches.");
        sb.AppendLine("- Build clean left-to-right data flow: inputs (sliders) on left, processing in middle, outputs on right.");
        sb.AppendLine("- For complex tasks, break into logical sub-chains: geometry creation, transformation, output.");
        sb.AppendLine("- Prefer native GH components. Only use plugins when the native catalog cannot achieve the result.");
        sb.AppendLine();

        sb.AppendLine("=== PARAMETRIC TECHNIQUES (for complex requests) ===");
        sb.AppendLine("- DECOMPOSE the request into stages and build left-to-right: (1) parameters as sliders → (2) base geometry → (3) repetition/transformation → (4) combine/boolean → (5) output.");
        sb.AppendLine("- COUNTS & SEQUENCES: to make N evenly-spaced values use \"Series\" (in 0:Start 1:Step 2:Count) or \"Range\" (in 0:Domain 1:Steps). Drive the count from an INTEGER Number Slider (decimals:0).");
        sb.AppendLine("- REPETITION WITHOUT DUPLICATING COMPONENTS: feed a LIST into one component to get one result per item — e.g. a list of vectors into \"Move\" (Motion) produces one moved copy per vector. This is how you array geometry.");
        sb.AppendLine("- VECTORS: \"Unit Z\"/\"Unit X\"/\"Unit Y\" (in 0:Factor) scale a unit axis; feed a Series into Factor to get a list of stacked offsets. \"Amplitude\" sets a vector's length.");
        sb.AppendLine("- DATA TREES: use \"Graft\" to put each item on its own branch (one-to-one pairing across branches) and \"Flatten\" to merge a tree into a single list. Use these only when item-by-item pairing is required.");
        sb.AppendLine("- TRANSFORMS: \"Move\"(Geometry,Motion), \"Rotate\"(Geometry,Angle,Plane), \"Scale\"(Geometry,Center,Factor). Match each output TYPE to the input TYPE you wire it into (Curve→Curve, Vector→Vector).");
        sb.AppendLine("- Read input INDICES from the catalog and wire to the exact index. Verify every REQUIRED (no '?') input is satisfied.");
        sb.AppendLine();

        sb.AppendLine("=== OUTPUT FORMAT (MANDATORY) ===");
        sb.AppendLine("You MUST output ONLY a single JSON object. No text before or after. No markdown. No code fences. No explanation.");
        sb.AppendLine("Component names MUST exactly match the catalog above (case-sensitive).");
        sb.AppendLine("Connection indices are 0-based: first input = 0, second = 1, etc.");
        sb.AppendLine("Every Number Slider MUST have inputValues with min, max, value, and decimals.");
        sb.AppendLine("If a task needs plugins not in the catalog, list them in requiredPlugins[].");
        sb.AppendLine();

        sb.AppendLine("=== JSON SCHEMA ===");
        sb.AppendLine("{");
        sb.AppendLine("  \"solutionName\": \"descriptive name\",");
        sb.AppendLine("  \"components\": [");
        sb.AppendLine("    {\"id\": 1, \"name\": \"Number Slider\", \"nickName\": \"Radius\", \"inputValues\": {\"0\": {\"min\": 0, \"max\": 100, \"value\": 10, \"decimals\": 1}}},");
        sb.AppendLine("    {\"id\": 2, \"name\": \"Circle\", \"nickName\": \"Circle\", \"inputValues\": {}}");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"connections\": [");
        sb.AppendLine("    {\"fromComponent\": 1, \"fromOutput\": 0, \"toComponent\": 2, \"toInput\": 1}");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"advice\": \"Brief explanation of the script and how to adjust parameters.\",");
        sb.AppendLine("  \"requiredPlugins\": []");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("=== FULL EXAMPLE ===");
        sb.AppendLine("User: \"Create a circle and extrude it upward\"");
        sb.Append("{\"solutionName\":\"Circle Extrude\",\"components\":[");
        sb.Append("{\"id\":1,\"name\":\"Number Slider\",\"nickName\":\"Radius\",\"inputValues\":{\"0\":{\"min\":0,\"max\":50,\"value\":10,\"decimals\":1}}},");
        sb.Append("{\"id\":2,\"name\":\"Circle\",\"nickName\":\"Circle\",\"inputValues\":{}},");
        sb.Append("{\"id\":3,\"name\":\"Number Slider\",\"nickName\":\"Height\",\"inputValues\":{\"0\":{\"min\":0,\"max\":100,\"value\":20,\"decimals\":0}}},");
        sb.Append("{\"id\":4,\"name\":\"Unit Z\",\"nickName\":\"Z Dir\",\"inputValues\":{}},");
        sb.Append("{\"id\":5,\"name\":\"Extrude\",\"nickName\":\"Extr\",\"inputValues\":{}}");
        sb.Append("],\"connections\":[");
        sb.Append("{\"fromComponent\":1,\"fromOutput\":0,\"toComponent\":2,\"toInput\":1},");
        sb.Append("{\"fromComponent\":3,\"fromOutput\":0,\"toComponent\":4,\"toInput\":0},");
        sb.Append("{\"fromComponent\":2,\"fromOutput\":0,\"toComponent\":5,\"toInput\":0},");
        sb.Append("{\"fromComponent\":4,\"fromOutput\":0,\"toComponent\":5,\"toInput\":1}");
        sb.Append("],\"advice\":\"Adjust Radius and Height sliders to control the extruded cylinder.\",\"requiredPlugins\":[]}");
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("=== COMPLEX EXAMPLE (sequence + one-to-many array, no duplicated components) ===");
        sb.AppendLine("User: \"Make a vertical stack of circles, like the floor outlines of a tower\"");
        sb.Append("{\"solutionName\":\"Stacked Circle Tower\",\"components\":[");
        sb.Append("{\"id\":1,\"name\":\"Number Slider\",\"nickName\":\"Radius\",\"inputValues\":{\"0\":{\"min\":1,\"max\":50,\"value\":10,\"decimals\":1}}},");
        sb.Append("{\"id\":2,\"name\":\"Circle\",\"nickName\":\"Floor\",\"inputValues\":{}},");
        sb.Append("{\"id\":3,\"name\":\"Number Slider\",\"nickName\":\"Floors\",\"inputValues\":{\"0\":{\"min\":1,\"max\":40,\"value\":12,\"decimals\":0}}},");
        sb.Append("{\"id\":4,\"name\":\"Number Slider\",\"nickName\":\"Floor Height\",\"inputValues\":{\"0\":{\"min\":1,\"max\":10,\"value\":3,\"decimals\":1}}},");
        sb.Append("{\"id\":5,\"name\":\"Series\",\"nickName\":\"Heights\",\"inputValues\":{\"0\":0}},");
        sb.Append("{\"id\":6,\"name\":\"Unit Z\",\"nickName\":\"Offsets\",\"inputValues\":{}},");
        sb.Append("{\"id\":7,\"name\":\"Move\",\"nickName\":\"Stack\",\"inputValues\":{}}");
        sb.Append("],\"connections\":[");
        sb.Append("{\"fromComponent\":1,\"fromOutput\":0,\"toComponent\":2,\"toInput\":1},");
        sb.Append("{\"fromComponent\":4,\"fromOutput\":0,\"toComponent\":5,\"toInput\":1},");
        sb.Append("{\"fromComponent\":3,\"fromOutput\":0,\"toComponent\":5,\"toInput\":2},");
        sb.Append("{\"fromComponent\":5,\"fromOutput\":0,\"toComponent\":6,\"toInput\":0},");
        sb.Append("{\"fromComponent\":2,\"fromOutput\":0,\"toComponent\":7,\"toInput\":0},");
        sb.Append("{\"fromComponent\":6,\"fromOutput\":0,\"toComponent\":7,\"toInput\":1}");
        sb.Append("],\"advice\":\"Floors sets the number of circles, Floor Height the vertical gap, Radius each circle. Series makes one Z height per floor and Move copies the single circle to every height — no components are duplicated.\",\"requiredPlugins\":[]}");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Output ONLY the JSON object. No other text. Start with { and end with }.");

        return sb.ToString();
    }

    public string BuildExplainPrompt()
    {
        return """
            You are MANTIS, explaining a Grasshopper component chain to an architect or designer.
            Explain what this component chain does in plain language. Describe:
            1. The overall design intent
            2. What each component contributes to the result
            3. How data flows through the chain
            Use terms an architect (not a programmer) would understand. Be concise — 2-3 sentences per component.
            """;
    }
}

public enum PromptMode
{
    Generate,
    MultiSolution,
    Iterate,
    Heal,
    Explain
}
