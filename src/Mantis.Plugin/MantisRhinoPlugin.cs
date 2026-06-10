using Mantis.Plugin.UI;
using Rhino.PlugIns;

namespace Mantis.Plugin;

/// <summary>
/// Rhino-side entry point. Because MANTIS ships as a <c>.rhp</c>, Rhino loads this
/// at startup and Grasshopper still discovers the components inside the same
/// assembly. Registering the dockable panel here means MANTIS appears as a Rhino
/// tab that the user can open WITHOUT first opening Grasshopper.
/// </summary>
public class MantisRhinoPlugin : PlugIn
{
    public MantisRhinoPlugin()
    {
        Instance = this;
    }

    public static MantisRhinoPlugin? Instance { get; private set; }

    /// <summary>Load at startup so the MANTIS panel is registered and available
    /// from Rhino's Panels menu before any command is run.</summary>
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        try
        {
            // Register the panel TYPE only — Rhino instantiates it lazily the
            // first time it's shown, so Grasshopper is not force-loaded here.
            MantisLog.Info("Rhino plugin OnLoad: registering MANTIS panel…");
            Rhino.UI.Panels.RegisterPanel(
                this,
                typeof(MantisDockPanel),
                "MANTIS",
                null);
            MantisLog.Info("Rhino plugin OnLoad: panel registered. Open MANTIS via the 'Mantis' command "
                           + "or Panels menu — Grasshopper is NOT loaded until you actually build.");
        }
        catch (Exception ex)
        {
            errorMessage = $"MANTIS panel registration failed: {ex.Message}";
            MantisLog.Error("Rhino plugin OnLoad: panel registration failed", ex);
        }

        return LoadReturnCode.Success;
    }
}
