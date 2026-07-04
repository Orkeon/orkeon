using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Search facade of <see cref="LanceDbMemoryProvider"/>. Full-text (BM25) and vector
/// similarity searches are executed and ranked <b>server-side</b> by LanceDB; the
/// hybrid entry point issues both server-side queries and only fuses the two
/// server-ranked candidate lists locally (weighted combination), mirroring the
/// reranking approach of the official LanceDB SDKs.
/// </summary>
public partial class LanceDbMemoryProvider
{
    /// <inheritdoc />
    public override Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IEnumerable<MemoryItem>>(Array.Empty<MemoryItem>());

        return SearchAsyncCore(query, limit, cancellationToken);
    }

    private async Task<IEnumerable<MemoryItem>> SearchAsyncCore(string query, int limit, CancellationToken cancellationToken)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var effectiveLimit = limit > 0 ? limit : _options.DefaultTopK;
            var rows = await _client.QueryAsync(BuildFullTextRequest(query, effectiveLimit), cancellationToken).ConfigureAwait(false);

            var results = rows.Select(r => r.Record.ToMemoryItem()).ToList();
            LogFoundMemoryItemsMatchingQuery(results.Count, query);
            return results;
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchAsync");
            throw;
        }
    }

    /// <summary>
    /// Searches for memory items similar to the provided query embedding vector.
    /// The similarity ranking is computed server-side; the returned <c>_distance</c>
    /// is converted to a score as <c>1 - distance</c> (cosine metric).
    /// Overrides the abstract <see cref="Base.MemoryProviderBase.SearchSimilarAsync"/> so
    /// interface dispatch reaches the server-side vector search instead of resolving the
    /// empty <see cref="IMemoryProvider"/> default body.
    /// </summary>
    public override Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        return SearchSimilarAsyncCore(queryEmbedding, topK, minScore, filter, cancellationToken);
    }

    private async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsyncCore(
        float[] queryEmbedding,
        int topK,
        float minScore,
        Dictionary<string, object>? filter,
        CancellationToken cancellationToken)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var effectiveTopK = topK > 0 ? topK : _options.DefaultTopK;
            var effectiveMinScore = minScore > 0 ? minScore : _options.MinSimilarityScore;

            var rows = await _client.QueryAsync(
                BuildVectorRequest(queryEmbedding, effectiveTopK, filter),
                cancellationToken).ConfigureAwait(false);

            var results = rows
                .Select(r => new ScoredMemoryItem(r.Record.ToMemoryItem(), DistanceToSimilarity(r.Distance)))
                .Where(s => s.Score >= effectiveMinScore)
                .OrderByDescending(s => s.Score)
                .Take(effectiveTopK)
                .ToList();

            LogVectorSearchResults(results.Count, effectiveMinScore, effectiveTopK);
            return results;
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchSimilarAsync");
            throw;
        }
    }

    /// <summary>
    /// Performs a hybrid search: a server-side vector query and a server-side full-text
    /// query are issued, then the two server-ranked candidate lists are fused locally
    /// using the configured <see cref="LanceDbOptions.VectorWeight"/> and
    /// <see cref="LanceDbOptions.FullTextWeight"/>.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="queryText">The text query for full-text matching.</param>
    /// <param name="topK">Maximum number of results.</param>
    /// <param name="minScore">Minimum combined score threshold.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of scored memory items.</returns>
    public Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        float[] queryEmbedding,
        string queryText,
        int topK = 10,
        float minScore = 0.0f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentNullException.ThrowIfNull(queryText);
        return HybridSearchCoreAsync();

        async Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchCoreAsync()
        {
            await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var effectiveTopK = topK > 0 ? topK : _options.DefaultTopK;

                // Overfetch both server-ranked lists so the fusion has enough candidates.
                var fetchK = effectiveTopK * 2;

                var vectorRows = await _client.QueryAsync(
                    BuildVectorRequest(queryEmbedding, fetchK, filter: null),
                    cancellationToken).ConfigureAwait(false);

                IReadOnlyList<LanceDbQueryRow> textRows = [];
                if (!string.IsNullOrWhiteSpace(queryText))
                {
                    textRows = await _client.QueryAsync(
                        BuildFullTextRequest(queryText, fetchK),
                        cancellationToken).ConfigureAwait(false);
                }

                var results = FuseHybridCandidates(vectorRows, textRows, effectiveTopK, minScore);
                LogHybridSearchResults(results.Count, effectiveTopK);
                return results;
            }
            catch (Exception ex)
            {
                LogException(ex, "HybridSearchAsync");
                throw;
            }
        }
    }

    // --- Request builders and fusion helpers ---

    private LanceDbQueryRequest BuildVectorRequest(float[] queryEmbedding, int k, Dictionary<string, object>? filter)
    {
        var predicate = LanceDbFilterBuilder.FromMetadataFilter(filter);
        return new LanceDbQueryRequest
        {
            Vector = new LanceDbQueryVector { SingleVector = queryEmbedding },
            K = k,
            DistanceType = _options.DistanceType,
            VectorColumn = LanceDbArrowCodec.VectorColumn,
            Filter = predicate,
            Prefilter = predicate is null ? null : true
        };
    }

    private static LanceDbQueryRequest BuildFullTextRequest(string query, int k)
    {
        return new LanceDbQueryRequest
        {
            Vector = null,
            K = k,
            FullTextQuery = new LanceDbFullTextQuery
            {
                StringQuery = new LanceDbStringFtsQuery
                {
                    Query = query,
                    Columns = [LanceDbArrowCodec.ContentColumn]
                }
            }
        };
    }

    /// <summary>Converts a server-side cosine distance into a similarity score (1 - distance).</summary>
    private static float DistanceToSimilarity(float? distance)
        => distance.HasValue ? 1f - distance.Value : 0f;

    /// <summary>
    /// Fuses the two server-ranked candidate lists. Vector candidates contribute their
    /// cosine similarity; full-text candidates contribute their BM25 score normalized
    /// by the best score of the list; both are combined with the configured weights.
    /// </summary>
    private List<ScoredMemoryItem> FuseHybridCandidates(
        IReadOnlyList<LanceDbQueryRow> vectorRows,
        IReadOnlyList<LanceDbQueryRow> textRows,
        int topK,
        float minScore)
    {
        var candidates = new Dictionary<string, (LanceDbRecord Record, float VectorScore, float TextScore)>(StringComparer.Ordinal);

        foreach (var row in vectorRows)
        {
            var similarity = DistanceToSimilarity(row.Distance);
            candidates[row.Record.Id] = (row.Record, similarity, 0f);
        }

        var maxTextScore = 0f;
        foreach (var row in textRows)
        {
            if (row.Score is { } score && score > maxTextScore)
                maxTextScore = score;
        }

        foreach (var row in textRows)
        {
            var normalizedText = maxTextScore > 0f && row.Score is { } score ? score / maxTextScore : 0f;
            if (candidates.TryGetValue(row.Record.Id, out var existing))
            {
                candidates[row.Record.Id] = (existing.Record, existing.VectorScore, normalizedText);
            }
            else
            {
                candidates[row.Record.Id] = (row.Record, 0f, normalizedText);
            }
        }

        return candidates.Values
            .Select(c => new
            {
                c.Record,
                c.VectorScore,
                c.TextScore,
                Composite = (_options.VectorWeight * c.VectorScore) + (_options.FullTextWeight * c.TextScore)
            })
            .Where(c => c.Composite >= minScore && (c.VectorScore > 0f || c.TextScore > 0f))
            .OrderByDescending(c => c.Composite)
            .Take(topK)
            .Select(c => new ScoredMemoryItem(c.Record.ToMemoryItem(), c.Composite))
            .ToList();
    }
}
