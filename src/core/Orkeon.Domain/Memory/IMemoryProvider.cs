using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Interface for memory storage providers.
/// </summary>
public interface IMemoryProvider
{
    /// <summary>
    /// Stores a memory item.
    /// </summary>
    System.Threading.Tasks.Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a memory item.
    /// </summary>
    System.Threading.Tasks.Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for memory items by their text.
    /// </summary>
    /// <param name="query">The text to look for (each provider matches it its own way).</param>
    /// <param name="limit">Maximum number of results, counted after the filter.</param>
    /// <param name="filter">
    /// Optional metadata filter with the semantics of <see cref="SearchSimilarAsync"/>'s:
    /// <c>source</c> equality, <c>tag</c>/<c>tags</c> membership, any other key an equality on the
    /// item's custom property of that name. The provider applies it <b>before</b> the limit, so a
    /// filtered search returns as many matches as the store holds, up to <paramref name="limit"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a memory item.
    /// </summary>
    System.Threading.Tasks.Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all memory items.
    /// </summary>
    System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a memory item with an associated embedding vector.
    /// Default implementation sets the embedding on the item and delegates to StoreAsync.
    /// </summary>
    System.Threading.Tasks.Task StoreWithEmbeddingAsync(
        string key,
        MemoryItem item,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetEmbedding(embedding);
        return StoreAsync(key, item, cancellationToken);
    }

    /// <summary>
    /// Searches for memory items similar to the provided query embedding vector.
    /// Returns items scored by similarity, filtered by minimum score threshold.
    /// Default implementation returns empty results (providers should override for vector search support).
    /// </summary>
    /// <remarks>
    /// Beware the default-interface-method mapping trap: C# does not re-map members of a
    /// derived class onto an interface implemented by its base class. A provider inheriting
    /// from a base that lists this interface must either re-list the interface on its own
    /// declaration or override a virtual/abstract base member — otherwise calls made through
    /// <see cref="IMemoryProvider"/> silently resolve to this empty default body (MAT-017).
    /// In-repo providers derive from <c>MemoryProviderBase</c>, which declares
    /// <c>SearchSimilarAsync</c> as abstract precisely to close that trap.
    /// </remarks>
    System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(Array.Empty<ScoredMemoryItem>());
    }
}
