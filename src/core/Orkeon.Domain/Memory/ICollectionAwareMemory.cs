namespace Orkeon.Domain.Memory;

/// <summary>
/// Optional capability of an <see cref="IMemoryProvider"/>: native, collection-scoped
/// storage and retrieval (RAG-03/C2, plan §6.2). Providers implement this interface when
/// the backing store has a first-class container concept — ChromaDB collections, Pinecone
/// namespaces, LanceDB tables — so a logical collection maps to a real store container
/// instead of a key-prefix convention.
/// </summary>
/// <remarks>
/// <para>
/// Capability interfaces are opt-in: a provider implements this interface only when it can
/// honour the contract natively. Consumers discover capabilities with the
/// <see cref="MemoryCapabilityExtensions.TryGetCapability{TCapability}"/> extension (which is
/// decorator-aware) or, on a concrete provider, plain pattern matching. Providers without
/// this capability are handled by consumers with the prefixed-key scheme
/// (<c>rag:{collection}:{sourceHash}:{chunkIndex}</c> plus a collection metadata filter).
/// </para>
/// <para>
/// Contract common to every member: <c>collection</c> is the logical collection name,
/// required non-empty; keys are scoped to their collection (the same key may exist in two
/// collections without colliding); collections are created lazily on first write.
/// Implementations map the name to their native container verbatim — callers should stick
/// to portable names (letters, digits, <c>-</c>, <c>_</c>).
/// </para>
/// </remarks>
public interface ICollectionAwareMemory
{
    /// <summary>
    /// Stores a memory item under <paramref name="key"/> in <paramref name="collection"/>,
    /// associating <paramref name="embedding"/> with the item (upsert-by-key semantics).
    /// </summary>
    /// <param name="collection">The logical collection name (non-empty).</param>
    /// <param name="key">The storage key, unique within the collection (non-empty).</param>
    /// <param name="item">The memory item to store.</param>
    /// <param name="embedding">The embedding vector to associate with the item (non-empty).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task StoreWithEmbeddingAsync(
        string collection,
        string key,
        MemoryItem item,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates all <paramref name="entries"/> in <paramref name="collection"/>
    /// (upsert-by-key). Entries are validated before anything is written, so an invalid
    /// entry never results in a partially applied batch (same contract as
    /// <see cref="IBatchUpsert"/>).
    /// </summary>
    /// <param name="collection">The logical collection name (non-empty).</param>
    /// <param name="entries">The batch entries; every key must be non-empty and every item non-null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">An entry is invalid — nothing is written.</exception>
    System.Threading.Tasks.Task UpsertBatchAsync(
        string collection,
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches <paramref name="collection"/> for the <paramref name="topK"/> items most
    /// similar to <paramref name="embedding"/>, returning results ordered by descending
    /// score (same score contract as <see cref="IScoredVectorSearch"/>: provider scores
    /// end to end, <paramref name="minScore"/> applied as given).
    /// </summary>
    /// <param name="collection">The logical collection name (non-empty).</param>
    /// <param name="embedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return (must be positive).</param>
    /// <param name="minScore">
    /// Minimum similarity score threshold, applied as given — implementations must not
    /// substitute configuration defaults for an explicit value (including <c>0</c>).
    /// </param>
    /// <param name="filter">Optional typed metadata filter; <see langword="null"/> matches all items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The matching items of the collection ordered by descending
    /// <see cref="ScoredMemoryItem.Score"/>, each with the storage
    /// <see cref="ScoredMemoryItem.Key"/> when the store can recover it.
    /// </returns>
    System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        string collection,
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every item of <paramref name="collection"/> matching <paramref name="filter"/>.
    /// </summary>
    /// <param name="collection">The logical collection name (non-empty).</param>
    /// <param name="filter">
    /// The metadata filter selecting the items to delete. Required and must carry at least
    /// one criterion (<see cref="MemoryFilter.IsEmpty"/> is rejected — use
    /// <see cref="DropCollectionAsync"/> to remove a whole collection).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="filter"/> is empty.</exception>
    System.Threading.Tasks.Task DeleteByFilterAsync(
        string collection,
        MemoryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops <paramref name="collection"/> entirely — the native container and all its
    /// items. Dropping a collection that does not exist is a no-op.
    /// </summary>
    /// <param name="collection">The logical collection name (non-empty).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task DropCollectionAsync(
        string collection,
        CancellationToken cancellationToken = default);
}
