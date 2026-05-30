using Grasshopper;
using Grasshopper.Plugin;

namespace Mantis.Plugin;

/// <summary>
/// Bridges the Rhino-side entry points to Grasshopper. The MANTIS panel can be
/// opened from a Rhino tab without the user ever opening the Grasshopper editor,
/// so before we scan components or build a graph we make sure Grasshopper itself
/// is loaded in the background.
/// </summary>
internal static class GrasshopperGateway
{
    /// <summary>
    /// Load the Grasshopper plugin (without forcing its editor window open) so
    /// <c>Grasshopper.Instances</c> and the component server are available.
    /// Best-effort: a failure here degrades gracefully — the component registry
    /// scan already tolerates Grasshopper not being present.
    /// </summary>
    public static void EnsureLoaded()
    {
        // Already up? The component server is populated once GH has loaded.
        try
        {
            if (Instances.ComponentServer != null &&
                Instances.ComponentServer.ObjectProxies.Count > 0)
                return;
        }
        catch { /* Instances not ready yet — fall through and load it */ }

        try
        {
            if (Rhino.RhinoApp.GetPlugInObject("Grasshopper") is GH_RhinoScriptInterface gh &&
                !gh.IsEditorLoaded())
            {
                gh.LoadEditor();
            }
        }
        catch { /* best-effort */ }
    }

    /// <summary>Bring the Grasshopper editor window forward (used by the "Open Grasshopper" affordance).</summary>
    public static void ShowEditor()
    {
        try
        {
            if (Rhino.RhinoApp.GetPlugInObject("Grasshopper") is GH_RhinoScriptInterface gh)
            {
                if (!gh.IsEditorLoaded()) gh.LoadEditor();
                gh.ShowEditor();
            }
        }
        catch { /* best-effort */ }
    }
}
