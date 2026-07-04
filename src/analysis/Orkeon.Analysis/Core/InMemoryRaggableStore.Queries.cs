using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Read side of <see cref="InMemoryRaggableStore"/>: lookups, filtered queries,
/// semantic search and the delegations to the extracted collaborators
/// (<see cref="GraphTraversal"/>, <see cref="StoreAnalytics"/>, <see cref="SourceSliceReader"/>).
/// </summary>
public sealed partial class InMemoryRaggableStore
{
    public Task<RaggableNode?> GetAsync(string fqn, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(fqn);
        if (_nodesByFqn.TryGetValue(fqn, out var node)) return Task.FromResult<RaggableNode?>(node);
        if (_nodesById.TryGetValue(fqn, out var byId)) return Task.FromResult<RaggableNode?>(byId);
        return Task.FromResult<RaggableNode?>(null);
    }

    public Task<IReadOnlyList<RaggableNode>> FindByLocalNameAsync(string localName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(localName);

        var matches = new List<RaggableNode>();
        foreach (var node in _nodesByFqn.Values)
        {
            ct.ThrowIfCancellationRequested();
            var fqn = node.Fqn;
            if (string.IsNullOrEmpty(fqn)) continue;

            var lastSep = fqn.LastIndexOf("::", StringComparison.Ordinal);
            if (lastSep < 0) continue;

            var tail = fqn.AsSpan(lastSep + 2);
            if (tail.SequenceEqual(localName)) matches.Add(node);
        }

        return Task.FromResult<IReadOnlyList<RaggableNode>>(matches);
    }

    public Task<IReadOnlyList<RaggableNode>> GetManyAsync(IEnumerable<string> fqns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fqns);
        var result = new List<RaggableNode>();
        foreach (var fqn in fqns)
        {
            if (string.IsNullOrEmpty(fqn)) continue;
            if (_nodesByFqn.TryGetValue(fqn, out var n)) result.Add(n);
            else if (_nodesById.TryGetValue(fqn, out var byId)) result.Add(byId);
        }
        return Task.FromResult<IReadOnlyList<RaggableNode>>(result);
    }

    public Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<RaggableNode> q = _nodesById.Values;

        if (query.Level is { } level) q = q.Where(n => n.Level == level);
        if (query.Kinds is { Length: > 0 } kinds) q = q.Where(n => kinds.Contains(n.EffectiveKind));
        if (query.Languages is { Length: > 0 } langs) q = q.Where(n => langs.Contains(n.Language, StringComparer.OrdinalIgnoreCase));
        if (query.Packages is { Length: > 0 } packages)
        {
            q = q.Where(n => packages.Any(p => n.Fqn.Contains(p, StringComparison.Ordinal)));
        }
        if (!string.IsNullOrEmpty(query.ParentId)) q = q.Where(n => n.ParentId == query.ParentId);
        if (query.Tags is { Count: > 0 } tags)
        {
            q = q.Where(n => tags.All(t => n.Tags.TryGetValue(t.Key, out var v) && string.Equals(v, t.Value, StringComparison.Ordinal)));
        }
        if (query.MinComplexity is { } min)
        {
            q = q.Where(n => (n.Body?.CyclomaticComplexity ?? 0) >= min);
        }

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 1000);
        var list = q.OrderBy(n => n.Fqn, StringComparer.Ordinal).Skip(skip).Take(take).ToList();
        return Task.FromResult<IReadOnlyList<RaggableNode>>(list);
    }

    public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentId);
        var parent = Resolve(parentId);
        if (parent is null) return Task.FromResult<IReadOnlyList<RaggableNode>>([]);
        var children = parent.ChildrenIds
            .Select(id => _nodesById.TryGetValue(id, out var c) ? c : null)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
        return Task.FromResult<IReadOnlyList<RaggableNode>>(children);
    }

    public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentSymbolId);
        if (_statementsByParent.TryGetValue(parentSymbolId, out var stmts))
        {
            return Task.FromResult<IReadOnlyList<StatementNode>>(stmts);
        }
        var node = Resolve(parentSymbolId);
        if (node is not null && node.Statements.Count > 0)
        {
            return Task.FromResult<IReadOnlyList<StatementNode>>(Flatten(node.Statements));
        }
        return Task.FromResult<IReadOnlyList<StatementNode>>([]);
    }

    public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        RaggableNode? match = null;
        foreach (var node in _nodesById.Values)
        {
            if (node.Level != NodeLevel.L2_Module) continue;
            if (!string.Equals(node.VirtualFilePath, filePath, StringComparison.Ordinal)) continue;
            match = node;
            break;
        }
        return Task.FromResult(match);
    }

    public Task<int> GetNodeCountAsync(CancellationToken ct)
        => Task.FromResult(_nodesById.Count);

    public Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(fqn);
        var node = Resolve(fqn);
        if (node is null) return Task.FromResult<IReadOnlyList<RaggableEdge>>([]);

        var edges = new List<RaggableEdge>();
        if (dir is Direction.Forward or Direction.Both
            && _edgesBySource.TryGetValue(node.Id, out var outList))
        {
            edges.AddRange(outList.Where(e => kinds == EdgeKind.None || kinds.HasFlag(e.Kind)));
        }
        if (dir is Direction.Backward or Direction.Both
            && _edgesByTarget.TryGetValue(node.Id, out var inList))
        {
            edges.AddRange(inList.Where(e => kinds == EdgeKind.None || kinds.HasFlag(e.Kind)));
        }
        return Task.FromResult<IReadOnlyList<RaggableEdge>>(edges);
    }

    public Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct)
        => Task.FromResult(_graph.Expand(query, ct));

    public Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct)
        => Task.FromResult(_graph.FindAllPaths(query, ct));

    public Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct)
        => Task.FromResult(_graph.ShortestPath(query, ct));

    public Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct)
        => Task.FromResult(_graph.FindCycles(query, ct));

    public Task<IReadOnlyList<SearchHit>> SemanticSearchAsync(SemanticQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SemanticSearchCoreAsync(query, ct);
    }

    private async Task<IReadOnlyList<SearchHit>> SemanticSearchCoreAsync(SemanticQuery query, CancellationToken ct)
    {
        var topK = Math.Clamp(query.TopK, 1, MaxTopK);

        if (_queryEmbedder is null) return [];
        var queryVec = await _queryEmbedder(query.Text, ct).ConfigureAwait(false);
        if (queryVec is null || queryVec.Value.Length == 0) return [];

        IEnumerable<RaggableNode> candidates = _nodesById.Values.Where(n => n.Embedding is not null);
        if (query.PreFilter is not null)
        {
            var filtered = await QueryAsync(query.PreFilter, ct).ConfigureAwait(false);
            var allowed = new HashSet<string>(filtered.Select(n => n.Id), StringComparer.Ordinal);
            candidates = candidates.Where(n => allowed.Contains(n.Id));
        }

        var scored = new List<(RaggableNode node, double score)>();
        foreach (var node in candidates)
        {
            var score = Cosine(queryVec.Value.Span, node.Embedding!.Value.Span);
            if (score < query.MinScore) continue;
            scored.Add((node, score));
        }

        return [.. scored
            .OrderByDescending(x => x.score)
            .Take(topK)
            .Select(x => new SearchHit
            {
                Fqn = x.node.Fqn,
                Score = x.score,
                SummaryShort = x.node.SemanticSummary,
                Signature = x.node.Signature,
            })];
    }

    public async Task<SourceSlice?> GetSourceAsync(string fqn, SourceMode mode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(fqn);
        var node = Resolve(fqn);
        if (node is null) return null;
        return await _sourceReader.ReadAsync(node, mode, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ComplexityEntry>> TopComplexityAsync(ComplexityQuery query, CancellationToken ct)
        => Task.FromResult(_analytics.TopComplexity(query));

    public Task<IReadOnlyList<CentralityEntry>> TopCentralityAsync(CentralityQuery query, CancellationToken ct)
        => Task.FromResult(_analytics.TopCentrality(query));

    public IReadOnlyList<IndexedRoot> GetIndexedRoots()
        => _analytics.GetIndexedRoots();

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
