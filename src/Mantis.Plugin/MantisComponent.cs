using System;
using System.Drawing;
using Grasshopper.Kernel;
using Mantis.Plugin.UI;

namespace Mantis.Plugin;

/// <summary>
/// Optional Grasshopper component for toggling the MANTIS panel.
/// The panel also auto-opens when Grasshopper loads (via MantisLoader).
/// </summary>
public class MantisComponent : GH_Component
{
    public MantisComponent()
        : base("MANTIS", "Mantis",
            "Toggle the MANTIS AI chat panel. The panel also opens automatically when Grasshopper starts.",
            "Mantis", "AI")
    {
    }

    public override Guid ComponentGuid => new("8B4E9F3A-2C5D-4A7E-B6F1-3D8E2A5C7F9B");

    protected override Bitmap? Icon => null;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddBooleanParameter("Toggle", "T", "Toggle the MANTIS panel on/off", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddTextParameter("Status", "S", "MANTIS status", GH_ParamAccess.item);
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        bool toggle = false;
        DA.GetData(0, ref toggle);

        if (toggle)
        {
            if (MantisPanelHost.IsVisible)
                MantisPanelHost.Hide();
            else
                MantisPanelHost.Show();
        }

        var visible = MantisPanelHost.IsVisible ? "visible" : "hidden";
        var provider = MantisPanelHost.Service?.ProviderManager?.ActiveProviderName ?? "none";
        DA.SetData(0, $"MANTIS v0.1.1 — panel {visible} — provider: {provider}");
    }
}
