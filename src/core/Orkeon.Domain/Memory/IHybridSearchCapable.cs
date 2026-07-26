namespace Orkeon.Domain.Memory;

/// <summary>
/// Optional capability of an <see cref="IMemoryProvider"/>: native hybrid search combining
/// full-text relevance and vector similarity in a single query (RAG-02/C4, plan §4.2).
/// </summary>
/// <remarks>
/// <para>
/// Capability interfaces are opt-in: a provider implements this interface only when the
/// backing store fuses text and vector ranking natively (e.g. LanceDB BM25/FTS + KNN).
/// Consumers discover capabilities with the
/// <see cref="MemoryCapabilityExtensions.TryGetCapability{TCapability}"/> extension (which is
/// decorator-aware) or, on a concrete provider, plain pattern matching. Providers without
/// this capability are handled by consumers with an emulated hybrid path (in-process BM25 +
/// reciprocal-rank fusion over the vector results).
/// </para>
/// </remarks>
public interface IHybridSearchCapable
{
    /// <summary>
    /// Performs a native hybrid (text + vector) search, returning results ordered by
    /// descending fused score.
    /// </summary>
    /// <param name="query">The full-text query.</param>
    /// <param name="embedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return (must be positive).</param>
    /// <param name="filter">Optional typed metadata filter; <see langword="null"/> matches all items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The matching items ordered by descending fused <see cref="ScoredMemoryItem.Score"/>,
    /// each with the storage <see cref="ScoredMemoryItem.Key"/> when recoverable.
    /// </returns>
    System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a native hybrid (text + vector) search scoped to a logical collection
    /// (RAG-04/C2). Providers that also implement <see cref="ICollectionAwareMemory"/>
    /// (where chunks live in real per-collection containers, not the default one) must
    /// override this member so hybrid search reaches the collection's native container;
    /// the default body throws <see cref="NotSupportedException"/> so a missing override
    /// fails loudly instead of silently searching the wrong container.
    /// </summary>
    /// <param name="collection">The logical collection name (non-empty), same contract as <see cref="ICollectionAwareMemory"/>.</param>
    /// <param name="query">The full-text query.</param>
    /// <param name="embedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return (must be positive).</param>
    /// <param name="filter">Optional typed metadata filter; <see langword="null"/> matches all items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The matching items of the collection ordered by descending fused
    /// <see cref="ScoredMemoryItem.Score"/>, each with the storage
    /// <see cref="ScoredMemoryItem.Key"/> when recoverable.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// The provider does not support collection-scoped hybrid search (default body).
    /// </exception>
    System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string collection,
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support collection-scoped hybrid search. " +
            "Providers implementing ICollectionAwareMemory must override this member; " +
            "consumers should fall back to the emulated hybrid path (in-process BM25 + RRF).");
}
