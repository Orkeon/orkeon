using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IMemoryProvider for testing.
/// Uses an in-memory dictionary for storage.
/// </summary>
public sealed class MockMemoryProvider : IMemoryProvider
{
    private readonly Dictionary<string, MemoryItem> _storage = [];
    private IEnumerable<MemoryItem> _searchResult = Array.Empty<MemoryItem>();
    private IReadOnlyList<ScoredMemoryItem> _searchSimilarResult = Array.Empty<ScoredMemoryItem>();
    private Func<string, int, IEnumerable<MemoryItem>>? _searchFunc;

    // --- Tracking ---
    public int StoreCallCount { get; private set; }
    public int GetCallCount { get; private set; }
    public int SearchCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }
    public int ClearCallCount { get; private set; }
    public int StoreWithEmbeddingCallCount { get; private set; }
    public int SearchSimilarCallCount { get; private set; }
    public string? LastStoreKey { get; private set; }
    public MemoryItem? LastStoreItem { get; private set; }
    public string? LastGetKey { get; private set; }
    public string? LastSearchQuery { get; private set; }
    public int? LastSearchLimit { get; private set; }
    public string? LastDeleteKey { get; private set; }
    public float[]? LastStoreEmbedding { get; private set; }
    public float[]? LastSearchSimilarQueryEmbedding { get; private set; }
    public Dictionary<string, object>? LastSearchSimilarFilter { get; private set; }

    private Exception? _searchException;

    // --- Configuration ---
    public void SetSearchResult(IEnumerable<MemoryItem> result) => _searchResult = result;
    public void SetSearchFunc(Func<string, int, IEnumerable<MemoryItem>> func) => _searchFunc = func;
    public void SetSearchSimilarResult(IReadOnlyList<ScoredMemoryItem> result) => _searchSimilarResult = result;

    /// <summary>
    /// Configures SearchAsync to throw the specified exception.
    /// </summary>
    public void SetSearchException(Exception exception) => _searchException = exception;

    /// <summary>
    /// Gets the internal storage dictionary for test assertions.
    /// </summary>
    public IReadOnlyDictionary<string, MemoryItem> Storage => _storage;

    public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        StoreCallCount++;
        LastStoreKey = key;
        LastStoreItem = item;
        _storage[key] = item;
        return Task.CompletedTask;
    }

    public Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        GetCallCount++;
        LastGetKey = key;
        _storage.TryGetValue(key, out var item);
        return Task.FromResult(item);
    }

    public Task<IEnumerable<MemoryItem>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        SearchCallCount++;
        LastSearchQuery = query;
        LastSearchLimit = limit;

        if (_searchException != null)
            throw _searchException;

        var result = _searchFunc != null ? _searchFunc(query, limit) : _searchResult;
        return Task.FromResult(result);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        DeleteCallCount++;
        LastDeleteKey = key;
        return Task.FromResult(_storage.Remove(key));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCallCount++;
        _storage.Clear();
        return Task.CompletedTask;
    }

    public Task StoreWithEmbeddingAsync(
        string key,
        MemoryItem item,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        StoreWithEmbeddingCallCount++;
        LastStoreKey = key;
        LastStoreItem = item;
        LastStoreEmbedding = embedding;
        item.SetEmbedding(embedding);
        _storage[key] = item;
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
        LastSearchSimilarQueryEmbedding = queryEmbedding;
        LastSearchSimilarFilter = filter;
        return Task.FromResult(_searchSimilarResult);
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        StoreCallCount = 0;
        GetCallCount = 0;
        SearchCallCount = 0;
        DeleteCallCount = 0;
        ClearCallCount = 0;
        StoreWithEmbeddingCallCount = 0;
        SearchSimilarCallCount = 0;
        LastStoreKey = null;
        LastStoreItem = null;
        LastGetKey = null;
        LastSearchQuery = null;
        LastSearchLimit = null;
        LastDeleteKey = null;
        LastStoreEmbedding = null;
        LastSearchSimilarQueryEmbedding = null;
        _storage.Clear();
    }
}
