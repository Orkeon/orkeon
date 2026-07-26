using System.Net.Http.Json;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.Pinecone;

/// <summary>
/// Optional vector capability of <see cref="PineconeMemoryProvider"/> on its default
/// namespace (RAG-04/C2, reliquat RAG-02): <see cref="IScoredVectorSearch"/> — Pinecone's
/// native match scores preserved end to end (similarity space of the index metric), typed
/// metadata filter, and storage keys populated from the match ids. The collection-scoped
/// twin already lives in the <see cref="ICollectionAwareMemory"/> partial; this member
/// targets the configured default namespace, mirroring the legacy single-namespace members.
/// </summary>
public partial class PineconeMemoryProvider : IScoredVectorSearch
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

        try
        {
            var payload = new
            {
                vector = embedding.ToArray(),
                topK,
                includeMetadata = true,
                includeValues = true,
                @namespace = _options.Namespace,
                filter = BuildScopedFilter(filter)
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<PineconeQueryResponse>(s_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (result?.Matches == null || result.Matches.Count == 0)
                return Array.Empty<ScoredMemoryItem>();

            var scored = new List<ScoredMemoryItem>(result.Matches.Count);
            foreach (var match in result.Matches)
            {
                // minScore is applied as given (capability contract) — no provider default.
                if (match.Score < minScore)
                    continue;

                var item = BuildScopedMemoryItem(match);
                if (item != null)
                    scored.Add(new ScoredMemoryItem(item, match.Score, match.Id));
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
