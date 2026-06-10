namespace Mantis.Plugin.ScriptBuilder;

public enum IssueSeverity
{
    /// <summary>Will degrade the built graph (a missing component or a wire that can't be made).</summary>
    Error,
    /// <summary>Builds fine, but breaks the "every stage is shown / narrated" contract.</summary>
    Warning
}

/// <summary>
/// One thing wrong with a <see cref="ScriptDefinition"/> before it is built.
/// </summary>
public class ScriptIssue
{
    public IssueSeverity Severity { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public int? ComponentId { get; init; }

    public override string ToString() => $"[{Severity}] {Message}";
}

/// <summary>
/// Pure, Grasshopper-free structural validation of a generated script. This is the
/// same set of invariants the headless simulation checks, lifted into shippable code
/// so the runtime enforces them too — not just the test suite.
///
/// Name resolution is injected as a predicate (<c>canResolve</c>) so this stays free
/// of the Grasshopper dependency: the plugin passes <c>ComponentFactory.CanResolve</c>,
/// the tests pass a faithful replica of the registry cascade.
///
/// Port arity is injected the same way (<c>portArity</c>): given a resolved component
/// name it returns that component's (input count, output count), or <c>null</c> when the
/// arity is unknown/untrusted. This lets the validator catch the single most common
/// silent failure in generated graphs — a connection wired to a port index that does not
/// exist on the component — WITHOUT taking a Grasshopper dependency. The plugin backs it
/// with the catalog (<c>ComponentRegistry.FindByName</c>); the tests pass a fake.
///
/// It validates STRUCTURE and port RANGE, not full Grasshopper type semantics (whether a
/// Curve output is convertible to a Brep input) — that requires a live document and is
/// exercised by the deploy test.
/// </summary>
public static class ScriptValidator
{
    public static List<ScriptIssue> Validate(
        ScriptDefinition? script,
        Func<string, bool> canResolve,
        Func<string, (int Inputs, int Outputs)?>? portArity = null,
        Func<string, int, bool, string?>? typeOf = null)
    {
        var issues = new List<ScriptIssue>();

        if (script == null)
        {
            issues.Add(new ScriptIssue
            {
                Severity = IssueSeverity.Error,
                Code = "NULL_SCRIPT",
                Message = "No script was produced."
            });
            return issues;
        }

        if (script.Components.Count == 0)
        {
            issues.Add(new ScriptIssue
            {
                Severity = IssueSeverity.Error,
                Code = "EMPTY_SCRIPT",
                Message = "Script contains no components."
            });
            return issues;
        }

        // --- Unique component ids (and an id → name map for port checks) ---
        var ids = new HashSet<int>();
        var idToName = new Dictionary<int, string>();
        foreach (var c in script.Components)
        {
            if (!ids.Add(c.Id))
                issues.Add(new ScriptIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = "DUPLICATE_ID",
                    ComponentId = c.Id,
                    Message = $"Duplicate component id {c.Id} (\"{c.Name}\")."
                });
            idToName.TryAdd(c.Id, c.Name);
        }

        // --- Every component name must resolve to something placeable ---
        foreach (var c in script.Components)
        {
            if (!canResolve(c.Name))
                issues.Add(new ScriptIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = "UNRESOLVABLE_NAME",
                    ComponentId = c.Id,
                    Message = $"Component name \"{c.Name}\" (id {c.Id}) is not in the catalog and cannot be placed."
                });
        }

        // --- Every connection must reference components that exist ---
        foreach (var conn in script.Connections)
        {
            if (!ids.Contains(conn.FromComponent))
                issues.Add(new ScriptIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = "DANGLING_CONNECTION",
                    Message = $"Connection from missing component id {conn.FromComponent}."
                });
            if (!ids.Contains(conn.ToComponent))
                issues.Add(new ScriptIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = "DANGLING_CONNECTION",
                    Message = $"Connection to missing component id {conn.ToComponent}."
                });
            if (conn.FromComponent == conn.ToComponent)
                issues.Add(new ScriptIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = "SELF_CONNECTION",
                    ComponentId = conn.FromComponent,
                    Message = $"Component {conn.FromComponent} is wired to itself."
                });

            // --- Port RANGE: the wire must reference a port the component has ---
            //   Only checked when the arity resolver is supplied AND it returns a
            //   trustworthy count for the relevant side. A count of 0 is treated as
            //   "unknown for that side" (params / specials whose ports the catalog
            //   didn't capture) and skipped, so a valid graph is never blocked by
            //   missing catalog data. A negative index is always wrong.
            if (portArity != null)
            {
                if (idToName.TryGetValue(conn.FromComponent, out var fromName) &&
                    portArity(fromName) is { } fa &&
                    (conn.FromOutput < 0 || (fa.Outputs > 0 && conn.FromOutput >= fa.Outputs)))
                    issues.Add(new ScriptIssue
                    {
                        Severity = IssueSeverity.Error,
                        Code = "PORT_OUT_OF_RANGE",
                        ComponentId = conn.FromComponent,
                        Message = $"Connection from output #{conn.FromOutput} of component {conn.FromComponent} " +
                                  $"(\"{fromName}\"), which has {fa.Outputs} output(s) " +
                                  $"(valid 0–{Math.Max(fa.Outputs - 1, 0)})."
                    });

                if (idToName.TryGetValue(conn.ToComponent, out var toName) &&
                    portArity(toName) is { } ta &&
                    (conn.ToInput < 0 || (ta.Inputs > 0 && conn.ToInput >= ta.Inputs)))
                    issues.Add(new ScriptIssue
                    {
                        Severity = IssueSeverity.Error,
                        Code = "PORT_OUT_OF_RANGE",
                        ComponentId = conn.ToComponent,
                        Message = $"Connection into input #{conn.ToInput} of component {conn.ToComponent} " +
                                  $"(\"{toName}\"), which has {ta.Inputs} input(s) " +
                                  $"(valid 0–{Math.Max(ta.Inputs - 1, 0)})."
                    });
            }

            // --- TYPE compatibility (conservative): flag ONLY the clearest mismatch — a scalar
            //   number wired to/from a geometry port. Unknown types or the Generic wildcard are
            //   never flagged, since Grasshopper's coercion is permissive. ---
            if (typeOf != null
                && idToName.TryGetValue(conn.FromComponent, out var fromTn)
                && idToName.TryGetValue(conn.ToComponent, out var toTn))
            {
                var srcType = typeOf(fromTn, conn.FromOutput, true);
                var dstType = typeOf(toTn, conn.ToInput, false);
                if (IsClearTypeMismatch(srcType, dstType))
                    issues.Add(new ScriptIssue
                    {
                        Severity = IssueSeverity.Error,
                        Code = "TYPE_MISMATCH",
                        ComponentId = conn.ToComponent,
                        Message = $"Output of component {conn.FromComponent} (\"{fromTn}\", {srcType}) is wired into a " +
                                  $"{dstType} input of component {conn.ToComponent} (\"{toTn}\") — incompatible types."
                    });
            }
        }

        // --- Stages (groups) must cover every component exactly once and narrate ---
        var cover = new Dictionary<int, int>();
        foreach (var g in script.Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Reasoning))
                issues.Add(new ScriptIssue
                {
                    Severity = IssueSeverity.Warning,
                    Code = "MISSING_REASONING",
                    Message = $"Stage \"{g.Name}\" has no thought-process narration."
                });

            foreach (var cid in g.ComponentIds)
            {
                if (!ids.Contains(cid))
                    issues.Add(new ScriptIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = "GROUP_MISSING_COMPONENT",
                        ComponentId = cid,
                        Message = $"Stage \"{g.Name}\" references component id {cid}, which does not exist."
                    });
                cover[cid] = cover.GetValueOrDefault(cid) + 1;
            }
        }

        // Only enforce coverage when the model actually emitted stages. A script with
        // no groups is a legacy/simple response, not a coverage failure.
        if (script.Groups.Count > 0)
        {
            foreach (var c in script.Components)
            {
                var n = cover.GetValueOrDefault(c.Id);
                if (n == 0)
                    issues.Add(new ScriptIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = "ORPHAN_COMPONENT",
                        ComponentId = c.Id,
                        Message = $"Component {c.Id} (\"{c.Name}\") is not in any stage/group."
                    });
                else if (n > 1)
                    issues.Add(new ScriptIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Code = "MULTI_GROUP",
                        ComponentId = c.Id,
                        Message = $"Component {c.Id} (\"{c.Name}\") appears in {n} stages (must be exactly one)."
                    });
            }
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ScriptIssue> issues) =>
        issues.Any(i => i.Severity == IssueSeverity.Error);

    public static List<ScriptIssue> Errors(IEnumerable<ScriptIssue> issues) =>
        issues.Where(i => i.Severity == IssueSeverity.Error).ToList();

    // Conservative type buckets. Anything not in either bucket (Point, Vector, Plane, Domain,
    // Transform, Generic, …) is treated as "could coerce" and is NEVER flagged.
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Number", "Integer", "Boolean", "Double", "Int", "Bool" };
    private static readonly HashSet<string> GeometryTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Curve", "Surface", "Brep", "Mesh", "Geometry", "Rectangle", "Polyline", "Box", "Solid", "SubD" };

    /// <summary>True only for the clearest mismatch: a scalar number wired to/from a geometry port.</summary>
    private static bool IsClearTypeMismatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        bool aNum = NumericTypes.Contains(a!), bNum = NumericTypes.Contains(b!);
        bool aGeo = GeometryTypes.Contains(a!), bGeo = GeometryTypes.Contains(b!);
        return (aNum && bGeo) || (aGeo && bNum);
    }
}
