using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
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
    public override string Description => "Dependency graph at a given scope (L1 packages, L2 modules, L3 symbols).";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<DependencyGraphResponse> ExecuteTypedAsync(DependencyGraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<DependencyGraphResponse> ExecuteCoreAsync()
        {
            var nodes = await ResolveNodesAsync(request, cancellationToken).ConfigureAwait(false);
            var kindMask = ResolveKindMask(request);
            var edges = await CollectEdgesAsync(request, nodes, kindMask, cancellationToken).ConfigureAwait(false);

            var graphNodes = nodes.Select(n => new GraphNode
            {
                Fqn = n.Fqn,
                Name = n.Name,
                Kind = n.EffectiveKind,
                Level = n.Level,
            }).ToImmutableArray();
            var graphEdges = edges.ToImmutableArray();

            var cycles = await _store.FindCyclesAsync(new CycleQuery { Scope = request.Scope, EdgeKinds = request.EdgeKinds }, cancellationToken).ConfigureAwait(false);
            var metrics = BuildMetrics(graphNodes, graphEdges, cycles.Count);
            var truncated = nodes.Count >= request.MaxNodes;

            return new DependencyGraphResponse
            {
                Nodes = graphNodes,
                Edges = graphEdges,
                Metrics = metrics,
                MermaidFlowchart = request.IncludeMermaid ? MermaidGenerator.ToFlowchart(graphNodes, graphEdges) : null,
                Dot = request.IncludeDot ? MermaidGenerator.ToDot(graphNodes, graphEdges) : null,
                Truncated = truncated,
            };
        }
    }

    private async Task<IReadOnlyList<RaggableNode>> ResolveNodesAsync(DependencyGraphRequest request, CancellationToken cancellationToken)
    {
        var nodes = await _store.QueryAsync(
            new NodeQuery { Level = request.Scope, Take = Math.Max(1, request.MaxNodes) },
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(request.RootFqn))
        {
            var root = await _store.GetAsync(request.RootFqn, cancellationToken).ConfigureAwait(false);
            if (root is not null)
            {
                nodes = [.. nodes.Where(n => n.Fqn.StartsWith(root.Fqn, StringComparison.Ordinal))];
            }
        }

        return nodes;
    }

    private static EdgeKind ResolveKindMask(DependencyGraphRequest request)
    {
        var kindMask = EdgeKind.None;
        foreach (var k in request.EdgeKinds) kindMask |= k;
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
