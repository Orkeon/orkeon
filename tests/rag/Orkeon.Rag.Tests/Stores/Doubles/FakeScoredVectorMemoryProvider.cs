using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Rag.Tests.Stores.Doubles;

/// <summary>
/// <see cref="FakeMemoryProvider"/> exposing the optional Domain vector capabilities
/// (<see cref="IScoredVectorSearch"/> + <see cref="IBatchUpsert"/>) with real cosine scores
/// and recorded calls. The legacy <see cref="IMemoryProvider.SearchSimilarAsync"/> throws,
/// proving the store prefers the capability path.
/// </summary>
public sealed class FakeScoredVectorMemoryProvider
    : FakeMemoryProvider, IMemoryProvider, IScoredVectorSearch, IBatchUpsert
{
    /// <summary>Number of batches received by <see cref="UpsertBatchAsync"/>.</summary>
    public int BatchUpsertCalls { get; private set; }

    /// <summary>Number of capability searches received.</summary>
    public int ScoredSearchCalls { get; private set; }

    /// <summary>Filter received by the last capability search.</summary>
    public MemoryFilter? LastFilter { get; private set; }

    /// <summary>TopK received by the last capability search.</summary>
    public int LastTopK { get; private set; }

    public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ScoredSearchCalls++;
        LastFilter = filter;
        LastTopK = topK;

        var query = embedding.ToArray();
        var results = Items
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

        return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(results);
    }

    public async Task UpsertBatchAsync(
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        BatchUpsertCalls++;

        foreach (var entry in entries)
        {
            if (entry.Embedding is { } embedding)
            {
                await ((IMemoryProvider)this)
                    .StoreWithEmbeddingAsync(entry.Key, entry.Item, embedding.ToArray(), cancellationToken);
            }
            else
            {
                await StoreAsync(entry.Key, entry.Item, cancellationToken);
            }
        }
    }

    /// <summary>Legacy path must never be taken when the capability is available.</summary>
    Task<IReadOnlyList<ScoredMemoryItem>> IMemoryProvider.SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        float minScore,
        Dictionary<string, object>? filter,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            "Legacy SearchSimilarAsync must not be called when IScoredVectorSearch is available.");
    }
}
