using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Hand-rolled in-memory <see cref="IDocumentStore"/>. Upserts are keyed by
/// <c>Chunk.Id</c> per collection. Search scores by cosine similarity when the
/// query carries an embedding, otherwise by case-insensitive substring match
/// (1.0 on hit, 0.0 otherwise); results honor metadata filters and
/// <c>TopK</c>, best first.
/// </summary>
public sealed class FakeDocumentStore : IDocumentStore
{
    private readonly Dictionary<string, Dictionary<string, EmbeddedChunk>> _collections =
        new(StringComparer.Ordinal);

    /// <summary>Collections touched by <see cref="UpsertAsync"/>, in call order.</summary>
    public List<string> UpsertedCollections { get; } = new();

    /// <summary>Queries received by <see cref="SearchAsync"/>, in call order.</summary>
    public List<RetrievalQuery> SearchCalls { get; } = new();

    /// <summary>Number of chunks currently stored in <paramref name="collection"/>.</summary>
    public int Count(string collection) =>
        _collections.TryGetValue(collection, out var chunks) ? chunks.Count : 0;

    /// <summary>Snapshot of the chunks currently stored in <paramref name="collection"/>.</summary>
    public IReadOnlyList<EmbeddedChunk> GetCollection(string collection) =>
        _collections.TryGetValue(collection, out var chunks)
            ? chunks.Values.ToList()
            : [];

    /// <inheritdoc />
    public Task UpsertAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(chunks);

        UpsertedCollections.Add(collection);
        if (!_collections.TryGetValue(collection, out var store))
        {
            store = new Dictionary<string, EmbeddedChunk>(StringComparer.Ordinal);
            _collections[collection] = store;
        }

        foreach (var chunk in chunks)
        {
            store[chunk.Chunk.Id] = chunk;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string collection,
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(query);

        SearchCalls.Add(query);

        if (!_collections.TryGetValue(collection, out var store))
        {
            return Task.FromResult<IReadOnlyList<ScoredChunk>>([]);
        }

        IReadOnlyList<ScoredChunk> results = store.Values
            .Where(embedded => MatchesFilters(embedded, query))
            .Select(embedded => new ScoredChunk
            {
                Chunk = embedded.Chunk,
                Score = ComputeScore(embedded, query),
                ScoreOrigin = query.Embedding is not null ? "vector" : "text",
            })
            .OrderByDescending(scored => scored.Score)
            .Take(query.TopK)
            .ToList();

        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task DeleteBySourceAsync(
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(sourceId);

        if (_collections.TryGetValue(collection, out var store))
        {
            var doomed = store.Values
                .Where(embedded => string.Equals(embedded.Chunk.SourceId, sourceId, StringComparison.Ordinal))
                .Select(embedded => embedded.Chunk.Id)
                .ToList();
            foreach (var id in doomed)
            {
                store.Remove(id);
            }
        }

        return Task.CompletedTask;
    }

    private static bool MatchesFilters(EmbeddedChunk embedded, RetrievalQuery query)
    {
        foreach (var (key, value) in query.Filters)
        {
            if (!embedded.Chunk.Metadata.TryGetValue(key, out var actual)
                || !string.Equals(actual, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static double ComputeScore(EmbeddedChunk embedded, RetrievalQuery query)
    {
        if (query.Embedding is { } queryVector)
        {
            return CosineSimilarity(queryVector.AsSpan(), embedded.Embedding.AsSpan());
        }

        return embedded.Chunk.Content.Contains(query.Text, StringComparison.OrdinalIgnoreCase)
            ? 1.0
            : 0.0;
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0.0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
