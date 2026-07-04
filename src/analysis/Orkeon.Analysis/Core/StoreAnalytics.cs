using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Read-only analytics over the RaggableTree node/edge index: complexity ranking,
/// centrality ranking and indexed-root inventory. Extracted from
/// <see cref="InMemoryRaggableStore"/> (R4.2 god-file decomposition) so the reporting
/// logic is testable in isolation.
/// </summary>
/// <remarks>
/// The instance references the same dictionary instances owned by the store. Those
/// dictionaries are cleared in place (never reassigned), so a single analytics instance
/// remains valid across <c>Replace</c>/<c>AddNodes</c> mutations.
/// </remarks>
public sealed class StoreAnalytics
{
    private readonly IReadOnlyDictionary<string, RaggableNode> _nodesById;
    private readonly IReadOnlyDictionary<string, List<RaggableEdge>> _edgesBySource;
    private readonly IReadOnlyDictionary<string, DateTimeOffset> _indexedAtByRoot;
    private readonly GraphTraversal _graph;
    private readonly Func<string, RaggableNode?> _resolve;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _maxNodes;

    public StoreAnalytics(
        IReadOnlyDictionary<string, RaggableNode> nodesById,
        IReadOnlyDictionary<string, List<RaggableEdge>> edgesBySource,
        IReadOnlyDictionary<string, DateTimeOffset> indexedAtByRoot,
        GraphTraversal graph,
        Func<string, RaggableNode?> resolve,
        Func<DateTimeOffset> clock,
        int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(nodesById);
        ArgumentNullException.ThrowIfNull(edgesBySource);
        ArgumentNullException.ThrowIfNull(indexedAtByRoot);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(clock);
        _nodesById = nodesById;
        _edgesBySource = edgesBySource;
        _indexedAtByRoot = indexedAtByRoot;
        _graph = graph;
        _resolve = resolve;
        _clock = clock;
        _maxNodes = maxNodes;
    }

    /// <summary>Returns the top-N nodes ranked by the requested complexity metric.</summary>
    public IReadOnlyList<ComplexityEntry> TopComplexity(ComplexityQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var topN = Math.Clamp(query.TopN, 1, _maxNodes);
        IEnumerable<RaggableNode> candidates = _nodesById.Values;
        if (!string.IsNullOrEmpty(query.RootFqn))
        {
            var root = _resolve(query.RootFqn);
            if (root is not null) candidates = candidates.Where(n => n.Fqn.StartsWith(root.Fqn, StringComparison.Ordinal));
        }

        return candidates
            .Select(n => (node: n, value: ExtractComplexity(n, query.Metric)))
            .Where(x => x.value > 0)
            .OrderByDescending(x => x.value)
            .Take(topN)
            .Select(x => new ComplexityEntry(x.node.Fqn, x.value, query.Metric))
            .ToList();
    }

    /// <summary>Returns the top-N nodes ranked by the requested centrality metric.</summary>
    public IReadOnlyList<CentralityEntry> TopCentrality(CentralityQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var topN = Math.Clamp(query.TopN, 1, _maxNodes);
        var scoped = _nodesById.Values.Where(n => n.Level == query.Scope).ToList();

        double Score(RaggableNode node) => query.Metric switch
        {
            CentralityMetric.InDegreeCalls => CountEdges(node.Id, EdgeKind.Calls, Direction.Backward),
            CentralityMetric.OutDegreeCalls => CountEdges(node.Id, EdgeKind.Calls, Direction.Forward),
            CentralityMetric.InDegreeImports => CountEdges(node.Id, EdgeKind.Imports, Direction.Backward),
            CentralityMetric.Betweenness => ApproximateBetweenness(node, scoped),
            CentralityMetric.PageRank => SimplifiedPageRank(node),
            _ => 0.0,
        };

        return scoped
            .Select(n => (node: n, score: Score(n)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(topN)
            .Select(x => new CentralityEntry(x.node.Fqn, x.score, query.Metric))
            .ToList();
    }

    /// <summary>Builds the inventory of indexed monorepo roots (node/edge counts, language summary).</summary>
    public IReadOnlyList<IndexedRoot> GetIndexedRoots()
    {
        var monorepos = _nodesById.Values
            .Where(n => n.Level == NodeLevel.L0_Monorepo && !string.IsNullOrEmpty(n.VirtualFilePath))
            .ToList();
        if (monorepos.Count == 0) return [];

        var roots = new List<IndexedRoot>(monorepos.Count);
        foreach (var monorepo in monorepos)
        {
            var descendants = CollectDescendants(monorepo.Id);
            var nodeCount = descendants.Count;
            var edgeCount = 0;
            foreach (var id in descendants)
            {
                if (_edgesBySource.TryGetValue(id, out var outList)) edgeCount += outList.Count;
            }

            var languageSummary = BuildLanguageSummary(descendants);
            var indexedAt = _indexedAtByRoot.TryGetValue(monorepo.VirtualFilePath, out var ts)
                ? ts
                : _clock();
            roots.Add(new IndexedRoot(monorepo.VirtualFilePath, indexedAt, nodeCount, edgeCount, languageSummary));
        }
        return roots;
    }

    private HashSet<string> CollectDescendants(string rootId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;
            if (!_nodesById.TryGetValue(current, out var node)) continue;
            foreach (var childId in node.ChildrenIds) stack.Push(childId);
        }
        return visited;
    }

    private string? BuildLanguageSummary(HashSet<string> descendants)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in descendants)
        {
            if (!_nodesById.TryGetValue(id, out var node)) continue;
            if (node.Level != NodeLevel.L2_Module) continue;
            if (string.IsNullOrEmpty(node.Language)) continue;
            counts[node.Language] = counts.GetValueOrDefault(node.Language) + 1;
        }
        if (counts.Count == 0) return null;
        return string.Join(", ", counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}: {kv.Value} file{(kv.Value == 1 ? "" : "s")}"));
    }

    private int CountEdges(string nodeId, EdgeKind kind, Direction dir)
        => _graph.CountEdges(nodeId, kind, dir);

    private double ApproximateBetweenness(RaggableNode node, List<RaggableNode> scope)
    {
        var inCount = CountEdges(node.Id, EdgeKind.Calls, Direction.Backward);
        var outCount = CountEdges(node.Id, EdgeKind.Calls, Direction.Forward);
        if (scope.Count == 0) return 0;
        return (double)inCount * outCount / Math.Max(1, scope.Count - 1);
    }

    private double SimplifiedPageRank(RaggableNode node)
    {
        var inCount = CountEdges(node.Id, EdgeKind.Calls, Direction.Backward);
        var outCount = CountEdges(node.Id, EdgeKind.Calls, Direction.Forward);
        return 0.15 + 0.85 * (inCount / Math.Max(1.0, outCount + 1));
    }

    private static double ExtractComplexity(RaggableNode node, ComplexityMetric metric)
    {
        var body = node.Body;
        return metric switch
        {
            ComplexityMetric.Cyclomatic => body?.CyclomaticComplexity ?? 0,
            ComplexityMetric.NestingDepth => body?.MaxDepth ?? 0,
            ComplexityMetric.FanOut => body?.CallCount ?? 0,
            ComplexityMetric.LoC => Math.Max(0, node.Range.EndLine - node.Range.StartLine + 1),
            ComplexityMetric.Callers => node.CalledByIds.Count,
            _ => 0,
        };
    }
}
