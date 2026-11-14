using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis;

public sealed class DependencyGraphTool : ToolBase<DependencyGraphRequest, DependencyGraphResponse>
{
    private readonly IRaggableStore _store;

    public DependencyGraphTool(IRaggableStore store, ILogger<DependencyGraphTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "dependency_graph";
    public override string Description =>
        "What depends on what at this scope? Returns the nodes and the edges of the given edge_kinds (imports by default) among "
        + "the packages (L1_Package), modules (L2_Module) or symbols (L3_Symbol) of the scope, at most max_nodes nodes (200) selected "
        + "by root_fqn and node_kinds, truncated when the scope holds more; cycles and cycle_count cover the whole scope, each cycle "
        + "naming its members as FQNs. For every edge of a kind across the index, edge_list; for the neighbourhood of a symbol, sub_graph.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    // A small model fills every optional field and sends 0: refuse it rather than return an empty result.
    protected override string? ValidateTypedRequest(DependencyGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxNodes < 1)
            return $"max_nodes must be at least 1 (got {request.MaxNodes}); omit it for the default of 200";
        // 0 is a count-only mode (no cycle listed, cycles_truncated true, cycle_count kept); only a negative is meaningless.
        if (request.MaxCycles < 0)
            return $"max_cycles must be at least 0 (got {request.MaxCycles}); 0 returns cycle_count alone, omit it for the default of 20";
        if (request.MaxCycleMembers < 1)
            return $"max_cycle_members must be at least 1 (got {request.MaxCycleMembers}); omit it for the default of 50";
        return null;
    }

    protected override Task<DependencyGraphResponse> ExecuteTypedAsync(DependencyGraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<DependencyGraphResponse> ExecuteCoreAsync()
        {
            var edgeKinds = GraphKindNames.ParseEdgeKinds(request.EdgeKinds);
            var (nodes, truncated) = await ResolveNodesAsync(request, cancellationToken).ConfigureAwait(false);
            var kindMask = ResolveKindMask(edgeKinds);
            // The cycles follow the kinds the edges follow: an empty list means imports here, never every kind.
            ImmutableArray<EdgeKind> cycleKinds = edgeKinds.IsDefaultOrEmpty ? [EdgeKind.Imports] : edgeKinds;
            var edges = await CollectEdgesAsync(request, nodes, kindMask, cancellationToken).ConfigureAwait(false);

            var graphNodes = nodes.Select(n => new GraphNode
            {
                Fqn = n.Fqn,
                Name = n.Name,
                Kind = n.EffectiveKind,
                Level = n.Level,
            }).ToImmutableArray();
            var graphEdges = edges.ToImmutableArray();

            var cycles = await _store.FindCyclesAsync(new CycleQuery { Scope = request.Scope, EdgeKinds = cycleKinds }, cancellationToken).ConfigureAwait(false);
            var metrics = BuildMetrics(graphNodes, graphEdges, cycles.Count);
            var (graphCycles, cyclesTruncated) = BoundCycles(cycles, kindMask, request.MaxCycles, request.MaxCycleMembers);

            return new DependencyGraphResponse
            {
                Nodes = graphNodes,
                Edges = graphEdges,
                Metrics = metrics,
                MermaidFlowchart = request.IncludeMermaid ? MermaidGenerator.ToFlowchart(graphNodes, graphEdges) : null,
                Dot = request.IncludeDot ? MermaidGenerator.ToDot(graphNodes, graphEdges) : null,
                Truncated = truncated,
                Cycles = graphCycles,
                CyclesTruncated = cyclesTruncated,
            };
        }
    }

    /// <summary>
    /// The nodes of the page and whether the scope holds more: one node past <c>max_nodes</c> is asked for,
    /// so that <c>truncated</c> says that more nodes exist, never that the page is full (#283).
    /// </summary>
    private async Task<(IReadOnlyList<RaggableNode> Nodes, bool Truncated)> ResolveNodesAsync(DependencyGraphRequest request, CancellationToken cancellationToken)
    {
        var maxNodes = Math.Max(1, request.MaxNodes);
        var probe = PageProbe.OnePast(maxNodes);
        // The root goes into the query: the page and truncated then describe the root's subset, not the whole scope.
        var root = string.IsNullOrEmpty(request.RootFqn) ? null : request.RootFqn;
        IReadOnlyList<RaggableNode> nodes;
        if (request.NodeKinds.IsDefaultOrEmpty)
        {
            nodes = await _store.QueryAsync(
                new NodeQuery { Level = request.Scope, RootFqn = root, Take = probe },
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var found = new Dictionary<string, RaggableNode>(StringComparer.Ordinal);
            foreach (var query in GraphKindNames.ParseNodeQueries(request.NodeKinds))
            {
                var batch = await _store.QueryAsync(query with { Level = request.Scope, RootFqn = root, Take = probe }, cancellationToken).ConfigureAwait(false);
                foreach (var n in batch) found.TryAdd(n.Id, n);
            }

            nodes = [.. found.Values.Take(probe)];
        }

        var truncated = nodes.Count > maxNodes;
        if (truncated) nodes = [.. nodes.Take(maxNodes)];
        return (nodes, truncated);
    }

    /// <summary>
    /// Orders the cycles largest first (then by first member), keeps <paramref name="maxCycles"/> of them and
    /// <paramref name="maxMembers"/> sorted members of each. Each cycle carries <paramref name="kindMask"/>, the
    /// kinds followed to find it: <see cref="Cycle.EdgeKind"/> holds only the first of them.
    /// </summary>
    private static (ImmutableArray<GraphCycle> Cycles, bool Truncated) BoundCycles(
        IReadOnlyList<Cycle> cycles, EdgeKind kindMask, int maxCycles, int maxMembers)
    {
        var cycleCap = Math.Max(0, maxCycles);
        var memberCap = Math.Max(1, maxMembers);
        var kept = cycles
            .Select(c => c.Fqns.Order(StringComparer.Ordinal).ToImmutableArray())
            .OrderByDescending(members => members.Length)
            .ThenBy(members => members.FirstOrDefault(), StringComparer.Ordinal)
            .Take(cycleCap)
            .ToList();

        var truncated = cycles.Count > kept.Count || kept.Exists(members => members.Length > memberCap);
        var bounded = kept.Select(members => new GraphCycle
        {
            Fqns = [.. members.Take(memberCap)],
            MemberCount = members.Length,
            EdgeKind = kindMask,
        }).ToImmutableArray();
        return (bounded, truncated);
    }

    private static EdgeKind ResolveKindMask(ImmutableArray<EdgeKind> edgeKinds)
    {
        var kindMask = EdgeKind.None;
        foreach (var k in edgeKinds) kindMask |= k;
        if (kindMask == EdgeKind.None) kindMask = EdgeKind.Imports;
        return kindMask;
    }

    private async Task<List<GraphEdge>> CollectEdgesAsync(
        DependencyGraphRequest request,
        IReadOnlyList<RaggableNode> nodes,
        EdgeKind kindMask,
        CancellationToken cancellationToken)
    {
        var nodeIdSet = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.Ordinal);
        var edges = new List<GraphEdge>();
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outEdges = await _store.GetEdgesAsync(node.Fqn, kindMask, Direction.Forward, cancellationToken).ConfigureAwait(false);
            foreach (var e in outEdges)
            {
                if (!request.IncludeExternal && !nodeIdSet.Contains(e.ToId)) continue;
                edges.Add(new GraphEdge { From = e.FromId, To = e.ToId, Kind = e.Kind });
            }
        }

        return edges;
    }

    private static GraphMetrics BuildMetrics(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges, int cycleCount)
    {
        if (nodes.Count == 0) return new GraphMetrics(0, edges.Count, cycleCount, 0, 0);
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in edges)
        {
            fanOut[e.From] = fanOut.GetValueOrDefault(e.From) + 1;
            fanIn[e.To] = fanIn.GetValueOrDefault(e.To) + 1;
        }
        var avgIn = fanIn.Count == 0 ? 0 : fanIn.Values.Average();
        var avgOut = fanOut.Count == 0 ? 0 : fanOut.Values.Average();
        return new GraphMetrics(nodes.Count, edges.Count, cycleCount, avgIn, avgOut);
    }
}
