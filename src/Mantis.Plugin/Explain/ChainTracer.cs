using Grasshopper.Kernel;

namespace Mantis.Plugin.Explain;

public class ChainTracer
{
    public List<IGH_DocumentObject> TraceUpstream(IGH_DocumentObject start, int maxDepth = 5)
    {
        var chain = new List<IGH_DocumentObject>();
        var visited = new HashSet<Guid>();
        TraceDirection(start, maxDepth, chain, visited, upstream: true);
        chain.Reverse();
        return chain;
    }

    public List<IGH_DocumentObject> TraceDownstream(IGH_DocumentObject start, int maxDepth = 5)
    {
        var chain = new List<IGH_DocumentObject>();
        var visited = new HashSet<Guid>();
        TraceDirection(start, maxDepth, chain, visited, upstream: false);
        return chain;
    }

    public List<IGH_DocumentObject> TraceFullChain(IGH_DocumentObject start, int maxDepth = 5)
    {
        var upstream = TraceUpstream(start, maxDepth);
        var downstream = TraceDownstream(start, maxDepth);

        var chain = new List<IGH_DocumentObject>(upstream);
        chain.Add(start);
        chain.AddRange(downstream);
        return chain;
    }

    private static void TraceDirection(
        IGH_DocumentObject current, int depth,
        List<IGH_DocumentObject> chain, HashSet<Guid> visited,
        bool upstream)
    {
        if (depth <= 0) return;
        if (!visited.Add(current.InstanceGuid)) return;

        if (current is GH_Component comp)
        {
            var paramSets = upstream ? comp.Params.Input : comp.Params.Output.Cast<IGH_Param>();

            foreach (var param in paramSets)
            {
                var linked = upstream ? param.Sources : param.Recipients;
                foreach (var linkedParam in linked)
                {
                    var owner = linkedParam.Attributes?.GetTopLevel?.DocObject;
                    if (owner == null) continue;
                    if (visited.Contains(owner.InstanceGuid)) continue;

                    chain.Add(owner);
                    TraceDirection(owner, depth - 1, chain, visited, upstream);
                }
            }
        }
    }
}
