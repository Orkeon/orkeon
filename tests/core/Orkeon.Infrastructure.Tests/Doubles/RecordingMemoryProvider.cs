using System.Collections.Concurrent;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IMemoryProvider"/> around a real one (GAP-30): every call is passed on,
/// and recorded — the stores it counts, each search with its method and its filter, each deletion
/// with its key.
/// </summary>
public sealed class RecordingMemoryProvider(IMemoryProvider inner) : IMemoryProvider
{
    private readonly ConcurrentQueue<RecordedSearch> _searches = new();
    private readonly ConcurrentQueue<string> _stored = new();
    private readonly ConcurrentQueue<string> _deleted = new();

    /// <summary>The key of every write — <c>StoreAsync</c> and <c>StoreWithEmbeddingAsync</c> — in order.</summary>
    public IReadOnlyList<string> Stored => [.. _stored];

    /// <summary>Every search — its method (<c>SearchAsync</c>, <c>SearchSimilarAsync</c>) and filter — in order.</summary>
    public IReadOnlyList<RecordedSearch> Searches => [.. _searches];

    /// <summary>The key of every deletion, in order.</summary>
    public IReadOnlyList<string> Deleted => [.. _deleted];

    public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        _stored.Enqueue(key);
        return inner.StoreAsync(key, item, cancellationToken);
    }

    public Task StoreWithEmbeddingAsync(string key, MemoryItem item, float[] embedding, CancellationToken cancellationToken = default)
    {
        _stored.Enqueue(key);
        return inner.StoreWithEmbeddingAsync(key, item, embedding, cancellationToken);
    }

    public Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        inner.GetAsync(key, cancellationToken);

    public Task<IEnumerable<MemoryItem>> SearchAsync(
        string query, int limit = 10, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default)
    {
        _searches.Enqueue(new RecordedSearch(nameof(SearchAsync), filter));
        return inner.SearchAsync(query, limit, filter, cancellationToken);
    }

    public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding, int topK = 10, float minScore = 0.0f,
        Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default)
    {
        _searches.Enqueue(new RecordedSearch(nameof(SearchSimilarAsync), filter));
        return inner.SearchSimilarAsync(queryEmbedding, topK, minScore, filter, cancellationToken);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _deleted.Enqueue(key);
        return inner.DeleteAsync(key, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => inner.ClearAsync(cancellationToken);

    /// <summary>One recorded search: the method it went through and the filter it carried.</summary>
    public sealed record RecordedSearch(string Method, Dictionary<string, object>? Filter);
}
