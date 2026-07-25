using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Rag.Tests.Stores.Doubles;

/// <summary>
/// <see cref="FakeMemoryProvider"/> that additionally overrides the legacy
/// <see cref="IMemoryProvider.SearchSimilarAsync"/> with a real cosine-similarity scan
/// (no <c>IScoredVectorSearch</c> capability). Exercises the store's legacy fallback,
/// whose scores are genuine vector similarities.
/// </summary>
public sealed class FakeLegacyVectorMemoryProvider : FakeMemoryProvider, IMemoryProvider
{
    /// <summary>Number of legacy similarity searches received.</summary>
    public int LegacySearchCalls { get; private set; }

    public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        LegacySearchCalls++;

        var typed = MemoryFilter.FromDictionary(filter);
        var results = Items
            .Where(pair => pair.Value.Embedding is { Count: > 0 } embedding
                && embedding.Count == queryEmbedding.Length
                && (typed is null || typed.Matches(pair.Value)))
            .Select(pair => new ScoredMemoryItem(
                pair.Value,
                VectorMath.CosineSimilarity(queryEmbedding, [.. pair.Value.Embedding!]),
                pair.Key))
            .Where(scored => scored.Score >= minScore)
            .OrderByDescending(scored => scored.Score)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(results);
    }
}
