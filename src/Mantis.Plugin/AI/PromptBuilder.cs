using Mantis.Plugin.Knowledge;
using Mantis.Plugin.ScriptBuilder;

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
        int contextWindowTokens = int.MaxValue,
        bool hasImages = false,
        PlanDef? plan = null)
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

        // ── The plan MANTIS already reasoned out (Generate, after a Plan pass) ──
        if (plan != null && plan.Steps.Count > 0)
        {
            sb.AppendLine("=== YOUR PLAN (you already reasoned this out — build it faithfully) ===");
            sb.AppendLine("Intent: " + plan.Intent);
            if (plan.Assumptions.Count > 0)
                sb.AppendLine("Assumptions: " + string.Join("; ", plan.Assumptions));
            sb.AppendLine("Steps (each becomes EXACTLY ONE group, same order, same name):");
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                var s = plan.Steps[i];
                sb.AppendLine($"  {i + 1}. {s.Name} — {s.Approach}" +
                              (s.CandidateComponents.Count > 0 ? "  [" + string.Join(", ", s.CandidateComponents) + "]" : ""));
            }
            sb.AppendLine("Realize this plan as a complete, working graph. The \"groups\" array MUST contain exactly these steps, in this order, with these names — so the on-canvas stages match the plan the user is reading.");
            sb.AppendLine();
        }

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

        if (hasImages)
        {
            sb.AppendLine("=== REFERENCE IMAGE(S) ATTACHED ===");
            sb.AppendLine("The user attached one or more reference images. Treat them as the design TARGET and read them carefully:");
            sb.AppendLine("- Identify the primary geometry (curve / surface / solid), its proportions, and any symmetry.");
            sb.AppendLine("- COUNT repeated elements (louvers, panels, floors, fins, columns) and reproduce that count with a Number Slider + Series/Range driving a one-to-many array — never hand-duplicate components or eyeball the number.");
            sb.AppendLine("- Translate the spatial relationships you SEE (stacking, tapering, rotation, offset, gradient, twist) into the matching transform components.");
            sb.AppendLine("- The image defines INTENT; the catalog defines MEANS. Build the closest faithful parametric reconstruction using ONLY catalog components, exposing the key dimensions as sliders so the user can adjust them.");
            sb.AppendLine("- If the image shows something the catalog cannot express, approximate it with the nearest catalog approach and state the approximation in the advice field.");
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

        // Pre-install "suggest" layer — only for large-context (cloud) models, to keep small
        // local-model prompts lean. Never used for building, only for recommending installs.
        if (contextWindowTokens >= CatalogTrimThreshold)
        {
            var pluginSuggest = _registry.BuildPluginSuggestions();
            if (!string.IsNullOrWhiteSpace(pluginSuggest))
            {
                sb.AppendLine("=== POPULAR PLUGINS (suggest only — may NOT be installed) ===");
                sb.AppendLine("These add-ons are NOT in the catalog above. Build with native catalog components whenever possible. ONLY if the native catalog genuinely cannot do what the user asked, name the matching plugin in requiredPlugins[] and tell the user to install it — never wire a component that isn't in the catalog above.");
                sb.Append(pluginSuggest);
                sb.AppendLine();
            }
        }

        sb.AppendLine("=== WORKFLOW GROUPING & REASONING (REQUIRED) ===");
        sb.AppendLine("Organize the whole script into 2-5 logical STAGES and emit them in the \"groups\" array. Each group becomes a labelled box on the canvas AND its reasoning is shown to the user as your thought process — so this is how MANTIS explains its wiring.");
        sb.AppendLine("- Typical stages in left-to-right order: \"Parameters\" (the sliders/toggles) → \"Base Geometry\" → \"Transform / Array\" → \"Combine\" → \"Output\". Name stages for what THIS script does, not generically.");
        sb.AppendLine("- Every component id MUST appear in exactly ONE group. Do not leave any component ungrouped and never put an id in two groups.");
        sb.AppendLine("- reasoning = 1-2 sentences, in plain architect/designer language, saying what this stage produces AND how its output feeds the next stage. Describe intent and data flow, not component trivia. Example: \"These sliders expose the tower's radius, floor count and floor height so everything downstream stays adjustable.\"");
        sb.AppendLine("- color is optional: a hex like \"#5CDB7A\". If omitted, MANTIS auto-assigns a distinct tint per stage.");
        sb.AppendLine();

        // User's standing preferences (units, favourite plugins, house style). Placed
        // BEFORE the output-format mandate so the JSON contract still has the last word.
        AppendCustomInstructions(sb);

        sb.AppendLine("=== OUTPUT FORMAT (MANDATORY) ===");
        sb.AppendLine("You MUST output ONLY a single JSON object. No text before or after. No markdown. No code fences. No explanation.");
        sb.AppendLine("Component names MUST exactly match the catalog above (case-sensitive).");
        sb.AppendLine("Connection indices are 0-based: first input = 0, second = 1, etc.");
        sb.AppendLine("NEVER use a port index the component does not have: fromOutput must be LESS than the count of out: ports listed for that component, and toInput LESS than the count of in: ports. Wiring a port that isn't in the catalog line is invalid and will be rejected.");
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
        sb.AppendLine("  \"requiredPlugins\": [],");
        sb.AppendLine("  \"groups\": [");
        sb.AppendLine("    {\"name\": \"Parameters\", \"componentIds\": [1], \"reasoning\": \"One slider exposes the radius so the circle stays adjustable.\", \"color\": \"#5CDB7A\"},");
        sb.AppendLine("    {\"name\": \"Base Geometry\", \"componentIds\": [2], \"reasoning\": \"Builds the circle at the origin from that radius — the profile everything downstream uses.\"}");
        sb.AppendLine("  ]");
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
        sb.Append("],\"advice\":\"Adjust Radius and Height sliders to control the extruded cylinder.\",\"requiredPlugins\":[],");
        sb.Append("\"groups\":[");
        sb.Append("{\"name\":\"Parameters\",\"componentIds\":[1,3],\"reasoning\":\"Two sliders expose the circle radius and the extrusion height so the cylinder stays fully adjustable.\"},");
        sb.Append("{\"name\":\"Profile\",\"componentIds\":[2],\"reasoning\":\"Builds the circle at the origin from the radius — this is the cross-section that gets extruded.\"},");
        sb.Append("{\"name\":\"Extrude\",\"componentIds\":[4,5],\"reasoning\":\"Turns the height into an upward Z vector and sweeps the circle along it to produce the solid cylinder.\"}");
        sb.Append("]}");
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
        sb.Append("],\"advice\":\"Floors sets the number of circles, Floor Height the vertical gap, Radius each circle. Series makes one Z height per floor and Move copies the single circle to every height — no components are duplicated.\",\"requiredPlugins\":[],");
        sb.Append("\"groups\":[");
        sb.Append("{\"name\":\"Parameters\",\"componentIds\":[1,3,4],\"reasoning\":\"These sliders expose the tower's radius, floor count and floor height so the whole stack stays adjustable from three knobs.\"},");
        sb.Append("{\"name\":\"Base Floor\",\"componentIds\":[2],\"reasoning\":\"Draws a single circle at the origin — the one floor outline that gets copied upward, so no geometry is duplicated by hand.\"},");
        sb.Append("{\"name\":\"Vertical Array\",\"componentIds\":[5,6,7],\"reasoning\":\"Series turns floor count and height into a list of Z heights, Unit Z makes one offset vector per floor, and Move copies the base circle to every height to build the tower.\"}");
        sb.Append("]}");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Output ONLY the JSON object. No other text. Start with { and end with }.");

        return sb.ToString();
    }

    /// <summary>
    /// PLAN mode: before building anything, MANTIS understands the request and lays out the
    /// smallest correct parametric workflow as an ordered set of reasoned stages (a
    /// <see cref="PlanDef"/>). Cheap — it outputs only the plan, not a graph — and it gives
    /// the model awareness of which components exist so its steps are grounded. The plan is
    /// then shown to the user and fed back into the build pass so the graph follows it, with
    /// each step becoming one on-canvas group.
    /// </summary>
    public string BuildPlanPrompt(
        string? userRequest = null,
        string? canvasState = null,
        int contextWindowTokens = int.MaxValue)
    {
        // Names/categories are enough to PLAN; the full per-port detail is only needed at build time.
        var catalog = _registry.BuildRelevantCatalog(userRequest);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are MANTIS, an expert computational designer for Rhino Grasshopper, in PLANNING mode.");
        sb.AppendLine("Before building anything you THINK: understand exactly what the user wants, then lay out the SMALLEST correct parametric workflow as an ordered set of stages with reasoning. You are NOT building the graph yet — only the plan.");
        sb.AppendLine();
        sb.AppendLine("=== AVAILABLE COMPONENTS (plan around these real names) ===");
        sb.AppendLine(catalog);

        if (!string.IsNullOrWhiteSpace(canvasState))
        {
            sb.AppendLine();
            sb.AppendLine("=== CURRENT CANVAS (the user may want to build on this) ===");
            sb.AppendLine(canvasState);
        }

        sb.AppendLine();
        sb.AppendLine("=== HOW TO PLAN ===");
        sb.AppendLine("- INTENT: restate, in one line, the concrete geometry/outcome the user wants.");
        sb.AppendLine("- STEPS: 2-5 ordered stages with clean left-to-right data flow — typically Parameters (sliders) -> Base Geometry -> Transform/Array -> Combine -> Output. Name each stage for what THIS design needs, not generically.");
        sb.AppendLine("- For each step give: reasoning (WHY this stage exists + what it produces for the next), approach (HOW — the technique), and candidateComponents (exact catalog names you expect to use).");
        sb.AppendLine("- Favor the MINIMUM set of stages that fully and correctly produces the result. For repetition, plan sliders + Series/Range + a one-to-many array (a list into one component) — never hand-duplicate components.");
        sb.AppendLine("- assumptions: defaults/interpretations you are committing to (units, counts, orientation).");
        sb.AppendLine("- openQuestions: only genuinely ambiguous points — do NOT block; proceed with sensible defaults.");
        sb.AppendLine();

        AppendCustomInstructions(sb);

        sb.AppendLine("=== OUTPUT FORMAT (MANDATORY) ===");
        sb.AppendLine("Output ONLY a single JSON object. No text, no markdown, no code fences.");
        sb.AppendLine("{");
        sb.AppendLine("  \"intent\": \"one-line restatement of the goal\",");
        sb.AppendLine("  \"assumptions\": [\"world XY origin\", \"count driven by an integer slider\"],");
        sb.AppendLine("  \"steps\": [");
        sb.AppendLine("    {\"name\": \"Parameters\", \"reasoning\": \"why this stage exists and what it feeds next\", \"approach\": \"expose radius/count/height as sliders\", \"candidateComponents\": [\"Number Slider\"]},");
        sb.AppendLine("    {\"name\": \"Base Geometry\", \"reasoning\": \"...\", \"approach\": \"...\", \"candidateComponents\": [\"Circle\"]}");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"openQuestions\": []");
        sb.AppendLine("}");
        sb.AppendLine("Start with { and end with }.");
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

    /// <summary>
    /// Conversational "Ask" mode: MANTIS answers Grasshopper / parametric-design
    /// questions in PROSE without generating components. It sees a (trimmed) catalog so
    /// it can name real components, and — when provided — the current canvas state so it
    /// can explain or critique what's already built. The hard rule: never emit JSON or a
    /// component graph; this mode only talks. Parity with Raven's Ask vs Edit split.
    /// </summary>
    public string BuildAskPrompt(
        string? canvasState = null,
        string? userRequest = null,
        int contextWindowTokens = int.MaxValue)
    {
        var catalog = contextWindowTokens < CatalogTrimThreshold
            ? _registry.BuildRelevantCatalog(userRequest)
            : _registry.BuildCompactCatalog();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are MANTIS, an expert computational designer for Rhino Grasshopper, in ASK mode.");
        sb.AppendLine("You answer questions about Grasshopper, parametric and generative design, geometry, and how to APPROACH a problem — in clear, friendly prose for an architect or designer, not a programmer.");
        sb.AppendLine();
        sb.AppendLine("ASK MODE RULES:");
        sb.AppendLine("- ANSWER IN PROSE. Do NOT output JSON, code fences, or a component graph — you are explaining and advising, not building.");
        sb.AppendLine("- When you recommend components, use their EXACT catalog names and describe the wiring in words (e.g. \"feed a Series into Move's Motion input\"); a chain can be sketched as A → B → C.");
        sb.AppendLine("- Be concise and concrete. Prefer a short numbered approach over a wall of text.");
        sb.AppendLine("- If the user is actually asking you to BUILD something, give a brief answer, then tell them to switch OFF \"Ask\" and resend so MANTIS builds it live on the canvas.");
        sb.AppendLine("- If something needs a plugin or you're unsure, say so plainly.");
        sb.AppendLine();
        sb.AppendLine("=== COMPONENT CATALOG (so you name real components) ===");
        sb.AppendLine(catalog);

        if (!string.IsNullOrWhiteSpace(canvasState))
        {
            sb.AppendLine();
            sb.AppendLine("=== CURRENT CANVAS (what the user already has) ===");
            sb.AppendLine(canvasState);
            sb.AppendLine("When the question is about \"my script\", \"this\", or \"why is X red\", refer to this canvas.");
        }

        AppendCustomInstructions(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Append the user's persisted standing instructions (from settings.json) so EVERY
    /// prompt mode honors them — units, preferred plugins, naming, house style. Read
    /// fresh each build so edits take effect immediately; absent/blank = nothing added,
    /// keeping the prompt byte-identical to before for users who never set them.
    /// </summary>
    private static void AppendCustomInstructions(System.Text.StringBuilder sb)
    {
        var custom = MantisSettings.Get(MantisSettings.CustomInstructionsKey);
        if (string.IsNullOrWhiteSpace(custom)) return;

        sb.AppendLine();
        sb.AppendLine("=== USER'S STANDING INSTRUCTIONS (always honor these) ===");
        sb.AppendLine(custom!.Trim());
        sb.AppendLine();
    }
}

public enum PromptMode
{
    Generate,
    Plan,
    MultiSolution,
    Iterate,
    Heal,
    Explain
}
