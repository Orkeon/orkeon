using System.Net.Http.Json;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.ChromaDb;

/// <summary>
/// Optional vector capability of <see cref="ChromaDbMemoryProvider"/> on its default
/// collection (RAG-04/C2, reliquat RAG-02): <see cref="IScoredVectorSearch"/> — server-side
/// vector search with native scores preserved end to end (<c>1 - distance</c>, cosine
/// convention), typed metadata filter, and storage keys populated from the response ids.
/// The collection-scoped twin already lives in the <see cref="ICollectionAwareMemory"/>
/// partial; this member delegates to the configured default collection, mirroring the
/// legacy single-collection members.
/// </summary>
public partial class ChromaDbMemoryProvider : IScoredVectorSearch
{
    /// <inheritdoc />
    /// <remarks>
    /// Unlike the legacy <c>SearchSimilarAsync</c>, results restore custom metadata
    /// properties (same mapping as the collection-scoped path) and
    /// <paramref name="minScore"/> is applied as given — no provider default substitution.
    /// </remarks>
    public async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new
            {
                query_embeddings = new[] { embedding.ToArray() },
                n_results = topK,
                where = BuildScopedWhereClause(filter),
                include = s_similarityQueryIncludes
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{_collectionId}/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<ChromaQueryResponse>(s_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (result?.Ids == null || result.Ids.Count == 0 || result.Ids[0].Count == 0)
                return Array.Empty<ScoredMemoryItem>();

            var scored = new List<ScoredMemoryItem>(result.Ids[0].Count);
            for (var i = 0; i < result.Ids[0].Count; i++)
            {
                var item = BuildScopedMemoryItem(result, 0, i);
                if (item == null)
                    continue;

                // minScore is applied as given (capability contract) — no provider default.
                var score = GetSimilarityScore(result, 0, i);
                if (score >= minScore)
                    scored.Add(new ScoredMemoryItem(item, score, result.Ids[0][i]));
            }

            return scored.OrderByDescending(s => s.Score).ToList();
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchSimilarWithScoresAsync");
            throw;
        }
    }
}
