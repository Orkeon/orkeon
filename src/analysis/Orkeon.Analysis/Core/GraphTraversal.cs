using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Graph traversal engine over the RaggableTree node/edge index.
/// Owns the BFS/DFS/Tarjan algorithms used by the store for expansion, path finding,
/// shortest path and cycle detection. Extracted from <see cref="InMemoryRaggableStore"/>
/// so the traversal logic is testable in isolation.
/// </summary>
/// <remarks>
/// The instance references the same dictionary instances owned by the store. Those
/// dictionaries are cleared in place (never reassigned), so a single traversal instance
/// remains valid across <c>Replace</c>/<c>AddNodes</c> mutations.
/// </remarks>
public sealed class GraphTraversal
{
    private readonly IReadOnlyDictionary<string, RaggableNode> _nodesById;
    private readonly IReadOnlyDictionary<string, List<RaggableEdge>> _edgesBySource;
    private readonly IReadOnlyDictionary<string, List<RaggableEdge>> _edgesByTarget;
    private readonly Func<string, RaggableNode?> _resolve;
    private readonly int _maxDepth;
    private readonly int _maxNodes;

    public GraphTraversal(
        IReadOnlyDictionary<string, RaggableNode> nodesById,
        IReadOnlyDictionary<string, List<RaggableEdge>> edgesBySource,
        IReadOnlyDictionary<string, List<RaggableEdge>> edgesByTarget,
        Func<string, RaggableNode?> resolve,
        int maxDepth,
        int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(nodesById);
        ArgumentNullException.ThrowIfNull(edgesBySource);
        ArgumentNullException.ThrowIfNull(edgesByTarget);
        ArgumentNullException.ThrowIfNull(resolve);
        _nodesById = nodesById;
        _edgesBySource = edgesBySource;
        _edgesByTarget = edgesByTarget;
        _resolve = resolve;
        _maxDepth = maxDepth;
        _maxNodes = maxNodes;
    }

    public SubGraph Expand(ExpandQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var maxDepth = Math.Clamp(query.MaxDepth, 1, _maxDepth);
        var maxNodes = Math.Clamp(query.MaxNodes, 1, _maxNodes);
        var kindMask = CombineKinds(query.EdgeKinds);

        var state = new BfsTraversalState();
        var truncated = false;

        foreach (var seed in query.Seeds)
        {
            var seedNode = _resolve(seed);
            if (seedNode is null) continue;
            if (state.Visited.TryAdd(seedNode.Id, seedNode))
            {
                state.Queue.Enqueue((seedNode.Id, 0));
            }
        }

        while (state.Queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (currentId, depth) = state.Queue.Dequeue();
            if (depth >= maxDepth) continue;

            if (ExpandNeighbors(query, currentId, depth, maxNodes, kindMask, state))
            {
                truncated = true;
                break;
            }
        }

        return new SubGraph
        {
            Nodes = [.. state.Visited.Values],
            Edges = [.. state.CollectedEdges],
            Truncated = truncated,
        };
    }

    /// <summary>
    /// Visits the neighbors of <paramref name="currentId"/>, recording edges and enqueueing
    /// unvisited nodes. Returns <see langword="true"/> when the node budget is exhausted
    /// (the caller must then stop the traversal).
    /// </summary>
    private bool ExpandNeighbors(
        ExpandQuery query,
        string currentId,
        int depth,
        int maxNodes,
        EdgeKind kindMask,
        BfsTraversalState state)
    {
        foreach (var edge in CollectEdges(currentId, kindMask, query.Direction))
        {
            if (state.Visited.Count >= maxNodes) return true;
            if (state.EdgesSet.Add(edge.Id)) state.CollectedEdges.Add(edge);

            var neighborId = query.Direction == Direction.Backward ? edge.FromId : edge.ToId;
            if (_nodesById.TryGetValue(neighborId, out var neighbor)
                && state.Visited.TryAdd(neighbor.Id, neighbor))
            {
                state.Queue.Enqueue((neighbor.Id, depth + 1));
            }
        }
        return false;
    }

    /// <summary>
    /// Mutable BFS accumulators threaded through <see cref="ExpandNeighbors"/> during an
    /// <see cref="Expand"/> traversal: the visited-node map, the seen-edge set, the collected
    /// edges, and the frontier queue. A class (not a record) because these are mutated in place.
    /// </summary>
    private sealed class BfsTraversalState
    {
        public Dictionary<string, RaggableNode> Visited { get; } = new(StringComparer.Ordinal);
        public HashSet<string> EdgesSet { get; } = new(StringComparer.Ordinal);
        public List<RaggableEdge> CollectedEdges { get; } = new();
        public Queue<(string id, int depth)> Queue { get; } = new();
    }

    public IReadOnlyList<CallPath> FindAllPaths(PathQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var from = _resolve(query.From);
        if (from is null) return [];

        var state = new PathSearchState(
            query,
            maxDepth: Math.Clamp(query.MaxDepth, 1, _maxDepth),
            maxPaths: Math.Clamp(query.MaxPaths, 1, _maxNodes),
            kindMask: CombineKinds(query.EdgeKinds),
            toId: query.To is null ? null : _resolve(query.To)?.Id,
            fromId: from.Id,
            cancellationToken: ct);

        DfsPaths(state, from.Id, 0);
        return state.Results;
    }

    /// <summary>
    /// Depth-first path search step. Records a completed path when the current node is the
    /// target (or matches the leaf predicate), then recurses into unvisited neighbors with
    /// backtracking. Extracted from <see cref="FindAllPaths"/> to keep that method's cognitive
    /// complexity manageable; behavior is identical (same order, short-circuiting, backtracking).
    /// </summary>
    private void DfsPaths(PathSearchState state, string currentId, int depth)
    {
        if (state.Results.Count >= state.MaxPaths) return;
        state.CancellationToken.ThrowIfCancellationRequested();

        var reachedTarget = state.ToId is not null && currentId == state.ToId && depth > 0;
        if (reachedTarget || IsLeafMatch(state.Query, state.ToId, currentId, depth))
        {
            state.Results.Add(BuildPath(state.Stack, state.CallSites));
            if (reachedTarget) return;
        }

        if (depth >= state.MaxDepth) return;

        foreach (var edge in CollectEdges(currentId, state.KindMask, state.Query.Direction))
        {
            var next = state.Query.Direction == Direction.Backward ? edge.FromId : edge.ToId;
            if (!state.Visited.Add(next)) continue;
            state.Stack.Add(next);
            state.CallSites.Add(edge.CallSite);
            DfsPaths(state, next, depth + 1);
            state.Stack.RemoveAt(state.Stack.Count - 1);
            state.CallSites.RemoveAt(state.CallSites.Count - 1);
            state.Visited.Remove(next);
        }
    }

    /// <summary>Mutable working set threaded through <see cref="DfsPaths"/> during a path search.</summary>
    private sealed class PathSearchState
    {
        public PathSearchState(
            PathQuery query,
            int maxDepth,
            int maxPaths,
            EdgeKind kindMask,
            string? toId,
            string fromId,
            CancellationToken cancellationToken)
        {
            Query = query;
            CancellationToken = cancellationToken;
            MaxDepth = maxDepth;
            MaxPaths = maxPaths;
            KindMask = kindMask;
            ToId = toId;
            Stack = new List<string> { fromId };
            CallSites = new List<SourceLocation?> { null };
            Visited = new HashSet<string>(StringComparer.Ordinal) { fromId };
        }

        public PathQuery Query { get; }
        public CancellationToken CancellationToken { get; }
        public int MaxDepth { get; }
        public int MaxPaths { get; }
        public EdgeKind KindMask { get; }
        public string? ToId { get; }
        public List<CallPath> Results { get; } = new();
        public List<string> Stack { get; }
        public List<SourceLocation?> CallSites { get; }
        public HashSet<string> Visited { get; }
    }

    private bool IsLeafMatch(PathQuery query, string? toId, string currentId, int depth)
        => toId is null && query.LeafPredicate is not null && depth > 0
           && _nodesById.TryGetValue(currentId, out var currentNode)
           && query.LeafPredicate(currentNode);

    public CallPath? ShortestPath(ShortestPathQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var from = _resolve(query.From);
        var to = _resolve(query.To);
        if (from is null || to is null) return null;
        if (from.Id == to.Id) return BuildPath([from.Id], [null]);

        var kindMask = CombineKinds(query.EdgeKinds);
        var prev = new Dictionary<string, (string prevId, SourceLocation? site)>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(from.Id);
        var visited = new HashSet<string>(StringComparer.Ordinal) { from.Id };

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var cur = queue.Dequeue();
            if (cur == to.Id) break;
            foreach (var edge in CollectEdges(cur, kindMask, query.Direction))
            {
                var next = query.Direction == Direction.Backward ? edge.FromId : edge.ToId;
                if (!visited.Add(next)) continue;
                prev[next] = (cur, edge.CallSite);
                queue.Enqueue(next);
            }
        }

        if (!prev.ContainsKey(to.Id)) return null;

        return ReconstructPath(from.Id, to.Id, prev);
    }

    private static CallPath ReconstructPath(
        string fromId,
        string toId,
        Dictionary<string, (string prevId, SourceLocation? site)> prev)
    {
        var pathIds = new List<string>();
        var pathSites = new List<SourceLocation?>();
        var current = toId;
        while (current != fromId)
        {
            var (pId, site) = prev[current];
            pathIds.Add(current);
            pathSites.Add(site);
            current = pId;
        }
        pathIds.Add(fromId);
        pathSites.Add(null);
        pathIds.Reverse();
        pathSites.Reverse();
        return BuildPath(pathIds, pathSites);
    }

    public IReadOnlyList<Cycle> FindCycles(CycleQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var kindMask = CombineKinds(query.EdgeKinds);
        var primaryKind = query.EdgeKinds.Length > 0 ? query.EdgeKinds[0] : EdgeKind.Calls;
        var nodesInScope = _nodesById.Values.Where(n => n.Level == query.Scope).Select(n => n.Id).ToList();

        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowlinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var cycles = new List<Cycle>();

        void StrongConnect(string v)
        {
            indices[v] = index;
            lowlinks[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);

            foreach (var w in CollectEdges(v, kindMask, Direction.Forward)
                .Select(edge => edge.ToId)
                .Where(w => _nodesById.TryGetValue(w, out var wn) && wn.Level == query.Scope))
            {
                if (!indices.TryGetValue(w, out var indexW))
                {
                    StrongConnect(w);
                    lowlinks[v] = Math.Min(lowlinks[v], lowlinks[w]);
                }
                else if (onStack.Contains(w))
                {
                    lowlinks[v] = Math.Min(lowlinks[v], indexW);
                }
            }

            if (lowlinks[v] == indices[v])
            {
                EmitScc(v, stack, onStack, query.MinLength, primaryKind, cycles);
            }
        }

        foreach (var v in nodesInScope)
        {
            ct.ThrowIfCancellationRequested();
            if (!indices.ContainsKey(v)) StrongConnect(v);
        }

        return cycles;
    }

    private void EmitScc(
        string root,
        Stack<string> stack,
        HashSet<string> onStack,
        int minLength,
        EdgeKind primaryKind,
        List<Cycle> cycles)
    {
        var scc = new List<string>();
        string w;
        do
        {
            w = stack.Pop();
            onStack.Remove(w);
            scc.Add(w);
        } while (w != root);

        if (scc.Count < minLength) return;

        var fqns = scc.Select(id => _nodesById[id].Fqn).Where(f => !string.IsNullOrEmpty(f)).ToImmutableArray();
        cycles.Add(new Cycle { Fqns = fqns, EdgeKind = primaryKind });
    }

    public int CountEdges(string nodeId, EdgeKind kind, Direction dir)
    {
        var count = 0;
        foreach (var _ in CollectEdges(nodeId, kind, dir)) count++;
        return count;
    }

    private IEnumerable<RaggableEdge> CollectEdges(string nodeId, EdgeKind kindMask, Direction dir)
    {
        if (dir is Direction.Forward or Direction.Both
            && _edgesBySource.TryGetValue(nodeId, out var outList))
        {
            foreach (var edge in FilterByKind(outList, kindMask)) yield return edge;
        }
        if (dir is Direction.Backward or Direction.Both
            && _edgesByTarget.TryGetValue(nodeId, out var inList))
        {
            foreach (var edge in FilterByKind(inList, kindMask)) yield return edge;
        }
    }

    private static IEnumerable<RaggableEdge> FilterByKind(List<RaggableEdge> edges, EdgeKind kindMask)
    {
        foreach (var edge in edges)
        {
            if (kindMask == EdgeKind.None || kindMask.HasFlag(edge.Kind)) yield return edge;
        }
    }

    private static EdgeKind CombineKinds(ImmutableArray<EdgeKind> kinds)
    {
        if (kinds.IsDefaultOrEmpty) return EdgeKind.None;
        var result = EdgeKind.None;
        foreach (var k in kinds) result |= k;
        return result;
    }

    private static CallPath BuildPath(List<string> idPath, List<SourceLocation?> callSites)
    {
        var fqns = idPath.ToImmutableArray();
        var sites = callSites
            .Where(s => s is not null)
            .Select(s => s!)
            .ToImmutableArray();
        return new CallPath
        {
            Fqns = fqns,
            Length = Math.Max(0, idPath.Count - 1),
            CallSites = sites,
        };
    }
}
