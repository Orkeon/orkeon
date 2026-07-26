using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Optional vector capabilities of <see cref="LanceDbMemoryProvider"/> on its default
/// table (RAG-04/C2): <see cref="IScoredVectorSearch"/> — server-side vector search with
/// scores preserved end to end, typed filter, storage keys — and
/// <see cref="IHybridSearchCapable"/> — the formerly out-of-interface
/// <c>HybridSearchAsync</c> exposed through the Domain contract, both on the default
/// table and collection-scoped (per-collection tables, same containers as
/// <see cref="ICollectionAwareMemory"/>).
/// </summary>
/// <remarks>
/// <para>
/// Hybrid fusion mirrors the legacy public entry point: a server-side vector query and a
/// server-side full-text (BM25) query are issued, then the two server-ranked lists are
/// fused locally with <see cref="LanceDbOptions.VectorWeight"/> /
/// <see cref="LanceDbOptions.FullTextWeight"/>.
/// </para>
/// <para>
/// Collection tables are created without an FTS index (collection-scoped access is
/// vector-first); the collection-scoped hybrid entry point lazily creates the index
/// (best-effort, once per collection per instance) when
/// <see cref="LanceDbOptions.CreateFullTextIndexOnInit"/> is enabled. If the index is
/// genuinely missing server-side, the full-text query propagates the server error —
/// never a silent vector-only degradation.
/// </para>
/// </remarks>
public partial class LanceDbMemoryProvider : IScoredVectorSearch, IHybridSearchCapable
{
    /// <summary>Collections whose FTS index creation has already been attempted by this instance.</summary>
    private readonly ConcurrentDictionary<string, bool> _ftsEnsuredCollections = new(StringComparer.Ordinal);

    /// <inheritdoc />
    /// <remarks>
    /// Unlike the legacy <c>SearchSimilarAsync</c>, <paramref name="minScore"/> is applied
    /// as given (no substitution of the configured provider default) and storage keys are
    /// populated from the server rows.
    /// </remarks>
    public async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var rows = await _client.QueryAsync(
                BuildScopedVectorRequest(embedding, topK, filter),
                cancellationToken).ConfigureAwait(false);

            // minScore is applied as given (capability contract) — no provider default.
            return rows
                .Select(r => new ScoredMemoryItem(
                    r.Record.ToMemoryItem(),
                    DistanceToSimilarity(r.Distance),
                    r.Record.Id))
                .Where(s => s.Score >= minScore)
                .OrderByDescending(s => s.Score)
                .ToList();
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchSimilarWithScoresAsync");
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default-table variant: both server-side queries carry the typed
    /// <paramref name="filter"/> compiled to a SQL predicate; the fused ranking is the
    /// weighted combination of the legacy hybrid entry point, with storage keys populated.
    /// </remarks>
    public async Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

        return await HybridQueryAndFuseAsync(
            tableName: null, query, embedding, topK, filter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Collection-scoped variant: targets the collection's dedicated table (same container
    /// as <see cref="ICollectionAwareMemory"/>), lazily ensuring the table and,
    /// best-effort, its FTS index.
    /// </remarks>
    public async Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string collection,
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        await EnsureCollectionTableAsync(collection, cancellationToken).ConfigureAwait(false);
        await EnsureCollectionFtsIndexAsync(collection, cancellationToken).ConfigureAwait(false);

        return await HybridQueryAndFuseAsync(
            collection, query, embedding, topK, filter, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues the vector and full-text server-side queries against
    /// <paramref name="tableName"/> (<see langword="null"/> = default table) and fuses the
    /// two server-ranked candidate lists locally.
    /// </summary>
    private async Task<IReadOnlyList<ScoredMemoryItem>> HybridQueryAndFuseAsync(
        string? tableName,
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter,
        CancellationToken cancellationToken)
    {
        try
        {
            // Overfetch both server-ranked lists so the fusion has enough candidates.
            var fetchK = topK * 2;
            var predicate = LanceDbFilterBuilder.FromMemoryFilter(filter);

            var vectorRequest = BuildScopedVectorRequest(embedding, fetchK, filter);
            var vectorRows = tableName is null
                ? await _client.QueryAsync(vectorRequest, cancellationToken).ConfigureAwait(false)
                : await _client.QueryAsync(tableName, vectorRequest, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<LanceDbQueryRow> textRows = [];
            if (!string.IsNullOrWhiteSpace(query))
            {
                var textRequest = BuildFullTextRequest(query, fetchK, predicate);
                textRows = tableName is null
                    ? await _client.QueryAsync(textRequest, cancellationToken).ConfigureAwait(false)
                    : await _client.QueryAsync(tableName, textRequest, cancellationToken).ConfigureAwait(false);
            }

            var results = FuseHybridCandidates(vectorRows, textRows, topK, minScore: 0f);
            LogHybridSearchResults(results.Count, topK);
            return results;
        }
        catch (Exception ex)
        {
            LogException(ex, tableName is null ? "HybridSearchAsync" : "HybridSearchAsync (collection-scoped)");
            throw;
        }
    }

    /// <summary>Builds a vector query request from a typed <see cref="MemoryFilter"/>.</summary>
    private LanceDbQueryRequest BuildScopedVectorRequest(
        ReadOnlyMemory<float> embedding, int k, MemoryFilter? filter)
    {
        var predicate = LanceDbFilterBuilder.FromMemoryFilter(filter);
        return new LanceDbQueryRequest
        {
            Vector = new LanceDbQueryVector { SingleVector = embedding.ToArray() },
            K = k,
            DistanceType = _options.DistanceType,
            VectorColumn = LanceDbArrowCodec.VectorColumn,
            Filter = predicate,
            Prefilter = predicate is null ? null : true
        };
    }

    /// <summary>
    /// Best-effort, once-per-collection FTS index creation for collection tables (created
    /// without one — collection-scoped access is vector-first). Failures are logged and
    /// swallowed: an existing index makes the request redundant, and a genuinely missing
    /// index surfaces later as a server error on the full-text query.
    /// </summary>
    private async Task EnsureCollectionFtsIndexAsync(string collection, CancellationToken cancellationToken)
    {
        if (!_options.CreateFullTextIndexOnInit || _ftsEnsuredCollections.ContainsKey(collection))
            return;

        try
        {
            await _client.CreateIndexAsync(
                collection,
                LanceDbArrowCodec.ContentColumn,
                LanceDbRestClient.FullTextIndexType,
                cancellationToken).ConfigureAwait(false);
            LogCreatedFullTextIndex(LanceDbArrowCodec.ContentColumn, collection);
        }
        catch (HttpRequestException ex)
        {
            LogFullTextIndexCreationFailed(ex, LanceDbArrowCodec.ContentColumn, collection);
        }

        _ftsEnsuredCollections[collection] = true;
    }
}
