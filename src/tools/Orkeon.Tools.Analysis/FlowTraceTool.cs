using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class FlowTraceTool : ToolBase<FlowTraceRequest, FlowTraceResponse>
{
    private readonly IRaggableStore _store;
    private readonly IndexFreshnessService? _freshness;

    public FlowTraceTool(
        IRaggableStore store,
        ILogger<FlowTraceTool>? logger = null,
        IndexFreshnessService? freshness = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _freshness = freshness;
    }

    public override string Name => "flow_trace";
    public override string Description => "Trace call flow from a symbol (optionally to a target) within a depth budget.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<FlowTraceResponse> ExecuteTypedAsync(FlowTraceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<FlowTraceResponse> ExecuteTypedCoreAsync()
        {
            // Lazy freshness (PLAN B3): trace edges of the CURRENT code, not the indexed past.
            if (_freshness is not null)
                await _freshness.EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
            // The same names as dependency_graph and sub_graph: inherits follows extends and implements.
            var parsed = GraphKindNames.ParseEdgeKinds(request.EdgeKinds);
            ImmutableArray<EdgeKind> edgeKinds = parsed.IsDefaultOrEmpty ? [EdgeKind.Calls] : parsed;
            if (string.IsNullOrEmpty(request.From)) return new FlowTraceResponse { Paths = [] };

            if (!request.IncludeAllPaths && !string.IsNullOrEmpty(request.To))
            {
                var shortest = await _store.ShortestPathAsync(
                    new ShortestPathQuery
                    {
                        From = request.From,
                        To = request.To,
                        EdgeKinds = edgeKinds,
                        Direction = request.Direction,
                    },
                    cancellationToken).ConfigureAwait(false);
                var paths = shortest is null ? ImmutableArray<CallPath>.Empty : [shortest];
                return new FlowTraceResponse { Paths = paths, Truncated = false };
            }

            var all = await _store.FindAllPathsAsync(
                new PathQuery
                {
                    From = request.From,
                    To = request.To,
                    EdgeKinds = edgeKinds,
                    Direction = request.Direction,
                    MaxDepth = request.MaxDepth,
                    MaxPaths = request.MaxPaths,
                },
                cancellationToken).ConfigureAwait(false);
            return new FlowTraceResponse
            {
                Paths = [.. all],
                Truncated = all.Count >= request.MaxPaths,
            };
        }
    }
}
