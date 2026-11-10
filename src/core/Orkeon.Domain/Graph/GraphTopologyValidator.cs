namespace Orkeon.Domain.Graph;

/// <summary>One outgoing edge of a node in a <see cref="GraphTopology"/>.</summary>
/// <param name="From">The node it leaves (or the start sentinel).</param>
/// <param name="Targets">Where it can lead; null when unknown (a script's function edge, a conditional edge declared without targets).</param>
/// <param name="IsFixed">True for an unconditional edge with exactly one target.</param>
public sealed record GraphEdgeDescription(string From, IReadOnlyList<string>? Targets, bool IsFixed);

/// <summary>The neutral description of a graph both engines, the YAML mapper and the export hand to the validator.</summary>
/// <param name="StartId">The engine's start sentinel; an edge whose source is this leaves START.</param>
/// <param name="EndId">The engine's exit sentinel.</param>
/// <param name="Nodes">The declared node ids.</param>
/// <param name="Edges">Every outgoing edge, START's included.</param>
public sealed record GraphTopology(
    string StartId,
    string EndId,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<GraphEdgeDescription> Edges);

/// <summary>
/// Checks the shape of a graph and returns every fault, not the first (ADR-018). Both engines, the
/// YAML mapper and the export call it, so a topology fault has one text wherever it surfaces.
/// </summary>
/// <remarks>
/// An edge whose targets are unknown (<see cref="GraphEdgeDescription.Targets"/> null) is assumed to
/// reach END, and, since nothing says otherwise, any declared node: a node reachable only through
/// such an edge is not an orphan. A node whose only way out is such an edge is never reported as
/// unable to reach END.
/// </remarks>
public static class GraphTopologyValidator
{
    /// <summary>Returns every fault of <paramref name="topology"/>; empty when it is sound.</summary>
    public static IReadOnlyList<string> Validate(GraphTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var faults = new List<string>();
        var declared = CheckNodeIds(topology, faults);
        var outgoing = CheckEdges(topology, declared, faults);

        foreach (var node in declared.Where(n => !outgoing.ContainsKey(n)))
            faults.Add($"Node '{node}' has no outgoing edge. Add an edge or route to END.");

        CheckReachability(topology, declared, outgoing, faults);
        return faults;
    }

    private static HashSet<string> CheckNodeIds(GraphTopology topology, List<string> faults)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in topology.Nodes)
        {
            if (string.IsNullOrWhiteSpace(id))
                faults.Add("A node id is empty.");
            else if (id == topology.StartId || id == topology.EndId)
                faults.Add($"Node id '{id}' is a reserved sentinel (START or END).");
            else if (!declared.Add(id))
                faults.Add($"Node '{id}' is declared twice.");
        }

        return declared;
    }

    private static Dictionary<string, List<GraphEdgeDescription>> CheckEdges(
        GraphTopology topology, HashSet<string> declared, List<string> faults)
    {
        var known = string.Join(", ", topology.Nodes.Where(n => !string.IsNullOrWhiteSpace(n)));
        var outgoing = new Dictionary<string, List<GraphEdgeDescription>>(StringComparer.Ordinal);
        foreach (var edge in topology.Edges)
        {
            if (edge.From != topology.StartId && !declared.Contains(edge.From))
                faults.Add($"Edge starts at unknown node '{edge.From}' (declared: {known}).");
            if (!outgoing.TryGetValue(edge.From, out var list))
                outgoing[edge.From] = list = [];
            list.Add(edge);

            foreach (var target in edge.Targets ?? [])
            {
                if (target != topology.EndId && !declared.Contains(target))
                    faults.Add($"Edge from '{edge.From}' targets unknown node '{target}' (declared: {known}).");
            }
        }

        if (!outgoing.ContainsKey(topology.StartId))
            faults.Add("Graph has no edge from START.");

        foreach (var (from, edges) in outgoing.Where(p => p.Value.Count > 1 && p.Value.Any(e => e.IsFixed)))
            faults.Add($"Node '{DisplayOf(topology, from)}' has a fixed outgoing edge and {edges.Count - 1} more: a fixed edge excludes any other.");

        return outgoing;
    }

    private static string DisplayOf(GraphTopology topology, string from) => from == topology.StartId ? "START" : from;

    private static void CheckReachability(
        GraphTopology topology,
        HashSet<string> declared,
        Dictionary<string, List<GraphEdgeDescription>> outgoing,
        List<string> faults)
    {
        var fromStart = Reach(topology.StartId, declared, outgoing, topology.EndId);
        foreach (var node in topology.Nodes.Where(n => declared.Contains(n) && !fromStart.Contains(n)).Distinct(StringComparer.Ordinal))
            faults.Add($"Node '{node}' is an orphan: nothing reaches it from START.");

        if (!outgoing.ContainsKey(topology.StartId))
            return;

        var reachesEnd = ReachEnd(topology, outgoing);
        if (!reachesEnd.Contains(topology.StartId))
        {
            faults.Add("Graph has no path from START to END.");
            return;
        }

        foreach (var node in fromStart.Where(n => outgoing.ContainsKey(n) && declared.Contains(n) && !reachesEnd.Contains(n)).Order(StringComparer.Ordinal))
            faults.Add($"Node '{node}' cannot reach END.");
    }

    /// <summary>Every node reachable from <paramref name="origin"/> following every edge.</summary>
    private static HashSet<string> Reach(
        string origin,
        HashSet<string> declared,
        Dictionary<string, List<GraphEdgeDescription>> outgoing,
        string endId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { origin };
        var queue = new Queue<string>();
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!outgoing.TryGetValue(node, out var edges))
                continue;
            var targets = edges
                .SelectMany(edge => (IEnumerable<string>?)edge.Targets ?? declared)
                .Where(t => t != endId && declared.Contains(t));
            foreach (var target in targets.Where(seen.Add))
                queue.Enqueue(target);
        }

        return seen;
    }

    /// <summary>Every node (START included) from which END can be reached: a fixpoint over the edges.</summary>
    private static HashSet<string> ReachEnd(
        GraphTopology topology,
        Dictionary<string, List<GraphEdgeDescription>> outgoing)
    {
        var good = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var (from, edges) in outgoing)
            {
                if (good.Contains(from))
                    continue;
                if (edges.Any(e => e.Targets is null || e.Targets.Any(t => t == topology.EndId || good.Contains(t))))
                    changed |= good.Add(from);
            }
        }
        while (changed);

        return good;
    }
}
