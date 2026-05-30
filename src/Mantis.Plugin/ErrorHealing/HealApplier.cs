using Grasshopper.Kernel;
using Mantis.Plugin.ScriptBuilder;

namespace Mantis.Plugin.ErrorHealing;

public class HealApplier
{
    private readonly CanvasBuilder _builder;

    public HealApplier(CanvasBuilder builder)
    {
        _builder = builder;
    }

    public BuildResult ApplyFix(ScriptDefinition fix, GH_Document document, bool clearExisting = false)
    {
        document.UndoUtil.RecordEvent(new Grasshopper.Kernel.Undo.GH_UndoRecord("Mantis: Heal Errors"));

        if (clearExisting)
        {
            var toRemove = document.Objects
                .OfType<GH_Component>()
                .Where(c => c.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error)
                .Cast<IGH_DocumentObject>()
                .ToList();

            foreach (var obj in toRemove)
                document.RemoveObject(obj, false);
        }

        return _builder.Build(fix, document);
    }
}
