using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// In-memory implementation of <see cref="IRaggableStore"/>: owns the node/edge index
/// and its mutations (replace, incremental add, recursive removal). Read-side queries
/// live in <c>InMemoryRaggableStore.Queries.cs</c>; graph traversal, analytics and
/// source extraction are delegated to <see cref="GraphTraversal"/>,
/// <see cref="StoreAnalytics"/> and <see cref="SourceSliceReader"/>
/// (R4.2 god-file decomposition).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The ReaderWriterLockSlim lives exactly as long as the store, and the store is a process-lifetime singleton in every host (AddRaggableTree). Making the store IDisposable would ripple CA2000 into every construction site (production factories and ~20 tests) to release a lock the OS reclaims at exit anyway.")]
public sealed partial class InMemoryRaggableStore : IRaggableStore, IIndexInvalidation
{
    public const int MaxTopK = 50;
    public const int MaxDepth = 6;
    public const int MaxNodes = 1000;
    public const int MaxLines = 200;
    public const int MaxChildren = 200;

    private readonly Dictionary<string, RaggableNode> _nodesByFqn;
    private readonly Dictionary<string, RaggableNode> _nodesById;
    private readonly Dictionary<string, List<RaggableEdge>> _edgesBySource;
    private readonly Dictionary<string, List<RaggableEdge>> _edgesByTarget;
    private readonly Dictionary<string, List<StatementNode>> _statementsByParent;
    private readonly Dictionary<string, DateTimeOffset> _indexedAtByRoot = new(StringComparer.Ordinal);
    private readonly Lexical.Bm25CodeIndex _bm25 = new();
    // Coordination primitives for the hybrid-search and freshness work, PLAN A3/B1. The
    // reader-writer lock makes searches safe DURING an incremental reindex, because the
    // store used to be plain dictionaries that AddNodes mutated in place while a
    // concurrent search was still enumerating them, which was an InvalidOperationException
    // waiting to happen. The dirty set records the paths that were edited but not yet
    // reindexed, which is what IIndexInvalidation exposes. The lock allows recursion
    // because query methods call each other, as semantic search does with the graph query.
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly HashSet<string> _dirtyPaths = new(StringComparer.Ordinal);
    private Func<string, CancellationToken, Task<ReadOnlyMemory<float>?>>? _queryEmbedder;
    private readonly Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;
    private readonly GraphTraversal _graph;
    private readonly StoreAnalytics _analytics;
    private readonly SourceSliceReader _sourceReader;

    internal T ReadLocked<T>(Func<T> read)
    {
        _lock.EnterReadLock();
        try { return read(); }
        finally { _lock.ExitReadLock(); }
    }

    private void WriteLocked(Action write)
    {
        _lock.EnterWriteLock();
        try { write(); }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Marks a path as edited-but-not-reindexed (<c>IIndexInvalidation</c>). A path
    /// outside every indexed root is a no-op — nothing stale to report about a file the
    /// index never covered.
    /// </summary>
    public void MarkDirty(string virtualPath)
    {
        if (string.IsNullOrEmpty(virtualPath)) return;
        var target = virtualPath.TrimEnd('/');
        var covered = ReadLocked(() => _indexedAtByRoot.Keys.Any(root =>
        {
            var prefix = root.TrimEnd('/');
            return prefix.Length > 0
                && (target.Equals(prefix, StringComparison.Ordinal)
                    || target.StartsWith(prefix + "/", StringComparison.Ordinal));
        }));
        if (!covered) return;
        WriteLocked(() => _dirtyPaths.Add(target));
    }

    /// <summary>Paths edited since the last (re)index — what a freshness pass must cover.</summary>
    public IReadOnlyCollection<string> DirtyPaths
        => ReadLocked(() => (IReadOnlyCollection<string>)_dirtyPaths.ToArray());

    /// <summary>Clears the given paths from the dirty set (called after they were reindexed).</summary>
    public void ClearDirty(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        WriteLocked(() =>
        {
            foreach (var p in paths) _dirtyPaths.Remove(p.TrimEnd('/'));
        });
    }

    /// <summary>
    /// Consistent snapshot of the current graph (read-locked) — what the incremental
    /// reindex engine needs as its "current tree" to reuse unchanged nodes.
    /// </summary>
    public (IReadOnlyList<RaggableNode> Nodes, IReadOnlyList<RaggableEdge> Edges) ExportSnapshot()
        => ReadLocked(() =>
        {
            IReadOnlyList<RaggableNode> nodes = [.. _nodesById.Values];
            IReadOnlyList<RaggableEdge> edges = [.. _edgesBySource.Values.SelectMany(list => list)];
            return (nodes, edges);
        });

    public void SetQueryEmbedder(Func<string, CancellationToken, Task<ReadOnlyMemory<float>?>>? embedder)
        => _queryEmbedder = embedder;

    public InMemoryRaggableStore(RaggableTree tree,
        IFileSystemService fileSystem,
        Func<string, CancellationToken, Task<ReadOnlyMemory<float>?>>? queryEmbedder = null)
        : this(NodesOf(tree), tree.Edges, fileSystem, queryEmbedder)
    {
    }

    public InMemoryRaggableStore(
        IReadOnlyList<RaggableNode> nodes,
        IReadOnlyList<RaggableEdge> edges,
        IFileSystemService fileSystem,
        Func<string, CancellationToken, Task<ReadOnlyMemory<float>?>>? queryEmbedder = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _nodesByFqn = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        _nodesById = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        _statementsByParent = new Dictionary<string, List<StatementNode>>(StringComparer.Ordinal);
        _edgesBySource = new Dictionary<string, List<RaggableEdge>>(StringComparer.Ordinal);
        _edgesByTarget = new Dictionary<string, List<RaggableEdge>>(StringComparer.Ordinal);
        _queryEmbedder = queryEmbedder;
        _graph = new GraphTraversal(_nodesById, _edgesBySource, _edgesByTarget, Resolve, MaxDepth, MaxNodes);
        _analytics = new StoreAnalytics(_nodesById, _edgesBySource, _indexedAtByRoot, _graph, Resolve, _clock, MaxNodes);
        _sourceReader = new SourceSliceReader(fileSystem, MaxLines);
        Replace(nodes, edges);
    }

    private static IReadOnlyList<RaggableNode> NodesOf(RaggableTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return tree.Nodes;
    }

    public void Replace(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        WriteLocked(() => ReplaceLocked(nodes, edges));
    }

    private void ReplaceLocked(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges)
    {
        // Deliberately does NOT clear the dirty set: a write can race a reindex pass,
        // and clearing everything here would silently drop a path that changed AFTER
        // the pass parsed it. The freshness service clears exactly the snapshot it
        // covered; anything newer stays dirty for the next pass.
        _bm25.Clear();
        _nodesByFqn.Clear();
        _nodesById.Clear();
        _statementsByParent.Clear();
        _edgesBySource.Clear();
        _edgesByTarget.Clear();
        _sourceReader.ClearCache();
        _indexedAtByRoot.Clear();

        var now = _clock();
        foreach (var node in nodes)
        {
            IndexNode(node, now);
        }

        foreach (var edge in edges)
        {
            IndexEdge(edge);
        }
    }

    private void IndexNode(RaggableNode node, DateTimeOffset now)
    {
        _bm25.Upsert(node);
        _nodesById[node.Id] = node;
        if (!string.IsNullOrEmpty(node.Fqn)) _nodesByFqn[node.Fqn] = node;
        if (node.Statements.Count > 0)
        {
            _statementsByParent[node.Id] = Flatten(node.Statements);
            if (!string.IsNullOrEmpty(node.Fqn))
            {
                _statementsByParent[node.Fqn] = _statementsByParent[node.Id];
            }
        }
        if (node.Level == NodeLevel.L0_Monorepo && !string.IsNullOrEmpty(node.VirtualFilePath))
        {
            _indexedAtByRoot[node.VirtualFilePath] = now;
        }
    }

    private void IndexEdge(RaggableEdge edge)
    {
        if (!_edgesBySource.TryGetValue(edge.FromId, out var outList))
        {
            outList = [];
            _edgesBySource[edge.FromId] = outList;
        }
        outList.Add(edge);

        if (!_edgesByTarget.TryGetValue(edge.ToId, out var inList))
        {
            inList = [];
            _edgesByTarget[edge.ToId] = inList;
        }
        inList.Add(edge);
    }

    public void RemoveFilesAndDescendants(IReadOnlyCollection<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (filePaths.Count == 0) return;
        WriteLocked(() => RemoveFilesAndDescendantsLocked(filePaths));
    }

    private void RemoveFilesAndDescendantsLocked(IReadOnlyCollection<string> filePaths)
    {
        var pathSet = new HashSet<string>(filePaths, StringComparer.Ordinal);
        var toRemove = new HashSet<string>(StringComparer.Ordinal);
        toRemove.UnionWith(_nodesById.Values
            .Where(node => pathSet.Contains(node.VirtualFilePath))
            .Select(node => node.Id));

        foreach (var id in toRemove.ToList())
        {
            RemoveNodeRecursive(id, toRemove);
        }

        foreach (var id in toRemove)
        {
            RemoveNodeById(id);
        }
    }

    private void RemoveNodeById(string id)
    {
        _bm25.Remove(id);
        if (_nodesById.TryGetValue(id, out var node))
        {
            _nodesById.Remove(id);
            if (!string.IsNullOrEmpty(node.Fqn)) _nodesByFqn.Remove(node.Fqn);
            _statementsByParent.Remove(id);
            _statementsByParent.Remove(node.Fqn);
        }
        _edgesBySource.Remove(id);
        if (_edgesByTarget.TryGetValue(id, out var incoming))
        {
            foreach (var e in incoming)
            {
                if (_edgesBySource.TryGetValue(e.FromId, out var outList))
                {
                    outList.RemoveAll(x => x.Id == e.Id);
                }
            }
            _edgesByTarget.Remove(id);
        }
    }

    public void AddNodes(IEnumerable<RaggableNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        WriteLocked(() =>
        {
            var now = _clock();
            foreach (var node in nodes)
            {
                IndexNode(node, now);
            }
        });
    }

    public void AddEdges(IEnumerable<RaggableEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        WriteLocked(() =>
        {
            foreach (var edge in edges)
            {
                IndexEdge(edge);
            }
        });
    }

    private void RemoveNodeRecursive(string id, HashSet<string> collector)
    {
        if (!_nodesById.TryGetValue(id, out var node)) return;
        foreach (var childId in node.ChildrenIds.Where(collector.Add))
        {
            RemoveNodeRecursive(childId, collector);
        }
    }

    private RaggableNode? Resolve(string key)
    {
        if (_nodesByFqn.TryGetValue(key, out var n)) return n;
        if (_nodesById.TryGetValue(key, out var byId)) return byId;
        return null;
    }

    private static List<StatementNode> Flatten(IEnumerable<StatementNode> stmts)
    {
        var list = new List<StatementNode>();
        foreach (var s in stmts)
        {
            list.Add(s);
            if (s.Children.Count > 0) list.AddRange(Flatten(s.Children));
        }
        return list;
    }
}
