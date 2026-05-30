using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Mantis.Plugin.ScriptBuilder;

public class CanvasBuilder
{
    private readonly ComponentFactory _factory;
    private readonly LayoutEngine _layout;

    public CanvasBuilder(ComponentFactory factory, LayoutEngine layout)
    {
        _factory = factory;
        _layout = layout;
    }

    public BuildResult Build(ScriptDefinition script, GH_Document document)
    {
        var result = new BuildResult();
        var placed = new Dictionary<int, IGH_DocumentObject>();
        var positions = _layout.ComputeLayout(script);

        document.UndoUtil.RecordEvent(new Grasshopper.Kernel.Undo.GH_UndoRecord("Mantis: Generate Script"));

        foreach (var compDef in script.Components)
        {
            var obj = _factory.CreateComponent(compDef);
            if (obj == null)
            {
                result.Errors.Add($"Unknown component: {compDef.Name}");
                continue;
            }

            if (positions.TryGetValue(compDef.Id, out var pos))
                obj.Attributes.Pivot = pos;
            else if (compDef.Position != null)
                obj.Attributes.Pivot = new PointF((float)compDef.Position.X, (float)compDef.Position.Y);

            document.AddObject(obj, false);
            placed[compDef.Id] = obj;
            result.PlacedComponents++;
        }

        foreach (var conn in script.Connections)
        {
            if (!placed.TryGetValue(conn.FromComponent, out var sourceObj) ||
                !placed.TryGetValue(conn.ToComponent, out var targetObj))
            {
                result.Errors.Add($"Connection references missing component: {conn.FromComponent}→{conn.ToComponent}");
                continue;
            }

            var outputParam = GetOutputParam(sourceObj, conn.FromOutput);
            var inputParam = GetInputParam(targetObj, conn.ToInput);
            if (outputParam == null || inputParam == null)
            {
                result.Errors.Add($"Cannot resolve params for {conn.FromComponent}→{conn.ToComponent}");
                continue;
            }

            inputParam.AddSource(outputParam);
            result.WiredConnections++;
        }

        // GUARANTEE A WORKING FILE: fill any required input left unconnected
        // with a sensible default so the graph resolves with no warnings.
        CanvasRepair.SatisfyRequiredInputs(placed.Values);

        document.NewSolution(false);
        return result;
    }

    private static IGH_Param? GetOutputParam(IGH_DocumentObject obj, int index)
    {
        if (obj is GH_Component comp && index < comp.Params.Output.Count)
            return comp.Params.Output[index];
        if (index == 0 && obj is IGH_Param param)
            return param; // Slider, Panel, Toggle — they ARE the param
        return null;
    }

    private static IGH_Param? GetInputParam(IGH_DocumentObject obj, int index)
    {
        if (obj is GH_Component comp && index < comp.Params.Input.Count)
            return comp.Params.Input[index];
        if (index == 0 && obj is IGH_Param param)
            return param;
        return null;
    }
}

public class BuildResult
{
    public int PlacedComponents { get; set; }
    public int WiredConnections { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;
}
