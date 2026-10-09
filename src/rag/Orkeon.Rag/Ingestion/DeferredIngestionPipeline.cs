using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Ingestion;

/// <summary>
/// An <see cref="IIngestionPipeline"/> resolved at its first ingestion rather than when its
/// consumer is built (GAP-02). The crew factory receives the collections bootstrapper for
/// every crew; resolving the real pipeline there resolved the document store, its backing
/// provider and the embedding provider too — so a crew with no <c>rag:</c> block paid for
/// them, and an unknown <c>Orkeon:Rag:Provider</c> failed every crew. The failure now
/// belongs to the first ingestion, with the same message.
/// </summary>
internal sealed class DeferredIngestionPipeline : IIngestionPipeline
{
    private readonly Lazy<IIngestionPipeline> _inner;

    public DeferredIngestionPipeline(Func<IIngestionPipeline> resolve) => _inner = new(resolve);

    public Task<IngestionReport> IngestAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
        => _inner.Value.IngestAsync(request, cancellationToken);
}
