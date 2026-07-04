using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
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
    public override string Description => "Sub-graph expansion around seeds, bounded by depth and node count.";

    protected override Task<SubGraphResponse> ExecuteTypedAsync(SubGraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteTypedCoreAsync();

        async Task<SubGraphResponse> ExecuteTypedCoreAsync()
        {
            var query = new ExpandQuery
            {
                Seeds = request.Seeds,
                EdgeKinds = request.EdgeKinds,
                Direction = request.Direction,
                MaxDepth = request.Depth,
                MaxNodes = request.MaxNodes,
            };
            var expanded = await _store.ExpandAsync(query, cancellationToken).ConfigureAwait(false);

            var seedSet = new HashSet<string>(request.Seeds.Where(s => !string.IsNullOrEmpty(s)), StringComparer.Ordinal);
            var graphNodes = expanded.Nodes.Select(n => new GraphNode
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
