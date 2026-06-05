using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Mantis.Plugin;

public class MantisInfo : GH_AssemblyInfo
{
    public override string Name => "MANTIS";
    public override string Version => "0.3.1";
    public override string Description => "AI-powered natural language to native Grasshopper scripts. Describe what you want to build — MANTIS generates the components, wired and ready.";
    public override Guid Id => new("7A3F8E2B-1D4C-4F6A-9E8B-5C2D7F1A3E9B");
    public override string AuthorName => "MANTIS";
    public override string AuthorContact => "";

    /// <summary>
    /// The MANTIS "constellation" mark — the same brand icon shown in the Rhino
    /// Package Manager and on the website — loaded from the embedded PNG so the plugin
    /// presents one consistent identity across every surface. Best-effort: returns null
    /// if the resource can't be read rather than throwing during assembly load.
    /// </summary>
    public override Bitmap? Icon
    {
        get
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var name = Array.Find(asm.GetManifestResourceNames(),
                    n => n.EndsWith("mantis-icon.png", StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using var stream = asm.GetManifestResourceStream(name);
                return stream == null ? null : new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }
    }
}
