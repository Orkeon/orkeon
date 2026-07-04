using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock implementation of IVectorMemoryStore for testing.
/// Uses an in-memory dictionary for storage.
/// </summary>
public sealed class MockVectorMemoryStore : IVectorMemoryStore
{
    private readonly Dictionary<string, (MemoryItem Item, EmbeddingVector Vector)> _storage = [];
    private IEnumerable<MemoryItem> _searchSimilarResult = Array.Empty<MemoryItem>();
    private Func<EmbeddingVector, int, float, IEnumerable<MemoryItem>>? _searchFunc;

    // --- Tracking ---
    public int SearchSimilarCallCount { get; private set; }
    public int StoreCallCount { get; private set; }
    public int RemoveCallCount { get; private set; }
    public int CountCallCount { get; private set; }
    public int ClearCallCount { get; private set; }
    public EmbeddingVector? LastSearchQuery { get; private set; }
    public int? LastSearchLimit { get; private set; }
    public float? LastSearchThreshold { get; private set; }
    public string? LastStoreId { get; private set; }
    public MemoryItem? LastStoreItem { get; private set; }
    public EmbeddingVector? LastStoreVector { get; private set; }
    public string? LastRemoveId { get; private set; }

    // --- Configuration ---
    public void SetSearchSimilarResult(IEnumerable<MemoryItem> result) => _searchSimilarResult = result;
    public void SetSearchFunc(Func<EmbeddingVector, int, float, IEnumerable<MemoryItem>> func) => _searchFunc = func;

    /// <summary>
    /// Gets the internal storage for test assertions.
    /// </summary>
    public IReadOnlyDictionary<string, (MemoryItem Item, EmbeddingVector Vector)> Storage => _storage;

    public Task<IEnumerable<MemoryItem>> SearchSimilarAsync(
        EmbeddingVector query,
        int limit,
        float threshold,
        CancellationToken cancellationToken = default)
    {
        SearchSimilarCallCount++;
        LastSearchQuery = query;
        LastSearchLimit = limit;
        LastSearchThreshold = threshold;

        var result = _searchFunc != null ? _searchFunc(query, limit, threshold) : _searchSimilarResult;
        return Task.FromResult(result);
    }

    public Task StoreAsync(
        string id,
        MemoryItem item,
        EmbeddingVector vector,
        CancellationToken cancellationToken = default)
    {
        StoreCallCount++;
        LastStoreId = id;
        LastStoreItem = item;
        LastStoreVector = vector;
        _storage[id] = (item, vector);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        RemoveCallCount++;
        LastRemoveId = id;
        return Task.FromResult(_storage.Remove(id));
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        CountCallCount++;
        return Task.FromResult(_storage.Count);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCallCount++;
        _storage.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resets all tracking counters and last arguments.
    /// </summary>
    public void Reset()
    {
        SearchSimilarCallCount = 0;
        StoreCallCount = 0;
        RemoveCallCount = 0;
        CountCallCount = 0;
        ClearCallCount = 0;
        LastSearchQuery = null;
        LastSearchLimit = null;
        LastSearchThreshold = null;
        LastStoreId = null;
        LastStoreItem = null;
        LastStoreVector = null;
        LastRemoveId = null;
        _storage.Clear();
    }
}
