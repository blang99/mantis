using System.Drawing;

namespace Mantis.Plugin.ScriptBuilder;

public class LayoutEngine
{
    private const float ColumnSpacing = 200f;
    private const float RowSpacing = 120f;
    private const float StartX = 100f;
    private const float StartY = 100f;

    public Dictionary<int, PointF> ComputeLayout(ScriptDefinition script)
    {
        var positions = new Dictionary<int, PointF>();
        if (script.Components.Count == 0) return positions;

        var depths = ComputeDepths(script);
        var columns = new Dictionary<int, List<int>>();

        foreach (var (compId, depth) in depths)
        {
            if (!columns.ContainsKey(depth))
                columns[depth] = new List<int>();
            columns[depth].Add(compId);
        }

        foreach (var (depth, compIds) in columns)
        {
            var totalHeight = (compIds.Count - 1) * RowSpacing;
            var startY = StartY - totalHeight / 2;

            for (int i = 0; i < compIds.Count; i++)
            {
                positions[compIds[i]] = new PointF(
                    StartX + depth * ColumnSpacing,
                    startY + i * RowSpacing
                );
            }
        }

        return positions;
    }

    public PointF GetIncrementalPosition(int componentId, int placedCount, int estimatedColumn = -1)
    {
        int col = estimatedColumn >= 0 ? estimatedColumn : placedCount;
        return new PointF(
            StartX + col * ColumnSpacing,
            StartY + (placedCount % 5) * RowSpacing
        );
    }

    public void RefineLayout(Dictionary<int, PointF> positions, ScriptDefinition script)
    {
        var depths = ComputeDepths(script);
        var columns = new Dictionary<int, List<int>>();

        foreach (var (compId, depth) in depths)
        {
            if (!columns.ContainsKey(depth))
                columns[depth] = new List<int>();
            columns[depth].Add(compId);
        }

        foreach (var (depth, compIds) in columns)
        {
            var totalHeight = (compIds.Count - 1) * RowSpacing;
            var startY = StartY - totalHeight / 2;

            for (int i = 0; i < compIds.Count; i++)
            {
                positions[compIds[i]] = new PointF(
                    StartX + depth * ColumnSpacing,
                    startY + i * RowSpacing
                );
            }
        }
    }

    private Dictionary<int, int> ComputeDepths(ScriptDefinition script)
    {
        var adjacency = new Dictionary<int, List<int>>();
        var inDegree = new Dictionary<int, int>();

        foreach (var comp in script.Components)
        {
            adjacency[comp.Id] = new List<int>();
            inDegree[comp.Id] = 0;
        }

        foreach (var conn in script.Connections)
        {
            if (adjacency.ContainsKey(conn.FromComponent) && inDegree.ContainsKey(conn.ToComponent))
            {
                adjacency[conn.FromComponent].Add(conn.ToComponent);
                inDegree[conn.ToComponent]++;
            }
        }

        var depths = new Dictionary<int, int>();
        var queue = new Queue<int>();

        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
            {
                queue.Enqueue(id);
                depths[id] = 0;
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in adjacency[current])
            {
                var newDepth = depths[current] + 1;
                if (!depths.ContainsKey(neighbor) || newDepth > depths[neighbor])
                    depths[neighbor] = newDepth;

                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        // Assign depth 0 to any components not reached (disconnected)
        foreach (var comp in script.Components)
        {
            if (!depths.ContainsKey(comp.Id))
                depths[comp.Id] = 0;
        }

        return depths;
    }
}
