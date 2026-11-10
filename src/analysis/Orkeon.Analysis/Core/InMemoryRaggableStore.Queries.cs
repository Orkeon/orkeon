using System.Collections.Immutable;
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
        return Task.FromResult(ReadLocked<RaggableNode?>(() =>
        {
            if (_nodesByFqn.TryGetValue(fqn, out var node)) return node;
            if (_nodesById.TryGetValue(fqn, out var byId)) return byId;
            return null;
        }));
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
        return Task.FromResult(ReadLocked<IReadOnlyList<RaggableNode>>(() =>
        {
            var result = new List<RaggableNode>();
            foreach (var fqn in fqns)
            {
                if (string.IsNullOrEmpty(fqn)) continue;
                if (_nodesByFqn.TryGetValue(fqn, out var n)) result.Add(n);
                else if (_nodesById.TryGetValue(fqn, out var byId)) result.Add(byId);
            }
            return result;
        }));
    }

    public Task<IReadOnlyList<RaggableNode>> QueryAsync(NodeQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.FromResult(ReadLocked<IReadOnlyList<RaggableNode>>(() => QueryLocked(query).Nodes));
    }

    /// <summary>
    /// The page of <paramref name="query"/> in FQN order and whether nodes matched past it (its <c>Take</c>, once
    /// clamped to <see cref="MaxNodes"/>, cut the matches).
    /// </summary>
    private (List<RaggableNode> Nodes, bool Clamped) QueryLocked(NodeQuery query)
    {
        IEnumerable<RaggableNode> q = _nodesById.Values;

        if (query.Level is { } level) q = q.Where(n => n.Level == level);
        if (query.Kinds is { Length: > 0 } kinds) q = q.Where(n => kinds.Contains(n.EffectiveKind));
        if (query.Languages is { Length: > 0 } langs) q = q.Where(n => langs.Contains(n.Language, StringComparer.OrdinalIgnoreCase));
        if (query.Packages is { Length: > 0 } packages)
        {
            q = q.Where(n => packages.Any(p => n.Fqn.Contains(p, StringComparison.Ordinal)));
        }
        if (!string.IsNullOrEmpty(query.ParentId)) q = q.Where(n => n.ParentId == query.ParentId);
        if (!string.IsNullOrEmpty(query.RootFqn))
        {
            var scope = RootScope.Resolve(query.RootFqn, this);
            q = q.Where(n => scope.Contains(n, n.Fqn));
        }
        if (query.Tags is { Count: > 0 } tags)
        {
            q = q.Where(n => tags.All(t => n.Tags.TryGetValue(t.Key, out var v) && string.Equals(v, t.Value, StringComparison.Ordinal)));
        }
        if (query.MinComplexity is { } min)
        {
            q = q.Where(n => (n.Body?.CyclomaticComplexity ?? 0) >= min);
        }

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, MaxNodes);
        // One node past the page says whether the take cut the matches.
        var page = q.OrderBy(n => n.Fqn, StringComparer.Ordinal).Skip(skip).Take(take + 1).ToList();
        var clamped = page.Count > take;
        if (clamped) page.RemoveAt(take);
        return (page, clamped);
    }

    /// <summary>
    /// What "at or under a root" means for <see cref="NodeQuery.RootFqn"/> and <see cref="EdgeListQuery.RootFqn"/>:
    /// a package or a file by its path (a <c>/</c> boundary); a type or a member by its FQN (a <c>.</c>, <c>::</c> or
    /// <c>/</c> boundary); a namespace by the files of every node declaring it plus its name as a prefix of the symbol
    /// part of an FQN; a text no node holds as a path and as an FQN prefix, both with a boundary.
    /// </summary>
    private sealed class RootScope
    {
        private string? _path;
        private string? _fqn;
        private string? _symbolPrefix;
        private HashSet<string>? _files;

        public static RootScope Resolve(string root, InMemoryRaggableStore store)
        {
            var node = store.Resolve(root);
            if (node is null)
                return new RootScope { _path = root.TrimEnd('/'), _fqn = root, _symbolPrefix = SymbolPart(root) };
            if (node.EffectiveKind == UniversalNodeKind.Namespace)
            {
                var name = SymbolPart(node.Fqn);
                var files = new HashSet<string>(StringComparer.Ordinal);
                foreach (var n in store._nodesById.Values)
                {
                    if (n.EffectiveKind == UniversalNodeKind.Namespace && !string.IsNullOrEmpty(n.VirtualFilePath)
                        && string.Equals(SymbolPart(n.Fqn), name, StringComparison.Ordinal))
                    {
                        files.Add(n.VirtualFilePath);
                    }
                }
                return new RootScope { _symbolPrefix = name, _files = files };
            }
            if (node.Level is NodeLevel.L0_Monorepo or NodeLevel.L1_Package or NodeLevel.L2_Module)
            {
                return string.IsNullOrEmpty(node.VirtualFilePath)
                    ? new RootScope { _fqn = node.Fqn }
                    : new RootScope { _path = node.VirtualFilePath.TrimEnd('/') };
            }
            return new RootScope { _fqn = node.Fqn };
        }

        /// <summary><paramref name="node"/> (null for an end outside the index) with <paramref name="fqn"/> is at or under the root.</summary>
        public bool Contains(RaggableNode? node, string fqn)
        {
            if (_path is not null && node is not null && UnderPath(node.VirtualFilePath, _path)) return true;
            if (_fqn is not null && UnderFqn(fqn, _fqn)) return true;
            if (_symbolPrefix is not null && UnderFqn(SymbolPart(fqn), _symbolPrefix)) return true;
            return _files is not null && node is not null && _files.Contains(node.VirtualFilePath);
        }

        private static string SymbolPart(string fqn)
        {
            var at = fqn.IndexOf("::", StringComparison.Ordinal);
            return at < 0 ? fqn : fqn[(at + 2)..];
        }

        private static bool UnderPath(string path, string prefix)
            => path.Length >= prefix.Length
               && path.StartsWith(prefix, StringComparison.Ordinal)
               && (path.Length == prefix.Length || path[prefix.Length] == '/');

        private static bool UnderFqn(string fqn, string prefix)
        {
            if (!fqn.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (fqn.Length == prefix.Length) return true;
            var next = fqn[prefix.Length];
            return next is '.' or '/' || fqn.AsSpan(prefix.Length).StartsWith("::", StringComparison.Ordinal);
        }
    }

    public Task<IReadOnlyList<RaggableNode>> GetChildrenAsync(string parentId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentId);
        return Task.FromResult(ReadLocked<IReadOnlyList<RaggableNode>>(() =>
        {
            var parent = Resolve(parentId);
            if (parent is null) return [];
            return parent.ChildrenIds
                .Select(id => _nodesById.TryGetValue(id, out var c) ? c : null)
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();
        }));
    }

    public Task<IReadOnlyList<StatementNode>> GetStatementsAsync(string parentSymbolId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentSymbolId);
        return Task.FromResult(ReadLocked<IReadOnlyList<StatementNode>>(() =>
        {
            if (_statementsByParent.TryGetValue(parentSymbolId, out var stmts)) return stmts;
            var node = Resolve(parentSymbolId);
            if (node is not null && node.Statements.Count > 0) return Flatten(node.Statements);
            return [];
        }));
    }

    public Task<RaggableNode?> GetModuleByPathAsync(string filePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        return Task.FromResult(ReadLocked<RaggableNode?>(() =>
        {
            foreach (var node in _nodesById.Values)
            {
                if (node.Level != NodeLevel.L2_Module) continue;
                if (!string.Equals(node.VirtualFilePath, filePath, StringComparison.Ordinal)) continue;
                return node;
            }
            return null;
        }));
    }

    public Task<int> GetNodeCountAsync(CancellationToken ct)
        => Task.FromResult(ReadLocked(() => _nodesById.Count));

    public Task<IReadOnlyList<RaggableEdge>> GetEdgesAsync(string fqn, EdgeKind kinds, Direction dir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(fqn);
        return Task.FromResult(ReadLocked<IReadOnlyList<RaggableEdge>>(() =>
        {
            var node = Resolve(fqn);
            if (node is null) return [];

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
            return edges;
        }));
    }

    public Task<EdgeListPage> ListEdgesAsync(EdgeListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.FromResult(ReadLocked(() => ListEdgesLocked(query, ct)));
    }

    private EdgeListPage ListEdgesLocked(EdgeListQuery query, CancellationToken ct)
    {
        var take = Math.Clamp(query.Take, 1, MaxNodes);
        var scope = string.IsNullOrEmpty(query.RootFqn) ? null : RootScope.Resolve(query.RootFqn, this);
        var matching = CollectMatchingEdges(query, scope, ct);
        matching.Sort(CompareEdges);
        var start = query.After is { } after ? FirstAfter(matching, after) : 0;

        var page = matching.GetRange(start, Math.Min(take, matching.Count - start)).ToImmutableArray();
        return new EdgeListPage
        {
            Edges = page,
            Total = matching.Count,
            HasMore = start + page.Length < matching.Count,
        };
    }

    private List<ListedEdge> CollectMatchingEdges(EdgeListQuery query, RootScope? scope, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matching = new List<ListedEdge>();
        foreach (var bucket in _edgesBySource.Values)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var edge in bucket)
            {
                if (query.Kinds != EdgeKind.None && (query.Kinds & edge.Kind) == EdgeKind.None) continue;
                if (!seen.Add(edge.Id)) continue;
                var listed = ListedEdgeOf(edge, scope);
                if (listed is not null) matching.Add(listed);
            }
        }
        return matching;
    }

    // The edge as listed, or null when a root scope is set and neither end lies inside it.
    private ListedEdge? ListedEdgeOf(RaggableEdge edge, RootScope? scope)
    {
        var from = Resolve(edge.FromId);
        var to = Resolve(edge.ToId);
        var fromFqn = from is { Fqn.Length: > 0 } ? from.Fqn : edge.FromId;
        var toFqn = to is { Fqn.Length: > 0 } ? to.Fqn : edge.ToId;
        if (scope is not null && !scope.Contains(from, fromFqn) && !scope.Contains(to, toFqn)) return null;
        return new ListedEdge
        {
            Id = edge.Id,
            FromId = edge.FromId,
            FromFqn = fromFqn,
            ToId = edge.ToId,
            ToFqn = toFqn,
            Kind = edge.Kind,
        };
    }

    // The first edge strictly after the key: an edge removed since the previous page moves nothing.
    private static int FirstAfter(List<ListedEdge> matching, EdgeListKey after)
    {
        var low = 0;
        var high = matching.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (CompareToKey(matching[mid], after) <= 0) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    private readonly record struct EdgeSortKey(string From, string To, EdgeKind Kind, string Id);

    /// <summary>The order of <see cref="EdgeListKey"/>, read off the edges without building a key.</summary>
    private static int CompareEdges(ListedEdge x, ListedEdge y)
        => Compare(new EdgeSortKey(x.FromFqn, x.ToFqn, x.Kind, x.Id), new EdgeSortKey(y.FromFqn, y.ToFqn, y.Kind, y.Id));

    private static int CompareToKey(ListedEdge x, EdgeListKey key)
        => Compare(new EdgeSortKey(x.FromFqn, x.ToFqn, x.Kind, x.Id), new EdgeSortKey(key.FromFqn, key.ToFqn, key.Kind, key.EdgeId));

    private static int Compare(EdgeSortKey x, EdgeSortKey y)
    {
        var c = string.CompareOrdinal(x.From, y.From);
        if (c != 0) return c;
        c = string.CompareOrdinal(x.To, y.To);
        if (c != 0) return c;
        c = ((int)x.Kind).CompareTo((int)y.Kind);
        return c != 0 ? c : string.CompareOrdinal(x.Id, y.Id);
    }

    public Task<SubGraph> ExpandAsync(ExpandQuery query, CancellationToken ct)
        => Task.FromResult(ReadLocked(() => _graph.Expand(query, ct)));

    public Task<IReadOnlyList<CallPath>> FindAllPathsAsync(PathQuery query, CancellationToken ct)
        => Task.FromResult(ReadLocked(() => _graph.FindAllPaths(query, ct)));

    public Task<CallPath?> ShortestPathAsync(ShortestPathQuery query, CancellationToken ct)
        => Task.FromResult(ReadLocked(() => _graph.ShortestPath(query, ct)));

    public Task<IReadOnlyList<Cycle>> FindCyclesAsync(CycleQuery query, CancellationToken ct)
        => Task.FromResult(ReadLocked(() => _graph.FindCycles(query, ct)));

    public Task<SemanticSearchResult> SemanticSearchAsync(SemanticQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SemanticSearchCoreAsync(query, ct);
    }

    private async Task<SemanticSearchResult> SemanticSearchCoreAsync(SemanticQuery query, CancellationToken ct)
    {
        var topK = Math.Clamp(query.TopK, 1, MaxTopK);

        // The embedding runs OUTSIDE the read lock (it can be an HTTP/ONNX call); only
        // the in-memory scoring below takes the lock.
        ReadOnlyMemory<float>? queryVec = null;
        var wantVector = query.Mode is SearchMode.Hybrid or SearchMode.Vector;
        if (wantVector && _queryEmbedder is not null)
            queryVec = await _queryEmbedder(query.Text, ct).ConfigureAwait(false);
        var hasVector = queryVec is { Length: > 0 };

        // Vector mode with no embedder used to return [] SILENTLY — a configured-out
        // embedder made every search look like an empty codebase. Hybrid degrades to
        // lexical instead; explicit Vector keeps the historical contract.
        if (query.Mode == SearchMode.Vector && !hasVector) return new SemanticSearchResult { Hits = [] };

        return ReadLocked(() =>
        {
            HashSet<string>? allowed = null;
            var capped = false;
            if (query.PreFilter is not null)
            {
                var (filtered, clamped) = QueryLocked(query.PreFilter);
                allowed = new HashSet<string>(filtered.Select(n => n.Id), StringComparer.Ordinal);
                capped = clamped;
            }

            return new SemanticSearchResult
            {
                Hits = SearchLocked(query, topK, hasVector ? queryVec!.Value : null, allowed),
                PreFilterCapped = capped,
            };
        });
    }

    private IReadOnlyList<SearchHit> SearchLocked(
        SemanticQuery query,
        int topK,
        ReadOnlyMemory<float>? queryVec,
        HashSet<string>? allowed)
    {
        // Both halves over-fetch to the store cap so the fusion has real overlap to work
        // with; the final Take(topK) trims after fusing.
        var vectorRanking = queryVec is null
            ? []
            : ScoreVector(queryVec.Value, query.MinScore, allowed, MaxTopK);
        var lexicalRanking = query.Mode == SearchMode.Vector
            ? (IReadOnlyList<(string Id, double Score)>)[]
            : FilterAllowed(_bm25.Search(query.Text, MaxTopK), allowed);

        // Single-source modes (or one half empty) skip the fusion — the raw score keeps
        // its native scale and the origin says which one.
        if (vectorRanking.Count == 0 || lexicalRanking.Count == 0)
        {
            var single = vectorRanking.Count > 0 ? vectorRanking : lexicalRanking;
            var origin = vectorRanking.Count > 0 ? "vector" : "bm25";
            return [.. single.Take(topK).Select(x => ToHit(x.Id, x.Score, origin))];
        }

        var fused = Lexical.RankFusion.Fuse(topK, vectorRanking, lexicalRanking);
        return [.. fused.Select(x => ToHit(x.Id, x.Score, OriginLabel(x.Origins)))];
    }

    /// <summary>
    /// Names the retrieval source behind a fused hit from the origin bitmask
    /// <c>RankFusion.Fuse</c> returns: bit 0 is the vector ranking,
    /// bit 1 the BM25 one, both set means the hit came from each.
    /// </summary>
    private static string OriginLabel(int origins) => origins switch
    {
        0b01 => "vector",
        0b10 => "bm25",
        _ => "hybrid",
    };

    private IReadOnlyList<(string Id, double Score)> ScoreVector(
        ReadOnlyMemory<float> queryVec,
        double minScore,
        HashSet<string>? allowed,
        int cap)
    {
        var scored = new List<(string Id, double Score)>();
        foreach (var node in _nodesById.Values)
        {
            if (node.Embedding is null) continue;
            if (allowed is not null && !allowed.Contains(node.Id)) continue;
            var score = Cosine(queryVec.Span, node.Embedding.Value.Span);
            if (score < minScore) continue;
            scored.Add((node.Id, score));
        }
        return [.. scored.OrderByDescending(x => x.Score).Take(cap)];
    }

    private static IReadOnlyList<(string Id, double Score)> FilterAllowed(
        IReadOnlyList<(string NodeId, double Score)> ranking,
        HashSet<string>? allowed)
        => allowed is null
            ? [.. ranking.Select(x => (x.NodeId, x.Score))]
            : [.. ranking.Where(x => allowed.Contains(x.NodeId)).Select(x => (x.NodeId, x.Score))];

    private SearchHit ToHit(string nodeId, double score, string origin)
    {
        var node = _nodesById[nodeId];
        return new SearchHit
        {
            Fqn = node.Fqn,
            Score = score,
            SummaryShort = node.SemanticSummary,
            Signature = node.Signature,
            MatchOrigin = origin,
        };
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
