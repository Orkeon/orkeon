using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// Dedicated tests for <see cref="GraphTraversal"/> exercised in isolation from
/// <see cref="InMemoryRaggableStore"/> (R4.2 god-file decomposition).
/// </summary>
public class GraphTraversalTests
{
    private const int MaxDepth = 6;
    private const int MaxNodes = 1000;

    private static GraphTraversal Build(
        IReadOnlyList<RaggableNode> nodes,
        IReadOnlyList<RaggableEdge> edges)
    {
        var byId = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        var byFqn = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
        var bySource = new Dictionary<string, List<RaggableEdge>>(StringComparer.Ordinal);
        var byTarget = new Dictionary<string, List<RaggableEdge>>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            byId[node.Id] = node;
            if (!string.IsNullOrEmpty(node.Fqn)) byFqn[node.Fqn] = node;
        }
        foreach (var edge in edges)
        {
            if (!bySource.TryGetValue(edge.FromId, out var outList)) bySource[edge.FromId] = outList = [];
            outList.Add(edge);
            if (!byTarget.TryGetValue(edge.ToId, out var inList)) byTarget[edge.ToId] = inList = [];
            inList.Add(edge);
        }

        RaggableNode? Resolve(string key)
        {
            if (byFqn.TryGetValue(key, out var n)) return n;
            if (byId.TryGetValue(key, out var byIdNode)) return byIdNode;
            return null;
        }

        return new GraphTraversal(byId, bySource, byTarget, Resolve, MaxDepth, MaxNodes);
    }

    private static RaggableNode Synthetic(string fqn, NodeLevel level = NodeLevel.L3_Symbol)
        => new()
        {
            Id = fqn,
            Kind = UniversalNodeKind.Function,
            Name = fqn,
            VirtualFilePath = $"/tmp/{fqn}.ts",
            Range = new NodeRange(0, 0, 0, 0, 0),
            Level = level,
            Language = "typescript",
            SourceSnippet = string.Empty,
            Sha256 = string.Empty,
            Fqn = fqn,
        };

    [Fact]
    public void Expand_walks_edges_within_budget()
    {
        var graph = Build(
            [Synthetic("a"), Synthetic("b"), Synthetic("c")],
            [new("e1", "a", "b", EdgeKind.Calls), new("e2", "b", "c", EdgeKind.Calls)]);

        var subgraph = graph.Expand(
            new ExpandQuery { Seeds = ["a"], EdgeKinds = [EdgeKind.Calls], MaxDepth = 2, MaxNodes = 50 },
            CancellationToken.None);

        Assert.Contains(subgraph.Nodes, n => n.Id == "a");
        Assert.Contains(subgraph.Nodes, n => n.Id == "b");
        Assert.False(subgraph.Truncated);
    }

    [Fact]
    public void Expand_applies_max_nodes_budget()
    {
        var nodes = new List<RaggableNode>();
        var edges = new List<RaggableEdge>();
        for (var i = 0; i < 20; i++) nodes.Add(Synthetic($"n{i}"));
        for (var i = 0; i < 19; i++) edges.Add(new RaggableEdge($"e{i}", $"n{i}", $"n{i + 1}", EdgeKind.Calls));
        var graph = Build(nodes, edges);

        var subgraph = graph.Expand(
            new ExpandQuery { Seeds = ["n0"], EdgeKinds = [EdgeKind.Calls], MaxDepth = 30, MaxNodes = 5 },
            CancellationToken.None);

        Assert.True(subgraph.Truncated);
        Assert.True(subgraph.Nodes.Length <= 5);
    }

    [Fact]
    public void ShortestPath_returns_direct_chain()
    {
        var graph = Build(
            [Synthetic("a"), Synthetic("b"), Synthetic("c")],
            [new("e1", "a", "b", EdgeKind.Calls), new("e2", "b", "c", EdgeKind.Calls)]);

        var path = graph.ShortestPath(new ShortestPathQuery { From = "a", To = "c" }, CancellationToken.None);

        Assert.NotNull(path);
        Assert.Equal(2, path!.Length);
        Assert.Equal("a", path.Fqns[0]);
        Assert.Equal("c", path.Fqns[^1]);
    }

    [Fact]
    public void ShortestPath_returns_null_when_unreachable()
    {
        var graph = Build(
            [Synthetic("a"), Synthetic("b")],
            []);

        var path = graph.ShortestPath(new ShortestPathQuery { From = "a", To = "b" }, CancellationToken.None);

        Assert.Null(path);
    }

    [Fact]
    public void FindAllPaths_enumerates_paths()
    {
        var graph = Build(
            [Synthetic("a"), Synthetic("b"), Synthetic("c"), Synthetic("d")],
            [
                new("e1", "a", "b", EdgeKind.Calls),
                new("e2", "a", "c", EdgeKind.Calls),
                new("e3", "b", "d", EdgeKind.Calls),
                new("e4", "c", "d", EdgeKind.Calls),
            ]);

        var paths = graph.FindAllPaths(
            new PathQuery { From = "a", To = "d", EdgeKinds = [EdgeKind.Calls], MaxDepth = 3 },
            CancellationToken.None);

        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public void FindCycles_detects_simple_cycle()
    {
        var graph = Build(
            [Synthetic("a"), Synthetic("b")],
            [new("e1", "a", "b", EdgeKind.Calls), new("e2", "b", "a", EdgeKind.Calls)]);

        var cycles = graph.FindCycles(
            new CycleQuery { Scope = NodeLevel.L3_Symbol, EdgeKinds = [EdgeKind.Calls] },
            CancellationToken.None);

        Assert.NotEmpty(cycles);
        Assert.Contains(cycles, c => c.Fqns.Length >= 2);
    }

    [Fact]
    public void FindCycles_returns_empty_for_acyclic_graph()
    {
        var graph = Build(
            [Synthetic("a"), Synthetic("b"), Synthetic("c")],
            [new("e1", "a", "b", EdgeKind.Calls), new("e2", "b", "c", EdgeKind.Calls)]);

        var cycles = graph.FindCycles(
            new CycleQuery { Scope = NodeLevel.L3_Symbol, EdgeKinds = [EdgeKind.Calls] },
            CancellationToken.None);

        Assert.Empty(cycles);
    }

    [Fact]
    public void CountEdges_counts_directional_edges()
    {
        var graph = Build(
            [Synthetic("a"), Synthetic("b"), Synthetic("c")],
            [new("e1", "a", "b", EdgeKind.Calls), new("e2", "a", "c", EdgeKind.Calls), new("e3", "b", "a", EdgeKind.Calls)]);

        Assert.Equal(2, graph.CountEdges("a", EdgeKind.Calls, Direction.Forward));
        Assert.Equal(1, graph.CountEdges("a", EdgeKind.Calls, Direction.Backward));
    }
}
