using Mantis.Plugin.UI;
using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace Mantis.Plugin;

/// <summary>
/// The <c>Mantis</c> Rhino command. Toggles the dockable MANTIS panel so the user
/// can summon the AI chat from the Rhino command line — no Grasshopper needed.
/// </summary>
public class MantisCommand : Command
{
    public override string EnglishName => "Mantis";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var panelId = MantisDockPanel.PanelId;

        if (Panels.IsPanelVisible(panelId))
            Panels.ClosePanel(panelId);
        else
            Panels.OpenPanel(panelId);

        return Result.Success;
    }
}
