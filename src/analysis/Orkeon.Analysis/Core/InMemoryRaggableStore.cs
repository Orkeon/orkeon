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
public sealed partial class InMemoryRaggableStore : IRaggableStore
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
    private Func<string, CancellationToken, Task<ReadOnlyMemory<float>?>>? _queryEmbedder;
    private readonly Func<DateTimeOffset> _clock = () => DateTimeOffset.UtcNow;
    private readonly GraphTraversal _graph;
    private readonly StoreAnalytics _analytics;
    private readonly SourceSliceReader _sourceReader;

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
        var now = _clock();
        foreach (var node in nodes)
        {
            IndexNode(node, now);
        }
    }

    public void AddEdges(IEnumerable<RaggableEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        foreach (var edge in edges)
        {
            IndexEdge(edge);
        }
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
