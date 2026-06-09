using System.Text.Json.Serialization;

namespace Mantis.Plugin.ScriptBuilder;

public class ScriptDefinition
{
    [JsonPropertyName("solutionName")]
    public string SolutionName { get; set; } = "";

    [JsonPropertyName("solutionDescription")]
    public string SolutionDescription { get; set; } = "";

    [JsonPropertyName("components")]
    public List<ComponentDef> Components { get; set; } = new();

    [JsonPropertyName("connections")]
    public List<ConnectionDef> Connections { get; set; } = new();

    [JsonPropertyName("advice")]
    public string Advice { get; set; } = "";

    [JsonPropertyName("requiredPlugins")]
    public List<string> RequiredPlugins { get; set; } = new();

    [JsonPropertyName("groups")]
    public List<GroupDef> Groups { get; set; } = new();
}

/// <summary>
/// A logical stage of the script. Each group becomes one GH_Group on the
/// canvas (the visual workflow annotation) and also carries the reasoning
/// shown in the chat panel's "thought process" narration. One structure
/// drives both the on-canvas grouping and the wiring explanation.
/// </summary>
public class GroupDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("componentIds")]
    public List<int> ComponentIds { get; set; } = new();

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

public class MultiSolutionResponse
{
    [JsonPropertyName("solutions")]
    public List<ScriptDefinition> Solutions { get; set; } = new();

    [JsonPropertyName("comparisonSummary")]
    public string ComparisonSummary { get; set; } = "";
}

/// <summary>
/// The result of the PLAN pass — MANTIS's explicit, reasoned strategy produced BEFORE
/// any component is emitted. The model first UNDERSTANDS the request and lays out an
/// ordered set of steps with reasoning; the build pass then turns each step into a
/// stage/<see cref="GroupDef"/> (one GH_Group on the canvas). So the same plan drives
/// (a) the build, (b) the chat "thought process" narration, and (c) the plan side-panel
/// where each step links to its on-canvas group.
/// </summary>
public class PlanDef
{
    /// <summary>One-line restatement of what the user actually wants.</summary>
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "";

    /// <summary>Assumptions MANTIS is making (units, defaults, interpretation).</summary>
    [JsonPropertyName("assumptions")]
    public List<string> Assumptions { get; set; } = new();

    /// <summary>The ordered workflow steps — each becomes a build stage / GH group.</summary>
    [JsonPropertyName("steps")]
    public List<PlanStepDef> Steps { get; set; } = new();

    /// <summary>Anything genuinely ambiguous worth flagging (not blocking).</summary>
    [JsonPropertyName("openQuestions")]
    public List<string> OpenQuestions { get; set; } = new();
}

/// <summary>One ordered stage of the plan — maps 1:1 to a <see cref="GroupDef"/> at build time.</summary>
public class PlanStepDef
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>WHY this step exists in the workflow.</summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    /// <summary>HOW — the technique / components this step will use.</summary>
    [JsonPropertyName("approach")]
    public string Approach { get; set; } = "";

    /// <summary>Component names this step is likely to need (guides the build pass).</summary>
    [JsonPropertyName("candidateComponents")]
    public List<string> CandidateComponents { get; set; } = new();
}

/// <summary>
/// A BUILT stage as the plan side-panel sees it: the stage's name + reasoning paired with
/// the real GH_Group it became on the canvas (<see cref="GroupGuid"/>). Clicking the step
/// in the panel navigates/zooms the Grasshopper canvas to that group. GroupGuid is
/// <see cref="System.Guid.Empty"/> when the stage produced no group (all members failed to
/// resolve), so the panel can still render it but leave it non-navigable. Produced by
/// CanvasGrouping.Apply, ordered 1:1 with the script's groups / the plan's steps.
/// </summary>
public class PlanStep
{
    public string Name { get; set; } = "";
    public string Reasoning { get; set; } = "";
    public string? Color { get; set; }
    public Guid GroupGuid { get; set; }
    public List<int> ComponentIds { get; set; } = new();
    public List<Guid> MemberGuids { get; set; } = new();
}

public class ComponentDef
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("nickName")]
    public string? NickName { get; set; }

    [JsonPropertyName("inputValues")]
    public Dictionary<string, object>? InputValues { get; set; }

    [JsonPropertyName("position")]
    public PointDef? Position { get; set; }
}

public class ConnectionDef
{
    [JsonPropertyName("fromComponent")]
    public int FromComponent { get; set; }

    [JsonPropertyName("fromOutput")]
    public int FromOutput { get; set; }

    [JsonPropertyName("toComponent")]
    public int ToComponent { get; set; }

    [JsonPropertyName("toInput")]
    public int ToInput { get; set; }

    [JsonPropertyName("fromOutputName")]
    public string? FromOutputName { get; set; }

    [JsonPropertyName("toInputName")]
    public string? ToInputName { get; set; }
}

public class PointDef
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}
