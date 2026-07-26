using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Rag.Tests.Stores.Doubles;

/// <summary>
/// Hand-rolled spy implementing <see cref="ICollectionAwareMemory"/> over per-collection
/// dictionaries: records the collections, keys and filters it receives so tests can prove
/// the document store's native path passes the collection to the provider (and no prefixed
/// keys). The base <see cref="IMemoryProvider"/> members (inherited from
/// <see cref="FakeMemoryProvider"/>) must never be touched on the native path.
/// </summary>
public class FakeCollectionAwareMemoryProvider : FakeMemoryProvider, ICollectionAwareMemory
{
    /// <summary>Per-collection backing storage, exposed for direct inspection.</summary>
    public Dictionary<string, Dictionary<string, MemoryItem>> Collections { get; } = new(StringComparer.Ordinal);

    /// <summary>Collections received by <see cref="UpsertBatchAsync"/> calls, in order.</summary>
    public List<string> UpsertCollections { get; } = [];

    /// <summary>Every key received by collection-scoped writes.</summary>
    public List<string> ReceivedKeys { get; } = [];

    /// <summary>Collection received by the last scoped search.</summary>
    public string? LastSearchCollection { get; private set; }

    /// <summary>Filter received by the last scoped search.</summary>
    public MemoryFilter? LastSearchFilter { get; private set; }

    /// <summary>Collection received by the last <see cref="DeleteByFilterAsync"/> call.</summary>
    public string? LastDeleteCollection { get; private set; }

    /// <summary>Filter received by the last <see cref="DeleteByFilterAsync"/> call.</summary>
    public MemoryFilter? LastDeleteFilter { get; private set; }

    /// <summary>Collections dropped via <see cref="DropCollectionAsync"/>.</summary>
    public List<string> DroppedCollections { get; } = [];

    public System.Threading.Tasks.Task StoreWithEmbeddingAsync(
        string collection,
        string key,
        MemoryItem item,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        Bucket(collection)[key] = WithEmbedding(item, embedding);
        ReceivedKeys.Add(key);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task UpsertBatchAsync(
        string collection,
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        UpsertCollections.Add(collection);

        var bucket = Bucket(collection);
        foreach (var entry in entries)
        {
            bucket[entry.Key] = entry.Embedding is { } embedding
                ? WithEmbedding(entry.Item, embedding)
                : entry.Item;
            ReceivedKeys.Add(entry.Key);
        }

        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        string collection,
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        LastSearchCollection = collection;
        LastSearchFilter = filter;

        var query = embedding.ToArray();
        IReadOnlyList<ScoredMemoryItem> results = Bucket(collection)
            .Where(pair => pair.Value.Embedding is { Count: > 0 } stored
                && stored.Count == query.Length
                && (filter is null || filter.Matches(pair.Value)))
            .Select(pair => new ScoredMemoryItem(
                pair.Value,
                VectorMath.CosineSimilarity(query, [.. pair.Value.Embedding!]),
                pair.Key))
            .Where(scored => scored.Score >= minScore)
            .OrderByDescending(scored => scored.Score)
            .Take(topK)
            .ToList();

        return System.Threading.Tasks.Task.FromResult(results);
    }

    public System.Threading.Tasks.Task DeleteByFilterAsync(
        string collection,
        MemoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LastDeleteCollection = collection;
        LastDeleteFilter = filter;

        var bucket = Bucket(collection);
        foreach (var key in bucket.Where(pair => filter.Matches(pair.Value)).Select(pair => pair.Key).ToList())
            bucket.Remove(key);

        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task DropCollectionAsync(
        string collection,
        CancellationToken cancellationToken = default)
    {
        DroppedCollections.Add(collection);
        Collections.Remove(collection);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Copies an item with the given embedding (SetEmbedding is Domain-internal).</summary>
    private static MemoryItem WithEmbedding(MemoryItem item, ReadOnlyMemory<float> embedding)
    {
        return MemoryItem.Create(
            item.Content,
            embedding.ToArray(),
            item.Importance,
            item.Source,
            item.Tags,
            customProperties: item.Metadata.CustomProperties is { } custom
                ? new Dictionary<string, string>(custom)
                : null);
    }

    private Dictionary<string, MemoryItem> Bucket(string collection)
    {
        if (!Collections.TryGetValue(collection, out var bucket))
        {
            bucket = new Dictionary<string, MemoryItem>(StringComparer.Ordinal);
            Collections[collection] = bucket;
        }

        return bucket;
    }
}
