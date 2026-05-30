using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Mantis.Plugin.UI;

namespace Mantis.Plugin;

/// <summary>
/// Auto-initializes MANTIS when Grasshopper loads.
/// No boolean toggle needed — the panel shows up automatically.
/// </summary>
public class MantisLoader : GH_AssemblyPriority
{
    private static bool _initialized;

    public override GH_LoadingInstruction PriorityLoad()
    {
        // Hook into the Grasshopper canvas created event so we initialize
        // once the GH editor is fully ready, not during early load.
        Instances.CanvasCreated += OnCanvasCreated;
        return GH_LoadingInstruction.Proceed;
    }

    private static void OnCanvasCreated(GH_Canvas canvas)
    {
        // Only initialize once
        Instances.CanvasCreated -= OnCanvasCreated;

        if (_initialized) return;
        _initialized = true;

        try
        {
            // Funnel through the shared host so the Grasshopper float and the
            // Rhino dock panel use the same initialized service.
            MantisPanelHost.GetOrCreateService();

            // Auto-show the MANTIS panel docked on the right
            Eto.Forms.Application.Instance.AsyncInvoke(() =>
            {
                MantisPanelHost.Show();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MANTIS init failed: {ex.Message}");
        }
    }

    /// <summary>Access the shared service instance (used by MantisComponent fallback).</summary>
    internal static MantisService? Service => MantisPanelHost.Service;
}
