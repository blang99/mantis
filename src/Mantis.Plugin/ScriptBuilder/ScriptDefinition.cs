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
}

public class MultiSolutionResponse
{
    [JsonPropertyName("solutions")]
    public List<ScriptDefinition> Solutions { get; set; } = new();

    [JsonPropertyName("comparisonSummary")]
    public string ComparisonSummary { get; set; } = "";
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
