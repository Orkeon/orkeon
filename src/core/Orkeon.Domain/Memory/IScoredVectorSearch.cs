namespace Orkeon.Domain.Memory;

/// <summary>
/// Optional capability of an <see cref="IMemoryProvider"/>: vector similarity search with
/// similarity scores guaranteed end to end and a typed metadata filter (RAG-02/C4, plan §4.2).
/// </summary>
/// <remarks>
/// <para>
/// Capability interfaces are opt-in: a provider implements this interface only when it can
/// honour the contract natively. Consumers discover capabilities with the
/// <see cref="MemoryCapabilityExtensions.TryGetCapability{TCapability}"/> extension (which is
/// decorator-aware) or, on a concrete provider, plain pattern matching
/// (<c>provider is IScoredVectorSearch s</c>). Providers without this capability are handled
/// by consumers with a graceful fallback (local cosine recomputation or rank-derived scores).
/// </para>
/// </remarks>
public interface IScoredVectorSearch
{
    /// <summary>
    /// Searches for the <paramref name="topK"/> items most similar to
    /// <paramref name="embedding"/>, returning results ordered by descending score.
    /// </summary>
    /// <param name="embedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return (must be positive).</param>
    /// <param name="minScore">
    /// Minimum similarity score threshold, applied as given — implementations must not
    /// substitute configuration defaults for an explicit value (including <c>0</c>).
    /// </param>
    /// <param name="filter">Optional typed metadata filter; <see langword="null"/> matches all items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The matching items ordered by descending <see cref="ScoredMemoryItem.Score"/>, each with
    /// a meaningful score and, when the store can recover it, the storage
    /// <see cref="ScoredMemoryItem.Key"/>.
    /// </returns>
    System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default);
}
