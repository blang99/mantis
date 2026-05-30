using System.Text.Json.Serialization;

namespace Mantis.Plugin.Knowledge;

public class ComponentInfo
{
    [JsonPropertyName("guid")]
    public Guid Guid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("nickName")]
    public string NickName { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("subcategory")]
    public string Subcategory { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("inputs")]
    public List<ParamInfo> Inputs { get; set; } = new();

    [JsonPropertyName("outputs")]
    public List<ParamInfo> Outputs { get; set; } = new();

    [JsonPropertyName("pluginName")]
    public string PluginName { get; set; } = "Grasshopper";

    [JsonPropertyName("isSpecial")]
    public bool IsSpecial { get; set; }

    /// <summary>
    /// True for the curated components shipped in the embedded catalog
    /// (BuiltInComponents.json). These are the common workhorses that are ALWAYS
    /// included when we trim the catalog for small-context local models, so the
    /// model never loses access to circles, extrude, move, series, etc. Set
    /// programmatically at load time — not part of the serialized JSON.
    /// </summary>
    [JsonIgnore]
    public bool IsCore { get; set; }
}

public class ParamInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("nickName")]
    public string NickName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("typeName")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("access")]
    public string Access { get; set; } = "item";

    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }
}
