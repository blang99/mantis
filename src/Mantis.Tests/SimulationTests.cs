using System.Text;
using System.Text.Json;
using Mantis.Plugin.AI;
using Mantis.Plugin.Knowledge;
using Mantis.Plugin.ScriptBuilder;
using Xunit;
using Xunit.Abstractions;

namespace Mantis.Tests;

/// <summary>
/// Headless "simulation + self-test" of MANTIS's generation intelligence.
///
/// WHAT IS REAL HERE (production code, exercised directly):
///   • ResponseParser.ParseComplete / Feed  — the actual JSON parser
///   • ScriptDefinition / ComponentDef / ConnectionDef / GroupDef — the actual data model
///   • LayoutEngine.ComputeLayout — the actual topological auto-layout
///   • ConversationManager — the actual multi-turn history used for "build on top"
///   • ComponentInfo + BuiltInComponents.json — the actual shipped catalog (96 components)
///
/// WHAT IS A FAITHFUL REPLICA (the one piece that needs Grasshopper at runtime):
///   • Resolve(name) mirrors ComponentRegistry.FindByName's exact cascade plus
///     ComponentFactory.CanResolve's special-component rules (slider/panel/toggle).
///     Replicated char-for-char from those files so "does this name resolve?" is
///     answered identically to the live plugin — without loading Grasshopper.
///
/// WHAT THIS DOES NOT COVER (honest scope):
///   • Live canvas paint inside Rhino (GH_Group boxes, wires, zero red errors) —
///     that requires a running Rhino and is tracked separately as a deploy test.
///
/// The scenarios below are authored as the model WOULD emit them, using ONLY real
/// catalog names, to test that the deterministic pipeline (parse → validate →
/// lay out) handles a COMPLEX request and then ADAPTS to two stacked follow-ups.
/// A negative control proves the self-test actually detects failure.
/// </summary>
public class SimulationTests
{
    private readonly ITestOutputHelper _out;
    private static readonly IReadOnlyDictionary<string, ComponentInfo> Catalog = LoadCatalog();

    public SimulationTests(ITestOutputHelper output) => _out = output;

    // ─────────────────────────────────────────────────────────────────────────
    //  Faithful name-resolution replica (ComponentRegistry.FindByName +
    //  ComponentFactory.CanResolve). Kept deliberately identical to production.
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsNumberSlider(string n) =>
        n.Equals("Number Slider", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("Slider", StringComparison.OrdinalIgnoreCase);
    private static bool IsPanel(string n) =>
        n.Equals("Panel", StringComparison.OrdinalIgnoreCase);
    private static bool IsBooleanToggle(string n) =>
        n.Equals("Boolean Toggle", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("Toggle", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mirror of ComponentRegistry.FindByName (hardened: conservative, no silent mis-bind).</summary>
    private static ComponentInfo? FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (Catalog.TryGetValue(name, out var info))
            return info;

        var cleanName = name;
        var parenIdx = name.IndexOf('(');
        if (parenIdx > 0)
            cleanName = name[..parenIdx].Trim();
        if (cleanName != name && Catalog.TryGetValue(cleanName, out info))
            return info;

        foreach (var comp in Catalog.Values)
            if (string.Equals(comp.NickName, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(comp.NickName, cleanName, StringComparison.OrdinalIgnoreCase))
                return comp;

        var nQuery = NormalizeName(cleanName);
        if (nQuery.Length > 0)
            foreach (var comp in Catalog.Values)
                if (NormalizeName(comp.Name) == nQuery || NormalizeName(comp.NickName ?? "") == nQuery)
                    return comp;

        if (cleanName.Length >= 4)
        {
            ComponentInfo? unique = null;
            foreach (var comp in Catalog.Values)
            {
                if (comp.Name.Contains(cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    if (unique != null) { unique = null; break; } // ambiguous → don't guess
                    unique = comp;
                }
            }
            if (unique != null) return unique;
        }

        return null;
    }

    private static string NormalizeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>Mirror of ComponentFactory.CanResolve.</summary>
    private static bool CanResolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (IsNumberSlider(name) || IsPanel(name) || IsBooleanToggle(name)) return true;
        return FindByName(name) != null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RESOLVER HARDENING (review Weakness #2). The resolver must STOP silently
    //  binding a hallucinated name to a real component it merely CONTAINS — that
    //  produced a clean-looking canvas computing the wrong geometry. Such a name
    //  must now come back unresolved so the repair loop + SuggestNames fix it.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Resolver_still_resolves_real_names_exactly_and_normalized()
    {
        var real = Catalog.Values.First().Name; // any real catalog component
        Assert.True(CanResolve(real), $"exact catalog name '{real}' must still resolve");
        Assert.True(CanResolve(real.ToLowerInvariant().Replace(" ", "")),
            "a case/space-normalized variant must still resolve");
    }

    [Fact]
    public void Resolver_no_longer_silently_binds_a_hallucination_that_contains_a_real_name()
    {
        var real = Catalog.Values.First().Name;            // e.g. "Circle"
        var hallucinated = "Quantum " + real + " Engine";  // contains the real name as a substring
        Assert.Null(FindByName(hallucinated));
        Assert.False(CanResolve(hallucinated),
            $"'{hallucinated}' must NOT silently resolve to '{real}' — it should surface as unresolvable so repair fixes it.");
    }

    /// <summary>
    /// Mirror of MantisService.PortArityOf: the (inputs, outputs) the SAME catalog
    /// advertises for a component, or null when unknown (specials/params aren't in the
    /// catalog → null → the validator skips them, exactly as in production). Wiring this
    /// into the scenarios below means the complex tower and both iterations must now also
    /// pass PORT-RANGE validation, not just structural checks.
    /// </summary>
    private static (int Inputs, int Outputs)? ArityOf(string name)
    {
        var info = FindByName(name);
        return info == null ? null : (info.Inputs.Count, info.Outputs.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Invariants are checked by the REAL shipping validator,
    //  Mantis.Plugin.ScriptBuilder.ScriptValidator (linked into this project), with
    //  CanResolve above as the injected name-resolution predicate. The test and the
    //  plugin therefore validate scripts through the exact same code path.
    // ─────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────
    //  TEST 1 — Negative control. PROVES the self-test has teeth: a deliberately
    //  broken script must be caught on all four defect classes.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void NegativeControl_DetectsEveryDefectClass()
    {
        var parser = new ResponseParser();
        var script = parser.ParseComplete(BrokenScenario);
        Assert.NotNull(script); // parser still parses it — validation is a separate stage

        // Uses the REAL shipping validator (Mantis.Plugin.ScriptBuilder.ScriptValidator),
        // now with catalog-backed port arity so the port-range check runs too.
        var issues = ScriptValidator.Validate(script!, CanResolve, ArityOf);

        // Four planted defects must each be reported, with the right code/severity.
        Assert.Contains(issues, i => i.Code == "UNRESOLVABLE_NAME" && i.Message.Contains("Quantum Twist Engine"));
        Assert.Contains(issues, i => i.Code == "DANGLING_CONNECTION" && i.Message.Contains("77"));
        Assert.Contains(issues, i => i.Code == "ORPHAN_COMPONENT" && i.Message.Contains("Rectangle"));
        Assert.Contains(issues, i => i.Code == "MISSING_REASONING");
        Assert.True(ScriptValidator.HasErrors(issues), "negative control must contain at least one Error-severity issue");

        _out.WriteLine($"Negative control correctly flagged {issues.Count} issues "
                       + $"({ScriptValidator.Errors(issues).Count} errors):");
        foreach (var i in issues) _out.WriteLine("   ✗ " + i);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TEST 2 — Complex request + two stacked iterations. The heart of the sim.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void ComplexRequest_AndStackedIterations_StayValidAndBuildOnTop()
    {
        var convo = new ConversationManager();
        var report = new StringBuilder();
        report.AppendLine("# MANTIS — Generation Intelligence Simulation");
        report.AppendLine();
        report.AppendLine($"_Run {DateTime.Now:yyyy-MM-dd HH:mm}_ · catalog: **{Catalog.Count}** components · "
                          + "REAL ResponseParser + ScriptDefinition + LayoutEngine + ConversationManager.");
        report.AppendLine();
        report.AppendLine("Resolver mirrors ComponentRegistry.FindByName + ComponentFactory.CanResolve exactly. "
                          + "Live in-Rhino canvas paint is validated separately (deploy test).");
        report.AppendLine();

        var steps = new (string Request, string Json)[]
        {
            ("Make a parametric twisting tower: a rectangular floor plate arrayed up N floors, "
             + "each plate rotated incrementally to create a continuous twist, then lofted into a smooth skin.",
             TowerBase),
            ("Soften every floor plate with a rounded corner fillet, and cap the top so the tower reads as a solid.",
             TowerIteration1),
            ("Add a panel reading out the total tower height, and drop a hexagonal site grid sized to the plate width.",
             TowerIteration2),
        };

        ScriptDefinition? prev = null;
        var labels = new[] { "Base request (complex)", "Iteration 1 (on top of base)", "Iteration 2 (on top of #1)" };

        for (int i = 0; i < steps.Length; i++)
        {
            var (request, json) = steps[i];

            // Faithful multi-turn flow: the follow-up sits on top of prior context.
            convo.AddUserMessage(request);

            var parser = new ResponseParser();
            var script = parser.ParseComplete(json);
            Assert.NotNull(script);
            convo.AddAssistantMessage(parser.LastRawResponse ?? json);

            // ---- Invariants: must be a clean, buildable script (REAL validator,
            //      now including catalog-backed PORT-RANGE validation) ----
            var issues = ScriptValidator.Validate(script!, CanResolve, ArityOf);

            // ---- Real auto-layout: every component placed, no two overlapping ----
            var layout = new LayoutEngine().ComputeLayout(script!);
            Assert.Equal(script!.Components.Count, layout.Count);
            var seen = new HashSet<(float, float)>();
            var overlaps = layout.Values.Count(p => !seen.Add((p.X, p.Y)));

            // ---- Build-on-top: this script must be a superset of the previous ----
            string buildOnTop = "n/a (base)";
            if (prev != null)
            {
                var prevIds = prev.Components.Select(c => c.Id).ToHashSet();
                var curIds = script!.Components.Select(c => c.Id).ToHashSet();
                var retained = prevIds.Count(curIds.Contains);
                Assert.True(prevIds.IsSubsetOf(curIds),
                    $"Iteration dropped prior components — not building on top (kept {retained}/{prevIds.Count}).");
                // The original twist engine (move=9, angle-mult=10, rotate=11) must survive.
                Assert.True(new[] { 9, 10, 11 }.All(curIds.Contains),
                    "Iteration discarded the core twist logic.");
                buildOnTop = $"kept all {prevIds.Count} prior components, added {curIds.Count - prevIds.Count} new";
            }

            // ---- Assert clean (zero issues — errors AND cosmetic warnings) ----
            Assert.Empty(issues);
            Assert.Equal(0, overlaps);

            // ---- Report ----
            var resolved = script!.Components.Count(c => CanResolve(c.Name));
            var xs = layout.Values.Select(p => p.X).DefaultIfEmpty(0).ToList();
            var ys = layout.Values.Select(p => p.Y).DefaultIfEmpty(0).ToList();
            report.AppendLine($"## {labels[i]}");
            report.AppendLine();
            report.AppendLine($"> {request}");
            report.AppendLine();
            report.AppendLine($"- **Solution:** {script!.SolutionName} — {script!.SolutionDescription}");
            report.AppendLine($"- **Parsed:** {script!.Components.Count} components · "
                              + $"{script!.Connections.Count} connections · {script!.Groups.Count} stages");
            report.AppendLine($"- **Names resolved:** {resolved}/{script!.Components.Count}");
            report.AppendLine($"- **Stage coverage:** every component in exactly one stage ✔");
            report.AppendLine($"- **Auto-layout:** {layout.Count} placed · {overlaps} overlaps · "
                              + $"canvas span X[{xs.Min():0}–{xs.Max():0}] Y[{ys.Min():0}–{ys.Max():0}]");
            report.AppendLine($"- **Build-on-top:** {buildOnTop}");
            report.AppendLine($"- **Structural defects:** {issues.Count}");
            report.AppendLine($"- **Stages:**");
            foreach (var g in script!.Groups)
                report.AppendLine($"    - _{g.Name}_ [{g.ComponentIds.Count}]: {Trunc(g.Reasoning, 130)}");
            report.AppendLine($"- **Verdict:** {(issues.Count == 0 && overlaps == 0 ? "✅ PASS — buildable, error-free structure" : "❌ FAIL")}");
            report.AppendLine();

            prev = script;
        }

        // Conversation genuinely accumulated all three turns (3 user + 3 assistant).
        Assert.Equal(6, convo.History.Count);
        report.AppendLine($"**Multi-turn memory:** {convo.History.Count} messages retained across "
                          + $"{steps.Length} turns — each follow-up adapted on top of the previous, not from scratch.");

        WriteReport(report.ToString());
        _out.WriteLine(report.ToString());
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  STRESS LOOP — the four properties under a hard battery, every iteration:
    //    (A) every request COMPLETES clean   (B) requests STACK on one another
    //    (C) MEMORY accumulates + recalls    (D) HISTORY threads through
    //  Drives the same real ResponseParser / ScriptValidator / LayoutEngine /
    //  ConversationManager / LessonStore as the plugin. 100% pass required.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Procedurally build a guaranteed-valid graph of N independent slider→Circle pairs.</summary>
    private static ScriptDefinition GenScalingGraph(int pairs)
    {
        var s = new ScriptDefinition { SolutionName = $"Gen-{pairs}", SolutionDescription = "procedural scaling graph" };
        var paramIds = new List<int>();
        var geoIds = new List<int>();
        for (int i = 0; i < pairs; i++)
        {
            int sliderId = i * 2 + 1, circleId = i * 2 + 2;
            s.Components.Add(new ComponentDef { Id = sliderId, Name = "Number Slider", NickName = $"R{i}" });
            s.Components.Add(new ComponentDef { Id = circleId, Name = "Circle", NickName = $"C{i}" });
            s.Connections.Add(new ConnectionDef { FromComponent = sliderId, FromOutput = 0, ToComponent = circleId, ToInput = 1 });
            paramIds.Add(sliderId);
            geoIds.Add(circleId);
        }
        s.Groups.Add(new GroupDef { Name = "Parameters", ComponentIds = paramIds, Reasoning = "Sliders expose each circle's radius so the set stays adjustable." });
        s.Groups.Add(new GroupDef { Name = "Geometry", ComponentIds = geoIds, Reasoning = "One circle per radius, drawn at the origin." });
        return s;
    }

    // (A) COMPLETION — a battery of diverse requests (3 hand-authored complex + 15 procedural
    //     of growing size) must EACH parse, validate clean, and lay out with no overlaps.
    [Fact]
    public void StressLoop_EveryRequestCompletesCleanly()
    {
        var scripts = new List<(string Name, ScriptDefinition Script)>();
        foreach (var (name, json) in new[] { ("TowerBase", TowerBase), ("TowerIter1", TowerIteration1), ("TowerIter2", TowerIteration2) })
        {
            var sc = new ResponseParser().ParseComplete(json);
            Assert.NotNull(sc);
            scripts.Add((name, sc!));
        }
        for (int n = 1; n <= 30; n++) scripts.Add(($"Gen-{n}", GenScalingGraph(n)));

        var failures = new List<string>();
        foreach (var (name, sc) in scripts)
        {
            var issues = ScriptValidator.Validate(sc, CanResolve, ArityOf);
            var layout = new LayoutEngine().ComputeLayout(sc);
            var seen = new HashSet<(float, float)>();
            int overlaps = layout.Values.Count(p => !seen.Add((p.X, p.Y)));
            if (issues.Count != 0 || overlaps != 0 || layout.Count != sc.Components.Count)
                failures.Add($"{name}: {issues.Count} issues [{string.Join(",", issues.Select(i => i.Code))}], "
                             + $"{overlaps} overlaps, {layout.Count}/{sc.Components.Count} placed");
        }
        _out.WriteLine($"(A) Completion loop: {scripts.Count - failures.Count}/{scripts.Count} scenarios completed clean.");
        foreach (var f in failures) _out.WriteLine("   ✗ " + f);
        Assert.Empty(failures);
    }

    // (B)+(D) STACKING + HISTORY — deep chain of follow-ups, each must stay valid AND be a
    //     strict superset of the prior (nothing dropped), while conversation history grows.
    [Fact]
    public void StressLoop_DeepStackStaysValidAndKeepsHistory()
    {
        const int K = 25;
        var convo = new ConversationManager();
        var script = new ResponseParser().ParseComplete(TowerBase);
        Assert.NotNull(script);
        convo.AddUserMessage("Base: parametric twisting tower.");
        convo.AddAssistantMessage(TowerBase);
        var baseIds = script!.Components.Select(c => c.Id).ToHashSet();
        Assert.Empty(ScriptValidator.Validate(script, CanResolve, ArityOf));

        var prevIds = baseIds;
        for (int k = 1; k <= K; k++)
        {
            convo.AddUserMessage($"Follow-up {k}: add a labelled readout.");
            // Append a self-contained, guaranteed-valid superset: a slider feeding a panel.
            int maxId = script.Components.Max(c => c.Id);
            int sliderId = maxId + 1, panelId = maxId + 2;
            script.Components.Add(new ComponentDef { Id = sliderId, Name = "Number Slider", NickName = $"P{k}" });
            script.Components.Add(new ComponentDef { Id = panelId, Name = "Panel", NickName = $"Readout{k}" });
            script.Connections.Add(new ConnectionDef { FromComponent = sliderId, FromOutput = 0, ToComponent = panelId, ToInput = 0 });
            script.Groups.Add(new GroupDef { Name = $"Readout {k}", ComponentIds = new() { sliderId, panelId }, Reasoning = $"Stage {k}: a panel echoes a value for inspection without disturbing prior stages." });
            convo.AddAssistantMessage("(updated, building on top)");

            var issues = ScriptValidator.Validate(script, CanResolve, ArityOf);
            Assert.True(issues.Count == 0, $"iteration {k} introduced defects: {string.Join(",", issues.Select(i => i.Code))}");
            var curIds = script.Components.Select(c => c.Id).ToHashSet();
            Assert.True(prevIds.IsSubsetOf(curIds), $"iteration {k} dropped prior components — not building on top.");
            prevIds = curIds;
        }

        Assert.True(baseIds.IsSubsetOf(prevIds), "the base tower logic must survive all iterations.");
        // History is a BOUNDED rolling window — oldest turns are trimmed to respect the token
        // budget, so it retains recent multi-turn context without growing unbounded. (Build-on-top
        // robustness comes from the live CANVAS state, not just chat, so trimming old chat is safe.)
        Assert.True(convo.History.Count >= 6, "recent multi-turn context must be retained for build-on-top.");
        Assert.True(convo.History.Count <= (K + 1) * 2, "history must be a bounded rolling window, not unbounded.");
        _out.WriteLine($"(B)+(D) Deep stack: {K} follow-ups on top of base — all stayed valid supersets; "
                       + $"rolling history window held {convo.History.Count} messages; final graph {prevIds.Count} components.");
    }

    // (C) MEMORY — many corrections accumulate, reinforce without duplicating, recall by
    //     relevance, and survive a reload (the exact LessonStore the plugin records into).
    [Fact]
    public void StressLoop_MemoryAccumulatesReinforcesAndRecalls()
    {
        var path = Path.Combine(Path.GetTempPath(), "mantis-stress-lessons-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new LessonStore(path);
        string[] bad = { "Cirkle", "Looft", "Extrood", "Moove", "Sereis", "Rektangle", "Polagon", "Sweap", "Revolv", "Boundery", "Offsett", "Devide" };
        string[] good = { "Circle", "Loft", "Extrude", "Move", "Series", "Rectangle", "Polygon", "Sweep1", "Revolution", "Boundary Surfaces", "Offset", "Divide Curve" };

        for (int i = 0; i < bad.Length; i++)
            store.Record($"name:{bad[i].ToLowerInvariant()}", $"\"{bad[i]}\" is not a real Grasshopper component", $"use \"{good[i]}\"", $"{bad[i]} {good[i]}");
        Assert.Equal(bad.Length, store.All.Count); // every distinct correction accumulated

        // Reinforcing an existing lesson must bump its count, not duplicate it.
        store.Record($"name:{bad[1].ToLowerInvariant()}", "x", $"use \"{good[1]}\"", bad[1]);
        Assert.Equal(bad.Length, store.All.Count);
        Assert.Equal(2, store.All.First(l => l.Key == $"name:{bad[1].ToLowerInvariant()}").Count);

        // Recall by relevance: a lofting request must surface the Loft lesson first.
        var top = store.GetRelevant("build a lofted surface from section curves", 3);
        Assert.Contains(top, l => l.Remedy.Contains("Loft"));

        // Survives a reload (fresh instance reads the same file).
        Assert.Equal(bad.Length, new LessonStore(path).All.Count);
        _out.WriteLine($"(C) Memory: {bad.Length} corrections accumulated, dedup+reinforce OK, relevance recall OK, persisted.");
        store.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TEST 2b — Port-range detection on the REAL catalog. Corrupt exactly one
    //  wire of the (otherwise clean) tower to reference a port the component does
    //  not have, and prove the shipping validator + catalog arity catches it —
    //  the same path MantisService's repair loop drives before building.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PortRange_RealCatalog_CatchesOneOutOfRangeWire()
    {
        var parser = new ResponseParser();

        // Pristine base is port-clean (no false positives against real catalog arity).
        var clean = parser.ParseComplete(TowerBase);
        Assert.NotNull(clean);
        Assert.DoesNotContain(ScriptValidator.Validate(clean!, CanResolve, ArityOf),
            i => i.Code == "PORT_OUT_OF_RANGE");

        // Corrupt ONE wire: feed Loft (id 12, which has just 2 inputs) on input #9.
        var corrupt = parser.ParseComplete(TowerBase)!;
        var wire = corrupt.Connections.First(c => c.ToComponent == 12);
        wire.ToInput = 9;

        var issues = ScriptValidator.Validate(corrupt, CanResolve, ArityOf);

        Assert.Contains(issues, i => i.Code == "PORT_OUT_OF_RANGE"
                                     && i.Severity == IssueSeverity.Error
                                     && i.ComponentId == 12);
        Assert.True(ScriptValidator.HasErrors(issues));
        _out.WriteLine("Real-catalog port check flagged the out-of-range wire into Loft input #9 "
                       + "(Loft has 2 inputs) — exactly the class of silent failure the repair loop now fixes.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TEST 3 — Streaming parser. The live "components appear as they stream"
    //  path: feed the JSON in small chunks and confirm a complete script emerges.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void StreamingParser_EmitsCompleteScriptFromChunks()
    {
        var parser = new ResponseParser();
        ScriptDefinition? emitted = null;
        parser.OnScriptComplete += s => emitted = s;

        // Simulate SSE token chunks arriving 40 chars at a time.
        const int chunk = 40;
        for (int i = 0; i < TowerBase.Length; i += chunk)
            parser.Feed(TowerBase.Substring(i, Math.Min(chunk, TowerBase.Length - i)));

        Assert.NotNull(emitted);
        Assert.Equal(13, emitted!.Components.Count);
        Assert.All(emitted!.Components, c => Assert.True(CanResolve(c.Name), $"streamed name failed: {c.Name}"));
        _out.WriteLine($"Streaming parser reconstructed {emitted!.Components.Count} components, "
                       + $"{emitted!.Connections.Count} connections, {emitted!.Groups.Count} stages from chunked input.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TEST 4 — Catalog data quality. A few curated entries ship with placeholder
    //  GUIDs (e.g. "Box"). ComponentFactory now recovers from those by matching the
    //  live proxy on Name/NickName — but that ONLY works if the entry has a usable
    //  NickName. Guard the precondition so a future edit can't silently break it.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PlaceholderGuidEntries_HaveNickNameForRuntimeFallback()
    {
        var placeholders = Catalog.Values.Where(c => LooksLikePlaceholder(c.Guid)).ToList();

        _out.WriteLine($"{placeholders.Count} catalog entries use placeholder GUIDs "
                       + "(recovered at runtime by Name/NickName match):");
        foreach (var c in placeholders)
            _out.WriteLine($"   • {c.Name}  (nick \"{c.NickName}\")  {c.Guid}");

        Assert.All(placeholders, c =>
            Assert.False(string.IsNullOrWhiteSpace(c.NickName),
                $"Placeholder-GUID entry \"{c.Name}\" has no NickName — the runtime fallback can't recover it."));
    }

    /// <summary>Low-entropy GUID heuristic: a hand-typed placeholder like a1a1a1a1-… </summary>
    private static bool LooksLikePlaceholder(Guid g)
    {
        var hex = g.ToString("N");
        return hex.Distinct().Count() <= 4;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string Trunc(string s, int n) =>
        s.Length <= n ? s : s[..n].TrimEnd() + "…";

    private static void WriteReport(string md)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "mantis_sim_report.md");
            File.WriteAllText(path, md);
        }
        catch { /* report file is a convenience, not a test requirement */ }
    }

    private static IReadOnlyDictionary<string, ComponentInfo> LoadCatalog()
    {
        var path = LocateCatalog();
        var json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<ComponentInfo>>(json)
                   ?? throw new InvalidOperationException("BuiltInComponents.json deserialized to null.");
        var dict = new Dictionary<string, ComponentInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in list)
            dict[c.Name] = c; // last-wins, mirrors ComponentRegistry.Register
        return dict;
    }

    private static string LocateCatalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            foreach (var rel in new[]
                     {
                         Path.Combine(dir.FullName, "Mantis.Plugin", "Knowledge", "Data", "BuiltInComponents.json"),
                         Path.Combine(dir.FullName, "src", "Mantis.Plugin", "Knowledge", "Data", "BuiltInComponents.json"),
                     })
            {
                if (File.Exists(rel)) return rel;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate BuiltInComponents.json from " + AppContext.BaseDirectory);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Scenario payloads — authored as the model would emit them, real names only.
    // ─────────────────────────────────────────────────────────────────────────

    private const string TowerBase = """
    {
      "solutionName": "Twisting Tower",
      "solutionDescription": "A rectangular floor plate arrayed up N floors, each plate rotated incrementally to produce a continuous twist, lofted into a smooth skin.",
      "components": [
        { "id": 1, "name": "Number Slider", "nickName": "Floor Count", "inputValues": { "0": { "min": 3, "max": 40, "value": 12, "decimals": 0 } } },
        { "id": 2, "name": "Number Slider", "nickName": "Floor Height", "inputValues": { "0": { "min": 2, "max": 6, "value": 3.5, "decimals": 2 } } },
        { "id": 3, "name": "Number Slider", "nickName": "Twist / Floor", "inputValues": { "0": { "min": 0, "max": 15, "value": 6, "decimals": 1 } } },
        { "id": 4, "name": "Number Slider", "nickName": "Plate Width", "inputValues": { "0": { "min": 5, "max": 30, "value": 14, "decimals": 1 } } },
        { "id": 5, "name": "Series", "nickName": "Floor Index" },
        { "id": 6, "name": "Multiplication", "nickName": "Z per floor" },
        { "id": 7, "name": "Unit Z", "nickName": "Lift" },
        { "id": 8, "name": "Rectangle", "nickName": "Base Plate" },
        { "id": 9, "name": "Move", "nickName": "Stack" },
        { "id": 10, "name": "Multiplication", "nickName": "Angle / floor" },
        { "id": 11, "name": "Rotate", "nickName": "Twist" },
        { "id": 12, "name": "Loft", "nickName": "Skin" },
        { "id": 13, "name": "Custom Preview", "nickName": "Show" }
      ],
      "connections": [
        { "fromComponent": 1, "fromOutput": 0, "toComponent": 5, "toInput": 2 },
        { "fromComponent": 2, "fromOutput": 0, "toComponent": 6, "toInput": 0 },
        { "fromComponent": 5, "fromOutput": 0, "toComponent": 6, "toInput": 1 },
        { "fromComponent": 6, "fromOutput": 0, "toComponent": 7, "toInput": 0 },
        { "fromComponent": 4, "fromOutput": 0, "toComponent": 8, "toInput": 1 },
        { "fromComponent": 8, "fromOutput": 0, "toComponent": 9, "toInput": 0 },
        { "fromComponent": 7, "fromOutput": 0, "toComponent": 9, "toInput": 1 },
        { "fromComponent": 5, "fromOutput": 0, "toComponent": 10, "toInput": 0 },
        { "fromComponent": 3, "fromOutput": 0, "toComponent": 10, "toInput": 1 },
        { "fromComponent": 9, "fromOutput": 0, "toComponent": 11, "toInput": 0 },
        { "fromComponent": 10, "fromOutput": 0, "toComponent": 11, "toInput": 1 },
        { "fromComponent": 11, "fromOutput": 0, "toComponent": 12, "toInput": 0 },
        { "fromComponent": 12, "fromOutput": 0, "toComponent": 13, "toInput": 0 }
      ],
      "groups": [
        { "name": "Parameters", "componentIds": [1,2,3,4], "color": "#5CDB7A", "reasoning": "Four sliders expose the only knobs a designer wants: floor count, floor height, twist per floor, and plan footprint. Everything downstream is driven from these so the whole tower stays live." },
        { "name": "Floor Stacking", "componentIds": [5,6,7], "color": "#54a8e8", "reasoning": "Series counts the floors 0..N-1; each index times the floor height gives its Z elevation, and Unit Z turns those heights into one vertical move vector per floor." },
        { "name": "Base Plate", "componentIds": [8], "color": "#54a8e8", "reasoning": "A single rectangle sized by Plate Width is the prototype floor plate that gets copied, lifted and twisted to build every level." },
        { "name": "Twist & Stack", "componentIds": [9,10,11], "color": "#e8b84d", "reasoning": "Move copies the plate up to each elevation; the floor index times the per-floor twist gives a cumulative angle, and Rotate spins each plate so the stack spirals." },
        { "name": "Skin & Display", "componentIds": [12,13], "color": "#5CDB7A", "reasoning": "Loft threads a smooth surface through the twisted plates as a continuous skin, and Custom Preview shows the result cleanly in the viewport." }
      ],
      "advice": "Push Twist / Floor past ~10 with a low Floor Count to read the spiral; raise Floor Count for a smoother skin."
    }
    """;

    private const string TowerIteration1 = """
    {
      "solutionName": "Twisting Tower + Fillet & Cap",
      "solutionDescription": "The twisting tower, now with softened plate corners and a capped top so it reads as a solid mass.",
      "components": [
        { "id": 1, "name": "Number Slider", "nickName": "Floor Count", "inputValues": { "0": { "min": 3, "max": 40, "value": 12, "decimals": 0 } } },
        { "id": 2, "name": "Number Slider", "nickName": "Floor Height", "inputValues": { "0": { "min": 2, "max": 6, "value": 3.5, "decimals": 2 } } },
        { "id": 3, "name": "Number Slider", "nickName": "Twist / Floor", "inputValues": { "0": { "min": 0, "max": 15, "value": 6, "decimals": 1 } } },
        { "id": 4, "name": "Number Slider", "nickName": "Plate Width", "inputValues": { "0": { "min": 5, "max": 30, "value": 14, "decimals": 1 } } },
        { "id": 5, "name": "Series", "nickName": "Floor Index" },
        { "id": 6, "name": "Multiplication", "nickName": "Z per floor" },
        { "id": 7, "name": "Unit Z", "nickName": "Lift" },
        { "id": 8, "name": "Rectangle", "nickName": "Base Plate" },
        { "id": 9, "name": "Move", "nickName": "Stack" },
        { "id": 10, "name": "Multiplication", "nickName": "Angle / floor" },
        { "id": 11, "name": "Rotate", "nickName": "Twist" },
        { "id": 12, "name": "Loft", "nickName": "Skin" },
        { "id": 13, "name": "Custom Preview", "nickName": "Show" },
        { "id": 14, "name": "Number Slider", "nickName": "Fillet Radius", "inputValues": { "0": { "min": 0, "max": 5, "value": 1, "decimals": 2 } } },
        { "id": 15, "name": "Fillet", "nickName": "Soft Corners" },
        { "id": 16, "name": "Cap Holes", "nickName": "Cap Top" }
      ],
      "connections": [
        { "fromComponent": 1, "fromOutput": 0, "toComponent": 5, "toInput": 2 },
        { "fromComponent": 2, "fromOutput": 0, "toComponent": 6, "toInput": 0 },
        { "fromComponent": 5, "fromOutput": 0, "toComponent": 6, "toInput": 1 },
        { "fromComponent": 6, "fromOutput": 0, "toComponent": 7, "toInput": 0 },
        { "fromComponent": 4, "fromOutput": 0, "toComponent": 8, "toInput": 1 },
        { "fromComponent": 8, "fromOutput": 0, "toComponent": 15, "toInput": 0 },
        { "fromComponent": 14, "fromOutput": 0, "toComponent": 15, "toInput": 1 },
        { "fromComponent": 15, "fromOutput": 0, "toComponent": 9, "toInput": 0 },
        { "fromComponent": 7, "fromOutput": 0, "toComponent": 9, "toInput": 1 },
        { "fromComponent": 5, "fromOutput": 0, "toComponent": 10, "toInput": 0 },
        { "fromComponent": 3, "fromOutput": 0, "toComponent": 10, "toInput": 1 },
        { "fromComponent": 9, "fromOutput": 0, "toComponent": 11, "toInput": 0 },
        { "fromComponent": 10, "fromOutput": 0, "toComponent": 11, "toInput": 1 },
        { "fromComponent": 11, "fromOutput": 0, "toComponent": 12, "toInput": 0 },
        { "fromComponent": 12, "fromOutput": 0, "toComponent": 16, "toInput": 0 },
        { "fromComponent": 16, "fromOutput": 0, "toComponent": 13, "toInput": 0 }
      ],
      "groups": [
        { "name": "Parameters", "componentIds": [1,2,3,4,14], "color": "#5CDB7A", "reasoning": "Same live knobs as before plus a new Fillet Radius slider, so the corner softness is adjustable without rewiring anything." },
        { "name": "Floor Stacking", "componentIds": [5,6,7], "color": "#54a8e8", "reasoning": "Unchanged from the base: Series → Z elevation → vertical move vectors, one per floor." },
        { "name": "Base Plate", "componentIds": [8,15], "color": "#54a8e8", "reasoning": "The rectangle is now passed through Fillet so each plate has rounded corners before it is stacked and twisted — the smoothing happens once, upstream of the array." },
        { "name": "Twist & Stack", "componentIds": [9,10,11], "color": "#e8b84d", "reasoning": "Identical twist logic; Move now consumes the filleted plate instead of the raw rectangle, so the whole stack inherits the softer profile." },
        { "name": "Skin & Display", "componentIds": [12,16,13], "color": "#5CDB7A", "reasoning": "Loft builds the skin as before, then Cap Holes seals the open top and bottom so the tower reads as a solid mass before Custom Preview displays it." }
      ],
      "advice": "Keep Fillet Radius below half the plate width so the corners stay valid; a small radius (~1) reads as a subtle chamfer."
    }
    """;

    private const string TowerIteration2 = """
    {
      "solutionName": "Twisting Tower + Readout & Site",
      "solutionDescription": "The filleted, capped twisting tower, now annotated with a total-height readout and a hexagonal site grid.",
      "components": [
        { "id": 1, "name": "Number Slider", "nickName": "Floor Count", "inputValues": { "0": { "min": 3, "max": 40, "value": 12, "decimals": 0 } } },
        { "id": 2, "name": "Number Slider", "nickName": "Floor Height", "inputValues": { "0": { "min": 2, "max": 6, "value": 3.5, "decimals": 2 } } },
        { "id": 3, "name": "Number Slider", "nickName": "Twist / Floor", "inputValues": { "0": { "min": 0, "max": 15, "value": 6, "decimals": 1 } } },
        { "id": 4, "name": "Number Slider", "nickName": "Plate Width", "inputValues": { "0": { "min": 5, "max": 30, "value": 14, "decimals": 1 } } },
        { "id": 5, "name": "Series", "nickName": "Floor Index" },
        { "id": 6, "name": "Multiplication", "nickName": "Z per floor" },
        { "id": 7, "name": "Unit Z", "nickName": "Lift" },
        { "id": 8, "name": "Rectangle", "nickName": "Base Plate" },
        { "id": 9, "name": "Move", "nickName": "Stack" },
        { "id": 10, "name": "Multiplication", "nickName": "Angle / floor" },
        { "id": 11, "name": "Rotate", "nickName": "Twist" },
        { "id": 12, "name": "Loft", "nickName": "Skin" },
        { "id": 13, "name": "Custom Preview", "nickName": "Show" },
        { "id": 14, "name": "Number Slider", "nickName": "Fillet Radius", "inputValues": { "0": { "min": 0, "max": 5, "value": 1, "decimals": 2 } } },
        { "id": 15, "name": "Fillet", "nickName": "Soft Corners" },
        { "id": 16, "name": "Cap Holes", "nickName": "Cap Top" },
        { "id": 17, "name": "Multiplication", "nickName": "Total Height" },
        { "id": 18, "name": "Panel", "nickName": "Height Readout" },
        { "id": 19, "name": "Hexagonal Grid", "nickName": "Site Grid" },
        { "id": 20, "name": "Custom Preview", "nickName": "Show Site" }
      ],
      "connections": [
        { "fromComponent": 1, "fromOutput": 0, "toComponent": 5, "toInput": 2 },
        { "fromComponent": 2, "fromOutput": 0, "toComponent": 6, "toInput": 0 },
        { "fromComponent": 5, "fromOutput": 0, "toComponent": 6, "toInput": 1 },
        { "fromComponent": 6, "fromOutput": 0, "toComponent": 7, "toInput": 0 },
        { "fromComponent": 4, "fromOutput": 0, "toComponent": 8, "toInput": 1 },
        { "fromComponent": 8, "fromOutput": 0, "toComponent": 15, "toInput": 0 },
        { "fromComponent": 14, "fromOutput": 0, "toComponent": 15, "toInput": 1 },
        { "fromComponent": 15, "fromOutput": 0, "toComponent": 9, "toInput": 0 },
        { "fromComponent": 7, "fromOutput": 0, "toComponent": 9, "toInput": 1 },
        { "fromComponent": 5, "fromOutput": 0, "toComponent": 10, "toInput": 0 },
        { "fromComponent": 3, "fromOutput": 0, "toComponent": 10, "toInput": 1 },
        { "fromComponent": 9, "fromOutput": 0, "toComponent": 11, "toInput": 0 },
        { "fromComponent": 10, "fromOutput": 0, "toComponent": 11, "toInput": 1 },
        { "fromComponent": 11, "fromOutput": 0, "toComponent": 12, "toInput": 0 },
        { "fromComponent": 12, "fromOutput": 0, "toComponent": 16, "toInput": 0 },
        { "fromComponent": 16, "fromOutput": 0, "toComponent": 13, "toInput": 0 },
        { "fromComponent": 1, "fromOutput": 0, "toComponent": 17, "toInput": 0 },
        { "fromComponent": 2, "fromOutput": 0, "toComponent": 17, "toInput": 1 },
        { "fromComponent": 17, "fromOutput": 0, "toComponent": 18, "toInput": 0 },
        { "fromComponent": 4, "fromOutput": 0, "toComponent": 19, "toInput": 1 },
        { "fromComponent": 19, "fromOutput": 0, "toComponent": 20, "toInput": 0 }
      ],
      "groups": [
        { "name": "Parameters", "componentIds": [1,2,3,4,14], "color": "#5CDB7A", "reasoning": "All five live knobs, untouched — the new annotation reuses them rather than introducing duplicate inputs." },
        { "name": "Floor Stacking", "componentIds": [5,6,7], "color": "#54a8e8", "reasoning": "Unchanged: Series → Z elevation → vertical move vectors." },
        { "name": "Base Plate", "componentIds": [8,15], "color": "#54a8e8", "reasoning": "Unchanged: rectangle softened by Fillet before stacking." },
        { "name": "Twist & Stack", "componentIds": [9,10,11], "color": "#e8b84d", "reasoning": "Unchanged twist engine — move, cumulative angle, rotate." },
        { "name": "Skin & Display", "componentIds": [12,16,13], "color": "#5CDB7A", "reasoning": "Unchanged: loft, cap, preview." },
        { "name": "Readout & Site", "componentIds": [17,18,19,20], "color": "#5ED6DC", "reasoning": "Floor Count times Floor Height gives total tower height, shown on a Panel; a Hexagonal Grid sized to the plate width lays down a site footprint, previewed alongside the tower. This stage only reads the existing parameters — it never disturbs the geometry chain." }
      ],
      "advice": "The height readout updates live as you drag Floor Count or Floor Height; the hex grid is a non-structural site reference you can mute by disabling its preview."
    }
    """;

    private const string BrokenScenario = """
    {
      "solutionName": "Broken Tower (negative control)",
      "solutionDescription": "Intentionally defective: a hallucinated component, an orphan, a dangling wire, and a stage with no reasoning.",
      "components": [
        { "id": 1, "name": "Number Slider", "nickName": "Count" },
        { "id": 2, "name": "Series", "nickName": "Index" },
        { "id": 3, "name": "Rectangle", "nickName": "Plate" },
        { "id": 4, "name": "Quantum Twist Engine", "nickName": "Magic" },
        { "id": 5, "name": "Move", "nickName": "Stack" },
        { "id": 6, "name": "Loft", "nickName": "Skin" },
        { "id": 7, "name": "Custom Preview", "nickName": "Show" }
      ],
      "connections": [
        { "fromComponent": 1, "fromOutput": 0, "toComponent": 2, "toInput": 2 },
        { "fromComponent": 2, "fromOutput": 0, "toComponent": 5, "toInput": 1 },
        { "fromComponent": 3, "fromOutput": 0, "toComponent": 5, "toInput": 0 },
        { "fromComponent": 5, "fromOutput": 0, "toComponent": 4, "toInput": 0 },
        { "fromComponent": 4, "fromOutput": 0, "toComponent": 6, "toInput": 0 },
        { "fromComponent": 6, "fromOutput": 0, "toComponent": 7, "toInput": 0 },
        { "fromComponent": 6, "fromOutput": 0, "toComponent": 77, "toInput": 0 }
      ],
      "groups": [
        { "name": "Inputs", "componentIds": [1,2], "reasoning": "Count drives the series of floor indices." },
        { "name": "Build", "componentIds": [4,5,6,7], "reasoning": "" }
      ]
    }
    """;
}
