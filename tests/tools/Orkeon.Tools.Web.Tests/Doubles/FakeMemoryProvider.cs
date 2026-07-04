using Orkeon.Domain.Memory;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IMemoryProvider"/> double for tests. Tracks stored items by key,
/// and lets callers preload the result set returned by <see cref="SearchSimilarAsync"/>.
/// </summary>
public sealed class FakeMemoryProvider : IMemoryProvider
{
    private readonly Dictionary<string, MemoryItem> _store = new(StringComparer.Ordinal);

    public List<ScoredMemoryItem> SearchResults { get; } = [];
    public List<(string Key, MemoryItem Item)> Stored { get; } = [];
    public int StoreCallCount { get; private set; }
    public int SearchSimilarCallCount { get; private set; }
    public int? LastRequestedTopK { get; private set; }

    /// <summary>Preloads an item so a subsequent GetAsync hits it (used to exercise dedup).</summary>
    public void Preload(string key, MemoryItem item) => _store[key] = item;

    public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        StoreCallCount++;
        _store[key] = item;
        Stored.Add((key, item));
        return Task.CompletedTask;
    }

    public Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(key, out var item) ? item : null);

    public Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<MemoryItem>>(_store.Values);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Remove(key));

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _store.Clear();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        SearchSimilarCallCount++;
        LastRequestedTopK = topK;
        return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(SearchResults);
    }
}
