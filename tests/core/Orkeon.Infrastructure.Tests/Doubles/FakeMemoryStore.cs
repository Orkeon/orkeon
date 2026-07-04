using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Memory.Base;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written in-memory <see cref="MemoryProviderBase"/> double for key rotation tests (R2.8).
/// Lists keys in deterministic ordinal order and supports per-key failure injection on
/// <see cref="StoreAsync"/> / <see cref="DeleteAsync"/> to simulate mid-rotation crashes.
/// </summary>
public sealed class FakeMemoryStore : MemoryProviderBase
{
    private readonly Dictionary<string, MemoryItem> _items = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override string Name => "FakeMemoryStore";

    /// <summary>Keys whose <see cref="StoreAsync"/> calls throw an injected failure.</summary>
    public HashSet<string> FailStoreKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Keys whose <see cref="DeleteAsync"/> calls throw an injected failure.</summary>
    public HashSet<string> FailDeleteKeys { get; } = new(StringComparer.Ordinal);

    // --- Tracking ---
    public int StoreCallCount { get; private set; }
    public int GetCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }
    public int ListKeysCallCount { get; private set; }

    /// <summary>Direct access to the raw stored items for assertions.</summary>
    public IReadOnlyDictionary<string, MemoryItem> Items => _items;

    /// <inheritdoc />
    public override Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        StoreCallCount++;
        if (FailStoreKeys.Contains(key))
            throw new InvalidOperationException($"Injected store failure for key '{key}'.");

        _items[key] = item;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        GetCallCount++;
        _items.TryGetValue(key, out var item);
        return Task.FromResult(item);
    }

    /// <inheritdoc />
    public override Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        if (!_items.ContainsKey(key))
            return Task.FromResult(false);

        _items[key] = item;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public override Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        DeleteCallCount++;
        if (FailDeleteKeys.Contains(key))
            throw new InvalidOperationException($"Injected delete failure for key '{key}'.");

        return Task.FromResult(_items.Remove(key));
    }

    /// <inheritdoc />
    public override Task<IEnumerable<MemoryItem>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<MemoryItem>>(_items.Values.Take(limit).ToList());

    /// <inheritdoc />
    public override Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _items.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<int> CountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_items.Count);

    /// <inheritdoc />
    public override Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        ListKeysCallCount++;
        var keys = _items.Keys
            .OrderBy(static k => k, StringComparer.Ordinal)
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult(keys);
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var scored = new List<ScoredMemoryItem>();
        foreach (var item in _items.Values)
        {
            var embedding = item.Embedding;
            if (embedding == null || embedding.Count != queryEmbedding.Length)
                continue;

            var score = VectorMath.CosineSimilarity(queryEmbedding, embedding.ToArray());
            if (score >= minScore)
                scored.Add(new ScoredMemoryItem(item, score));
        }

        return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList());
    }
}
