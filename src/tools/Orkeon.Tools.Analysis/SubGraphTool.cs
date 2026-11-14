using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Analysis.Internal;

namespace Orkeon.Tools.Analysis;

public sealed class SubGraphTool : ToolBase<SubGraphRequest, SubGraphResponse>
{
    private readonly IRaggableStore _store;

    public SubGraphTool(IRaggableStore store, ILogger<SubGraphTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "sub_graph";
    public override string Description => "What surrounds these symbols? Expands the graph from the seeds (FQNs) along edge_kinds, Forward (what they call or extend) or Backward (who calls or extends them), up to depth hops and max_nodes nodes (100); truncated says when the expansion stopped early. For the whole graph at a scope, dependency_graph; for the paths between two symbols, flow_trace.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    // A small model fills every optional field and sends 0: refuse it rather than return an empty result.
    protected override string? ValidateTypedRequest(SubGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Depth < 1)
            return $"depth must be at least 1 (got {request.Depth}); omit it for the default of 2";
        if (request.MaxNodes < 1)
            return $"max_nodes must be at least 1 (got {request.MaxNodes}); omit it for the default of 100";
        return null;
    }

    protected override Task<SubGraphResponse> ExecuteTypedAsync(SubGraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteTypedCoreAsync();

        async Task<SubGraphResponse> ExecuteTypedCoreAsync()
        {
            var edgeKinds = GraphKindNames.ParseEdgeKinds(request.EdgeKinds);
            var nodeFilter = GraphKindNames.ParseNodeKinds(request.NodeKinds);
            var query = new ExpandQuery
            {
                Seeds = request.Seeds,
                EdgeKinds = edgeKinds,
                Direction = request.Direction,
                MaxDepth = request.Depth,
                MaxNodes = request.MaxNodes,
            };
            var expanded = await _store.ExpandAsync(query, cancellationToken).ConfigureAwait(false);

            var seedSet = new HashSet<string>(request.Seeds.Where(s => !string.IsNullOrEmpty(s)), StringComparer.Ordinal);
            var graphNodes = expanded.Nodes.Where(n => request.NodeKinds.IsDefaultOrEmpty || nodeFilter(n)).Select(n => new GraphNode
            {
                Fqn = n.Fqn,
                Name = n.Name,
                Kind = n.EffectiveKind,
                Level = n.Level,
                IsSeed = seedSet.Contains(n.Fqn) || seedSet.Contains(n.Id),
            }).ToImmutableArray();
            var graphEdges = expanded.Edges.Select(e => new GraphEdge { From = e.FromId, To = e.ToId, Kind = e.Kind }).ToImmutableArray();

            return new SubGraphResponse
            {
                Nodes = graphNodes,
                Edges = graphEdges,
                MermaidFlowchart = request.IncludeMermaid ? MermaidGenerator.ToFlowchart(graphNodes, graphEdges) : null,
                Truncated = expanded.Truncated,
            };
        }
    }
}
