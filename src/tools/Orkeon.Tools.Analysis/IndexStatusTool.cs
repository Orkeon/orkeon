using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class IndexStatusTool : ToolBase<IndexStatusRequest, IndexStatusResponse>
{
    private readonly IRaggableStore _store;
    private readonly IIndexInvalidation? _invalidation;

    public IndexStatusTool(
        IRaggableStore store,
        ILogger<IndexStatusTool>? logger = null,
        IIndexInvalidation? invalidation = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _invalidation = invalidation;
    }

    public override string Name => "index_status";
    public override string Description => "List every virtual root currently indexed in the RaggableTree store.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    protected override Task<IndexStatusResponse> ExecuteTypedAsync(IndexStatusRequest request, CancellationToken cancellationToken)
    {
        var roots = _store.GetIndexedRoots()
            .Select(r => new IndexedRootDto(r.VirtualRoot, r.IndexedAt, r.NodeCount, r.EdgeCount, r.LanguageSummary))
            .ToImmutableList();
        // The lazy-freshness debt, made observable (PLAN B3): "stale" used to be a
        // state nothing could report.
        var dirty = _invalidation?.DirtyPaths ?? [];
        return Task.FromResult(new IndexStatusResponse
        {
            Roots = roots,
            DirtyCount = dirty.Count,
            DirtyPaths = [.. dirty.Take(10)],
        });
    }
}
