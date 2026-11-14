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
using Orkeon.Tools.Analysis.Internal;

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
    public override string Description => "How does control get from this symbol to that one? Returns the shortest call path from from to to, or with include_all_paths every path up to max_depth hops and max_paths paths (20), truncated when more exist; without to, the paths from from. edge_kinds takes calls by default. For the neighbourhood of one symbol, sub_graph.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    // A small model fills every optional field and sends 0: refuse it rather than return an empty result.
    protected override string? ValidateTypedRequest(FlowTraceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxDepth < 1)
            return $"max_depth must be at least 1 (got {request.MaxDepth}); omit it for the default of 4";
        if (request.MaxPaths < 1)
            return $"max_paths must be at least 1 (got {request.MaxPaths}); omit it for the default of 20";
        return null;
    }

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
            var maxPaths = request.MaxPaths;

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
                    // One path past the page: truncated says that more paths exist, never that the page is full (#283).
                    MaxPaths = PageProbe.OnePast(maxPaths),
                },
                cancellationToken).ConfigureAwait(false);
            return new FlowTraceResponse
            {
                Paths = [.. all.Take(maxPaths)],
                Truncated = all.Count > maxPaths,
            };
        }
    }
}
