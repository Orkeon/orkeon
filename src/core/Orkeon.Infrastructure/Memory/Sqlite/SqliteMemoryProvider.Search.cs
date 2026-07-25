using Orkeon.Domain.Constants.Memory;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Memory.Sqlite;

/// <summary>
/// Search facade of <see cref="SqliteMemoryProvider"/>: full-text (<c>LIKE</c>) and
/// cosine vector similarity entry points. SQLite has no native vector index, so
/// embedding-bearing rows are streamed out of the table and scored in memory with
/// <see cref="VectorMath.CosineSimilarity(ReadOnlySpan{float}, ReadOnlySpan{float})"/> —
/// the same brute-force strategy as the in-memory and embedded-file providers.
/// </summary>
public sealed partial class SqliteMemoryProvider
{
    /// <inheritdoc />
    public override Task<IEnumerable<MemoryItem>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return SearchAsyncCore(query, limit, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragments are the {_options.TableName} identifier (validated at construction via ValidateTableName regex ^[A-Za-z_][A-Za-z0-9_]*$; identifiers cannot be parameterized) and the {ColumnList} const; all caller values are passed as command parameters.")]
    private async Task<IEnumerable<MemoryItem>> SearchAsyncCore(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var effectiveLimit = limit > 0 ? limit : _options.DefaultTopK;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();

            if (string.IsNullOrWhiteSpace(query))
            {
                // Empty query returns all items (same semantics as InMemoryProvider).
                cmd.CommandText = $"SELECT {ColumnList} FROM {_options.TableName} ORDER BY created_at DESC LIMIT @limit";
            }
            else
            {
                // SQLite LIKE is case-insensitive for ASCII by default.
                cmd.CommandText = $"""
                    SELECT {ColumnList} FROM {_options.TableName}
                    WHERE content LIKE @pattern ESCAPE '\'
                    ORDER BY created_at DESC
                    LIMIT @limit
                    """;
                cmd.Parameters.AddWithValue("@pattern", $"%{EscapeLikePattern(query)}%");
            }

            cmd.Parameters.AddWithValue("@limit", effectiveLimit);

            var results = new List<MemoryItem>();
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(ReadRecord(reader).ToMemoryItem());
            }

            LogFoundMemoryItems(results.Count, query);
            return results;
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchAsync");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Searches for memory items similar to the provided query embedding vector
    /// (overrides the abstract <see cref="Base.MemoryProviderBase.SearchSimilarAsync"/> so
    /// interface dispatch performs a real cosine-similarity search instead of resolving
    /// the empty <see cref="IMemoryProvider"/> default body).
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results (provider default when non-positive).</param>
    /// <param name="minScore">Minimum cosine similarity score threshold.</param>
    /// <param name="filter">Optional metadata filter (<c>source</c>, <c>tag</c>/<c>tags</c>, custom properties).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching items scored by cosine similarity, best first.</returns>
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

    private Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsyncCore(
        float[] queryEmbedding,
        int topK,
        float minScore,
        Dictionary<string, object>? filter,
        CancellationToken cancellationToken)
    {
        // Legacy semantics preserved: non-positive thresholds fall back to the configured
        // provider defaults, and the dictionary filter is matched at record level.
        var effectiveTopK = topK > 0 ? topK : _options.DefaultTopK;
        var effectiveMinScore = minScore > 0 ? minScore : _options.MinSimilarityScore;

        return ScoreEmbeddingRowsAsync(
            queryEmbedding,
            effectiveTopK,
            effectiveMinScore,
            recordFilter: filter != null ? record => record.MatchesFilter(filter) : null,
            itemFilter: null,
            cancellationToken);
    }

    /// <summary>
    /// Shared brute-force scoring core: streams all embedding-bearing rows, applies the
    /// optional record-level then item-level filters, scores with cosine similarity, applies
    /// the threshold and returns the top-K best-first — with storage keys preserved.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "The only interpolated fragments are the {_options.TableName} identifier (validated at construction via ValidateTableName regex ^[A-Za-z_][A-Za-z0-9_]*$; identifiers cannot be parameterized) and the {ColumnList} const; no caller values are interpolated.")]
    private async Task<IReadOnlyList<ScoredMemoryItem>> ScoreEmbeddingRowsAsync(
        float[] queryEmbedding,
        int topK,
        float minScore,
        Func<SqliteMemoryRecord, bool>? recordFilter,
        Func<MemoryItem, bool>? itemFilter,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT {ColumnList} FROM {_options.TableName} WHERE embedding IS NOT NULL";

            var scored = new List<ScoredMemoryItem>();
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = ReadRecord(reader);

                if (record.Embedding == null || record.Embedding.Length != queryEmbedding.Length)
                    continue;

                if (recordFilter != null && !recordFilter(record))
                    continue;

                var score = VectorMath.CosineSimilarity(queryEmbedding, record.Embedding);
                if (score < minScore)
                    continue;

                var item = record.ToMemoryItem();
                if (itemFilter != null && !itemFilter(item))
                    continue;

                scored.Add(new ScoredMemoryItem(item, score, record.Key));
            }

            var results = scored
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .ToList();

            LogVectorSearchResults(results.Count, minScore, topK);
            return results;
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchSimilarAsync");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Escapes SQL <c>LIKE</c> wildcards so user queries are matched literally.
    /// </summary>
    private static string EscapeLikePattern(string query) =>
        query
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
