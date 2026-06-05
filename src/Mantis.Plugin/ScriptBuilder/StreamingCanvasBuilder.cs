using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Mantis.Plugin.AI;

namespace Mantis.Plugin.ScriptBuilder;

public class StreamingCanvasBuilder : IDisposable
{
    private readonly ComponentFactory _factory;
    private readonly LayoutEngine _layout;
    private readonly Dictionary<int, IGH_DocumentObject> _placed = new();
    private readonly Queue<ConnectionDef> _pendingWires = new();
    private readonly List<Guid> _groupGuids = new();
    private GH_Document? _document;
    private string? _undoName;

    public event Action<int, string>? OnComponentPlaced;
    public event Action<int, int>? OnConnectionMade;
    public event Action<string>? OnBuildError;
    public event Action? OnBuildComplete;

    public int PlacedCount => _placed.Count;

    /// <summary>
    /// Instance GUIDs of every object placed in the current/last build. Lets
    /// the service remove exactly the previous Mantis-generated graph before an
    /// iteration rebuild, so "build on top" never duplicates components.
    /// </summary>
    public IReadOnlyCollection<Guid> PlacedGuids =>
        _placed.Values.Select(o => o.InstanceGuid).Concat(_groupGuids).ToList();

    public StreamingCanvasBuilder(ComponentFactory factory, LayoutEngine layout)
    {
        _factory = factory;
        _layout = layout;
    }

    public void BeginBuild(GH_Document document, string undoName = "Mantis: Generate Script")
    {
        _document = document;
        _undoName = undoName;
        _placed.Clear();
        _pendingWires.Clear();
        _groupGuids.Clear();
        _document.UndoUtil.RecordEvent(new Grasshopper.Kernel.Undo.GH_UndoRecord(undoName));
    }

    public void PlaceComponent(ComponentDef compDef)
    {
        _ = PlaceComponentAsync(compDef);
    }

    public Task PlaceComponentAsync(ComponentDef compDef)
    {
        if (_document == null) return Task.CompletedTask;

        var tcs = new TaskCompletionSource<bool>();

        _document.ScheduleSolution(1, doc =>
        {
            try
            {
                var obj = _factory.CreateComponent(compDef);
                if (obj == null)
                {
                    OnBuildError?.Invoke(
                        $"Could not create component '{compDef.Name}' (id={compDef.Id}). " +
                        "The name may not match the catalog or the component is missing.");
                    tcs.TrySetResult(false);
                    return;
                }

                var position = compDef.Position != null
                    ? new PointF((float)compDef.Position.X, (float)compDef.Position.Y)
                    : _layout.GetIncrementalPosition(compDef.Id, _placed.Count);

                obj.Attributes.Pivot = position;
                doc.AddObject(obj, false);
                _placed[compDef.Id] = obj;

                OnComponentPlaced?.Invoke(compDef.Id, compDef.Name);
                TryWirePendingConnections(doc);

                // Force the canvas to redraw so the new component is immediately visible
                ForceCanvasRedraw();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                OnBuildError?.Invoke($"Failed to place {compDef.Name}: {ex.Message}");
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }

    public void AddConnection(ConnectionDef conn)
    {
        _ = AddConnectionAsync(conn);
    }

    public Task AddConnectionAsync(ConnectionDef conn)
    {
        if (_document == null) return Task.CompletedTask;

        if (!_placed.ContainsKey(conn.FromComponent) || !_placed.ContainsKey(conn.ToComponent))
        {
            _pendingWires.Enqueue(conn);
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        _document.ScheduleSolution(1, doc =>
        {
            WireConnection(conn, doc);
            ForceCanvasRedraw();
            tcs.TrySetResult(true);
        });
        return tcs.Task;
    }

    public void FinalizeBuild(ScriptDefinition script)
    {
        _ = FinalizeBuildAsync(script);
    }

    public Task FinalizeBuildAsync(ScriptDefinition script)
    {
        if (_document == null) return Task.CompletedTask;

        var tcs = new TaskCompletionSource<bool>();
        _document.ScheduleSolution(10, doc =>
        {
            // Wire any remaining pending connections
            TryWirePendingConnections(doc);

            // Refine layout now that all components are placed
            var positions = _layout.ComputeLayout(script);
            foreach (var (id, pos) in positions)
            {
                if (_placed.TryGetValue(id, out var obj))
                    obj.Attributes.Pivot = pos;
            }

            // GUARANTEE A WORKING FILE: fill any required input the model left
            // unconnected with a sensible default (origin point, XY plane, Z
            // vector, etc.) so the graph resolves with no "failed to collect
            // data" warnings.
            CanvasRepair.SatisfyRequiredInputs(_placed.Values);

            // Annotate the workflow: wrap each logical stage in a labelled,
            // tinted GH_Group. Track the created group GUIDs so an iteration
            // rebuild removes them too (otherwise old group boxes orphan).
            _groupGuids.AddRange(CanvasGrouping.Apply(doc, script, _placed));

            // Trigger a real solution so geometry actually computes and shows in Rhino
            doc.NewSolution(false);
            ForceCanvasRedraw();
            OnBuildComplete?.Invoke();
            tcs.TrySetResult(true);
        });
        return tcs.Task;
    }

    /// <summary>
    /// Force the Grasshopper canvas to redraw so the latest component placement
    /// becomes visible immediately. Without this, placements queued via
    /// ScheduleSolution may not paint until a full solution runs.
    /// </summary>
    private static void ForceCanvasRedraw()
    {
        try
        {
            // Grasshopper SDK: redraw the canvas. Works without referencing WinForms.
            Instances.RedrawCanvas();
        }
        catch
        {
            // Canvas refresh is best-effort
        }
    }

    public void BuildFromStream(ResponseParser parser, ScriptDefinition script)
    {
        parser.OnScriptComplete += s =>
        {
            foreach (var comp in s.Components)
                PlaceComponent(comp);
            foreach (var conn in s.Connections)
                AddConnection(conn);
            FinalizeBuild(s);
        };
    }

    private void TryWirePendingConnections(GH_Document doc)
    {
        var remaining = new Queue<ConnectionDef>();

        while (_pendingWires.Count > 0)
        {
            var conn = _pendingWires.Dequeue();
            if (_placed.ContainsKey(conn.FromComponent) && _placed.ContainsKey(conn.ToComponent))
                WireConnection(conn, doc);
            else
                remaining.Enqueue(conn);
        }

        while (remaining.Count > 0)
            _pendingWires.Enqueue(remaining.Dequeue());
    }

    private void WireConnection(ConnectionDef conn, GH_Document doc)
    {
        try
        {
            var sourceObj = _placed[conn.FromComponent];
            var targetObj = _placed[conn.ToComponent];

            var outputParam = GetOutputParam(sourceObj, conn.FromOutput);
            var inputParam = GetInputParam(targetObj, conn.ToInput);
            if (outputParam == null || inputParam == null)
            {
                OnBuildError?.Invoke($"Cannot resolve params for {conn.FromComponent}→{conn.ToComponent}");
                return;
            }

            inputParam.AddSource(outputParam);
            OnConnectionMade?.Invoke(conn.FromComponent, conn.ToComponent);
        }
        catch (Exception ex)
        {
            OnBuildError?.Invoke($"Failed to wire {conn.FromComponent}→{conn.ToComponent}: {ex.Message}");
        }
    }

    private static IGH_Param? GetOutputParam(IGH_DocumentObject obj, int index)
    {
        if (obj is GH_Component comp && index < comp.Params.Output.Count)
            return comp.Params.Output[index];
        if (index == 0 && obj is IGH_Param param)
            return param;
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

    public void Dispose()
    {
        _placed.Clear();
        _pendingWires.Clear();
        _groupGuids.Clear();
    }
}
