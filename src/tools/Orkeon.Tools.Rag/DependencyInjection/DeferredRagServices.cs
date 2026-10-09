using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Tools.Rag.DependencyInjection;

/// <summary>
/// The RAG services behind the agent tools, resolved at the tool's first call rather than
/// when the tool is built (GAP-02). A tool registry builds every registered tool for every
/// crew, and every runner host registers these three: resolving the pipelines there
/// resolved the document store, its backing provider and the embedding provider for crews
/// that never search — and an unknown <c>Orkeon:Rag:Provider</c> failed them all.
/// </summary>
internal sealed class DeferredRagPipeline : IRagPipeline
{
    private readonly Lazy<IRagPipeline> _inner;

    public DeferredRagPipeline(Func<IRagPipeline> resolve) => _inner = new(resolve);

    public Task<RagAnswer> QueryAsync(RagQuery query, CancellationToken cancellationToken = default)
        => _inner.Value.QueryAsync(query, cancellationToken);
}

/// <inheritdoc cref="DeferredRagPipeline"/>
internal sealed class DeferredIngestionPipeline : IIngestionPipeline
{
    private readonly Lazy<IIngestionPipeline> _inner;

    public DeferredIngestionPipeline(Func<IIngestionPipeline> resolve) => _inner = new(resolve);

    public Task<IngestionReport> IngestAsync(IngestionRequest request, CancellationToken cancellationToken = default)
        => _inner.Value.IngestAsync(request, cancellationToken);
}

/// <inheritdoc cref="DeferredRagPipeline"/>
internal sealed class DeferredRagEvalHarness : IRagEvalHarness
{
    private readonly Lazy<IRagEvalHarness> _inner;

    public DeferredRagEvalHarness(Func<IRagEvalHarness> resolve) => _inner = new(resolve);

    public Task<RagEvalRunResult> RunAsync(RagEvalRunRequest request, CancellationToken cancellationToken = default)
        => _inner.Value.RunAsync(request, cancellationToken);
}

/// <summary>Marks a service collection the RAG tools were already added to.</summary>
internal sealed class RagToolsRegistrationMarker
{
    public static readonly RagToolsRegistrationMarker Instance = new();
}
