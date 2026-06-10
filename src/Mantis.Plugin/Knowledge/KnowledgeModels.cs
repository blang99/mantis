namespace Mantis.Plugin.Knowledge;

/// <summary>
/// A popular downloadable Grasshopper plugin MANTIS KNOWS ABOUT but can only BUILD with once
/// it's installed (the runtime scan covers installed ones). This "suggest" layer lets MANTIS
/// recommend the right add-on — by name, capability and signature components — when the native
/// catalog can't express what the user wants. Metadata only; never used for emitting graphs.
/// </summary>
public class PluginEntry
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Capability { get; set; } = "";
    public List<string> HeroComponents { get; set; } = new();
    public string InstallHint { get; set; } = "";
}

/// <summary>A Rhino command in MANTIS's reference (for Ask mode / future Rhino-side actions).</summary>
public class RhinoCommand
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}
