using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.Sqlite;

/// <summary>
/// Optional vector capabilities of <see cref="SqliteMemoryProvider"/> (RAG-02/C4):
/// <see cref="IScoredVectorSearch"/> — cosine scores preserved end to end with storage keys
/// and a typed metadata filter — and <see cref="IBatchUpsert"/> — transactional batch upsert.
/// </summary>
public sealed partial class SqliteMemoryProvider : IScoredVectorSearch, IBatchUpsert
{
    /// <inheritdoc />
    /// <remarks>
    /// Unlike the legacy <c>SearchSimilarAsync</c>, the caller's <paramref name="minScore"/>
    /// is applied as given (no substitution of the configured provider default), so scores
    /// are governed solely by the capability contract.
    /// </remarks>
    public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTopK = topK > 0 ? topK : _options.DefaultTopK;

        return ScoreEmbeddingRowsAsync(
            embedding.ToArray(),
            effectiveTopK,
            minScore,
            recordFilter: null,
            itemFilter: filter != null && !filter.IsEmpty ? filter.Matches : null,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Atomicity: all entries are validated up front, then the whole batch is written inside
    /// a single SQLite transaction — either every entry is persisted or none is.
    /// </remarks>
    public Task UpsertBatchAsync(
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Validate-first: nothing is written when any entry is invalid.
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ValidateKey(entry.Key);
            ValidateMemoryItem(entry.Item);
        }

        return UpsertBatchCoreAsync(entries, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100",
        Justification = "UpsertSql only interpolates the {_options.TableName} identifier (validated at construction via ValidateTableName regex ^[A-Za-z_][A-Za-z0-9_]*$; identifiers cannot be parameterized) and the {ColumnList} const; all caller values are passed as command parameters.")]
    private async Task UpsertBatchCoreAsync(
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)
                await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var entry in entries)
            {
                if (entry.Embedding is { } embedding)
                    entry.Item.SetEmbedding(embedding.ToArray());

                var record = SqliteMemoryRecord.FromMemoryItem(entry.Key, entry.Item);

                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = UpsertSql;
                AddRecordParameters(cmd, record);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            LogBatchUpserted(entries.Count);
        }
        catch (Exception ex)
        {
            LogException(ex, "UpsertBatchAsync");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
