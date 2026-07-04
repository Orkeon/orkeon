using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;
using System.Collections.Concurrent;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// In-memory implementation of vector memory store with cosine similarity search.
/// </summary>
public sealed class InMemoryVectorStore : IVectorMemoryStore, IDisposable
{
    private readonly ConcurrentDictionary<string, (MemoryItem Item, EmbeddingVector Vector)> _store = new();
    private readonly ReaderWriterLockSlim _lock = new();

    /// <inheritdoc />
    public Task<IEnumerable<MemoryItem>> SearchSimilarAsync(
        EmbeddingVector query,
        int limit,
        float threshold,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (limit <= 0)
            throw new ArgumentException("Limit must be greater than 0", nameof(limit));

        if (threshold < 0 || threshold > 1)
            throw new ArgumentException("Threshold must be between 0 and 1", nameof(threshold));

        return SearchSimilarCoreAsync(query, limit, threshold, cancellationToken);

        async Task<IEnumerable<MemoryItem>> SearchSimilarCoreAsync(
            EmbeddingVector query, int limit, float threshold, CancellationToken cancellationToken)
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                _lock.EnterReadLock();
                try
                {
                    // Calculate similarities
                    var results = new List<(MemoryItem Item, float Similarity)>();

                    foreach (var kvp in _store)
                    {
                        var (item, vector) = kvp.Value;
                        var similarity = vector.CosineSimilarity(query);

                        if (similarity >= threshold)
                        {
                            results.Add((item, similarity));
                        }
                    }

                    // Sort by similarity and take top results
                    return results
                        .OrderByDescending(r => r.Similarity)
                        .Take(limit)
                        .Select(r => r.Item)
                        .ToList();
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task StoreAsync(
        string id,
        MemoryItem item,
        EmbeddingVector vector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(vector);

        _lock.EnterWriteLock();
        try
        {
            _store[id] = (item, vector);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MemoryItem?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(
                _store.TryGetValue(id, out var entry) ? entry.Item : null);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        _lock.EnterWriteLock();
        try
        {
            return Task.FromResult(_store.TryRemove(id, out _));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(_store.Count);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _lock.EnterWriteLock();
        try
        {
            _store.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <summary>Releases the reader-writer lock guarding the in-memory store.</summary>
    public void Dispose() => _lock.Dispose();
}
