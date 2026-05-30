using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Mantis.Plugin;

public class MantisInfo : GH_AssemblyInfo
{
    public override string Name => "MANTIS";
    public override string Version => "0.1.1";
    public override string Description => "AI-powered natural language to native Grasshopper scripts. Describe what you want to build — MANTIS generates the components, wired and ready.";
    public override Guid Id => new("7A3F8E2B-1D4C-4F6A-9E8B-5C2D7F1A3E9B");
    public override string AuthorName => "MANTIS";
    public override string AuthorContact => "";
    public override Bitmap? Icon => null;
}
