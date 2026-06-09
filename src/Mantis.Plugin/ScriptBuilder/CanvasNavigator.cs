using System;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Mantis.Plugin.ScriptBuilder;

/// <summary>
/// Navigates the live Grasshopper canvas to a specific GH_Group — the click-action behind
/// the plan side-panel: clicking a plan step selects and frames that step's group on the
/// canvas, so reading the plan and finding it on the canvas are one motion.
///
/// View-only and fully guarded: a deleted/missing group degrades to a no-op returning
/// <c>false</c> (so the panel can gray the step out) rather than throwing. Call on the UI
/// thread.
///
/// NOTE on portability: GH_Canvas derives from a WinForms Control that this cross-platform
/// net7.0 build cannot reference directly, so the viewport zoom is done via reflection. The
/// SELECTION (the load-bearing part) uses the typed GH_Document API and always works.
/// </summary>
internal static class CanvasNavigator
{
    /// <summary>
    /// Select + frame the Grasshopper canvas on the group with the given InstanceGuid.
    /// Returns false if the canvas/group is unavailable (e.g. the group was deleted).
    /// </summary>
    public static bool ZoomToGroup(Guid groupGuid)
    {
        if (groupGuid == Guid.Empty) return false;
        try
        {
            // GH_Document via the same chained access ChatPanel uses — declaring a GH_Canvas
            // local would force a System.Windows.Forms reference we deliberately don't have.
            var doc = Instances.ActiveCanvas?.Document;
            if (doc == null) return false;
            if (doc.FindObject(groupGuid, true) is not GH_Group grp) return false;

            // Highlight the group (typed, cross-platform).
            doc.DeselectAll();
            grp.Attributes.Selected = true;

            // Best-effort frame; selection already succeeded if this no-ops.
            TryFrameGroup(grp.Attributes.Bounds);

            Instances.RedrawCanvas();
            return true;
        }
        catch
        {
            return false;   // a deleted group / API mismatch must never throw into the UI
        }
    }

    /// <summary>
    /// Zoom/pan the GH canvas viewport to fit <paramref name="bounds"/>, via reflection on
    /// the GH_Canvas.Viewport (Zoom/Target/ScreenPort) so we never reference the WinForms
    /// Control base at compile time. Silent best-effort.
    /// </summary>
    private static void TryFrameGroup(System.Drawing.RectangleF bounds)
    {
        try
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            bounds.Inflate(40f, 40f);

            var canvas = typeof(Instances).GetProperty("ActiveCanvas")?.GetValue(null);
            var viewport = canvas?.GetType().GetProperty("Viewport")?.GetValue(canvas);
            if (viewport == null) return;
            var vt = viewport.GetType();

            if (vt.GetProperty("ScreenPort")?.GetValue(viewport) is not System.Drawing.Rectangle port
                || port.Width <= 0 || port.Height <= 0)
                return;

            var zoom = Math.Min(port.Width / bounds.Width, port.Height / bounds.Height) * 0.85f;
            zoom = Math.Max(0.1f, Math.Min(zoom, 2.0f));

            vt.GetProperty("Zoom")?.SetValue(viewport, zoom);
            vt.GetProperty("Target")?.SetValue(viewport, new System.Drawing.Point(
                (int)(bounds.X + bounds.Width / 2f), (int)(bounds.Y + bounds.Height / 2f)));
        }
        catch
        {
            // Zoom is a nicety; the selection already worked.
        }
    }
}
