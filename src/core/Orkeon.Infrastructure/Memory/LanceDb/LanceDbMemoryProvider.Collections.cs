using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Native collection support of <see cref="LanceDbMemoryProvider"/> (RAG-03/C2, plan §6.2):
/// each logical collection maps to a dedicated LanceDB table (created lazily with the same
/// fixed Arrow schema as the default table), so collection isolation is enforced
/// server-side instead of by key prefixes. The single-table members of the base provider
/// keep operating on <see cref="LanceDbOptions.TableName"/> unchanged.
/// </summary>
/// <remarks>
/// Custom metadata properties already round-trip through the <c>metadata_json</c> column
/// (<see cref="LanceDbRecord"/>), so collection-scoped reads restore them without any
/// mapping change. Collection tables are created without the optional full-text index
/// (collection-scoped access is vector-first); typed filters compile to server-side SQL
/// predicates via <see cref="LanceDbFilterBuilder.FromMemoryFilter"/>.
/// </remarks>
public partial class LanceDbMemoryProvider : ICollectionAwareMemory
{
    /// <summary>Tables already verified/created by this instance, one entry per collection.</summary>
    private readonly ConcurrentDictionary<string, bool> _initializedCollectionTables = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task StoreWithEmbeddingAsync(
        string collection,
        string key,
        MemoryItem item,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ValidateKey(key);
        ValidateMemoryItem(item);
        if (embedding.IsEmpty)
            throw new ArgumentException("The embedding vector must be non-empty.", nameof(embedding));

        return UpsertScopedAsync(
            collection,
            [new MemoryUpsertEntry(key, item, embedding)],
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Validate-first: every entry is checked, then the whole batch is encoded as a single
    /// Arrow IPC payload and sent in one <c>merge_insert</c> request — an invalid entry
    /// (including an embedding dimension mismatch) never results in a partially applied
    /// batch.
    /// </remarks>
    public Task UpsertBatchAsync(
        string collection,
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            return Task.CompletedTask;

        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ValidateKey(entry.Key);
            ValidateMemoryItem(entry.Item);
        }

        return UpsertScopedAsync(collection, entries, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        string collection,
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        await EnsureCollectionTableAsync(collection, cancellationToken).ConfigureAwait(false);

        try
        {
            var predicate = LanceDbFilterBuilder.FromMemoryFilter(filter);
            var request = new LanceDbQueryRequest
            {
                Vector = new LanceDbQueryVector { SingleVector = embedding.ToArray() },
                K = topK,
                DistanceType = _options.DistanceType,
                VectorColumn = LanceDbArrowCodec.VectorColumn,
                Filter = predicate,
                Prefilter = predicate is null ? null : true
            };

            var rows = await _client.QueryAsync(collection, request, cancellationToken).ConfigureAwait(false);

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
            LogException(ex, "SearchSimilarWithScoresAsync (collection-scoped)");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteByFilterAsync(
        string collection,
        MemoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.IsEmpty)
        {
            throw new ArgumentException(
                "The filter must carry at least one criterion — use DropCollectionAsync to remove " +
                "a whole collection.", nameof(filter));
        }

        await EnsureCollectionTableAsync(collection, cancellationToken).ConfigureAwait(false);

        try
        {
            var predicate = LanceDbFilterBuilder.FromMemoryFilter(filter)!;
            await _client.DeleteRowsAsync(collection, predicate, cancellationToken).ConfigureAwait(false);
            LogDeletedByFilterFromTable(collection);
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteByFilterAsync (collection-scoped)");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DropCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        try
        {
            await _client.DropTableAsync(collection, cancellationToken).ConfigureAwait(false);
            _initializedCollectionTables.TryRemove(collection, out _);
            LogDroppedTable(collection);
        }
        catch (Exception ex)
        {
            LogException(ex, "DropCollectionAsync");
            throw;
        }
    }

    // --- Collection-scoped helpers ---

    private async Task UpsertScopedAsync(
        string collection,
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken)
    {
        await EnsureCollectionTableAsync(collection, cancellationToken).ConfigureAwait(false);

        try
        {
            var records = new List<LanceDbRecord>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry.Embedding is { } embedding)
                    entry.Item.SetEmbedding(embedding.ToArray());

                records.Add(LanceDbRecord.FromMemoryItem(entry.Key, entry.Item));
            }

            var payload = LanceDbArrowCodec.EncodeRecords(records, _options.EmbeddingDimension);
            await _client.MergeInsertAsync(collection, payload, cancellationToken).ConfigureAwait(false);
            LogUpsertedIntoTable(records.Count, collection);
        }
        catch (Exception ex)
        {
            LogException(ex, "UpsertBatchAsync (collection-scoped)");
            throw;
        }
    }

    /// <summary>
    /// Lazily ensures the collection's table exists, creating it (with the fixed Arrow
    /// schema, no full-text index) on first use of this instance.
    /// </summary>
    private async Task EnsureCollectionTableAsync(string collection, CancellationToken cancellationToken)
    {
        if (_initializedCollectionTables.ContainsKey(collection))
            return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initializedCollectionTables.ContainsKey(collection))
                return;

            if (!await _client.TableExistsAsync(collection, cancellationToken).ConfigureAwait(false))
            {
                var emptyTablePayload = LanceDbArrowCodec.EncodeRecords([], _options.EmbeddingDimension);
                try
                {
                    await _client.CreateTableAsync(collection, emptyTablePayload, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // Another instance may have created the table concurrently.
                    if (!await _client.TableExistsAsync(collection, cancellationToken).ConfigureAwait(false))
                        throw;

                    LogTableCreationRaceLost(collection);
                }
            }

            _initializedCollectionTables[collection] = true;
            LogEnsuredTableExists(collection);
        }
        finally
        {
            _initLock.Release();
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Upserted {Count} memory items into LanceDB table: {Table}")]
    private partial void LogUpsertedIntoTable(int count, string table);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Deleted memory items by filter from LanceDB table: {Table}")]
    private partial void LogDeletedByFilterFromTable(string table);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Dropped LanceDB table: {Table}")]
    private partial void LogDroppedTable(string table);
}
