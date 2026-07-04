using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Analysis;

public sealed class IndexStatusTool : ToolBase<IndexStatusRequest, IndexStatusResponse>
{
    private readonly IRaggableStore _store;

    public IndexStatusTool(IRaggableStore store, ILogger<IndexStatusTool>? logger = null) : base(logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override string Name => "index_status";
    public override string Description => "List every virtual root currently indexed in the RaggableTree store.";

    protected override Task<IndexStatusResponse> ExecuteTypedAsync(IndexStatusRequest request, CancellationToken cancellationToken)
    {
        var roots = _store.GetIndexedRoots()
            .Select(r => new IndexedRootDto(r.VirtualRoot, r.IndexedAt, r.NodeCount, r.EdgeCount, r.LanguageSummary))
            .ToImmutableList();
        return Task.FromResult(new IndexStatusResponse { Roots = roots });
    }
}
