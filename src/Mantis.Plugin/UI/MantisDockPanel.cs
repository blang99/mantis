using System.Runtime.InteropServices;
using Eto.Forms;

namespace Mantis.Plugin.UI;

/// <summary>
/// Dockable Rhino panel that hosts the MANTIS chat UI. Registered by
/// <see cref="MantisRhinoPlugin"/> so it shows up as a Rhino tab. Reuses the very
/// same <see cref="ChatPanel"/> the Grasshopper float uses, sharing one
/// <see cref="MantisService"/> so conversation and component knowledge are
/// consistent across both entry points.
/// </summary>
[Guid("BA6BEED0-D3BF-4BE5-8CF2-65C3C88BB738")]
public class MantisDockPanel : Panel
{
    /// <summary>Stable id used by the <c>Mantis</c> command to open/close this panel.</summary>
    public static Guid PanelId => typeof(MantisDockPanel).GUID;

    public MantisDockPanel()
    {
        BackgroundColor = MantisPanelHost.BgDeep;

        // Creating the service ensures Grasshopper is loaded in the background and
        // the component catalog is scanned — so a build works even though the user
        // never opened the Grasshopper editor.
        var service = MantisPanelHost.GetOrCreateService();
        Content = new ChatPanel(service);
    }
}
