using Microsoft.Extensions.Logging;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Memory.Base;
using System.Collections.Concurrent;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Simplified in-memory memory provider focused on data storage only.
/// Business logic (similarity search, validation) moved to domain services.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IMemoryProvider"/> is re-listed on purpose (same pattern as
/// <c>SqliteMemoryProvider</c>/<c>LanceDbMemoryProvider</c>, R10.1): without
/// re-implementation, calls made through the interface would resolve
/// <c>StoreWithEmbeddingAsync</c>/<c>SearchSimilarAsync</c> to the interface's default
/// bodies (empty results) instead of the cosine-similarity search defined here
/// (interface mapping is otherwise frozen at <see cref="MemoryProviderBase"/>).
/// </para>
/// <para>
/// Implements the optional vector capabilities <see cref="IScoredVectorSearch"/> and
/// <see cref="IBatchUpsert"/> (RAG-02/C4): keys are recoverable, scores are cosine
/// similarities preserved end to end, and batches are validated before any write.
/// </para>
/// </remarks>
public partial class InMemoryProvider : MemoryProviderBase, IMemoryProvider, IScoredVectorSearch, IBatchUpsert
{
    private readonly ConcurrentDictionary<string, MemoryItem> _storage = new();

    /// <inheritdoc />
    public override string Name => "InMemory";

    /// <summary>Initializes a new instance of <see cref="InMemoryProvider"/>.</summary>
    /// <param name="logger">Optional logger.</param>
    public InMemoryProvider(ILogger<InMemoryProvider>? logger = null)
        : base(logger)
    {
    }

    /// <summary>
    /// Stores a memory item in memory.
    /// Pure data storage - no business logic.
    /// </summary>
    public override Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);

        try
        {
            _storage.AddOrUpdate(key, item, (_, _) => item);
            LogStoredItem(key);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogException(ex, "StoreAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a memory item from memory.
    /// Pure data retrieval - no business logic.
    /// </summary>
    public override Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        try
        {
            _storage.TryGetValue(key, out var item);

            if (item != null)
            {
                LogRetrievedItem(key);
            }
            else
            {
                LogItemNotFound(key);
            }

            return Task.FromResult(item);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Updates a memory item in memory.
    /// Pure data update - no business logic.
    /// Uses ConcurrentDictionary.AddOrUpdate with dual lambdas
    /// to avoid the TOCTOU race between TryGetValue and TryUpdate.
    /// </summary>
    public override Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);

        try
        {
            bool updated = false;
            _storage.AddOrUpdate(key,
                // addValueFactory: key does not exist — mark as failure, return item
                // as placeholder (will be removed immediately after).
                addValueFactory: _ =>
                {
                    updated = false;
                    return item;
                },
                // updateValueFactory: key exists — perform atomic update.
                updateValueFactory: (_, _) =>
                {
                    updated = true;
                    return item;
                });

            if (!updated)
            {
                // The key did not exist; the addValueFactory inserted a placeholder.
                // Remove it so the dictionary stays clean.
                _storage.TryRemove(key, out _);
                LogCannotUpdateNonExistent(key);
                return Task.FromResult(false);
            }

            LogUpdatedItem(key);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogException(ex, "UpdateAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Deletes a memory item from memory.
    /// Pure data deletion - no business logic.
    /// </summary>
    public override Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        try
        {
            var removed = _storage.TryRemove(key, out _);

            LogDeletedItem(key, removed);
            return Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Searches for memories by content matching.
    /// Pure data query - search logic implemented in domain services.
    /// </summary>
    public override Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken cancellationToken = default)
    {
        // Null query should throw ArgumentNullException
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            // Empty query returns all items
            if (string.IsNullOrWhiteSpace(query))
            {
                var allResults = _storage.Values.Take(limit).ToList();
                LogFoundItemsEmpty(allResults.Count);
                return Task.FromResult<IEnumerable<MemoryItem>>(allResults);
            }

            // Simple content matching - complex similarity search handled by domain services
            var results = _storage.Values
                .Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();

            LogFoundItems(results.Count, query);
            return Task.FromResult<IEnumerable<MemoryItem>>(results);
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchAsync");
            throw;
        }
    }

    /// <summary>
    /// Clears all memories.
    /// Pure data operation - no business logic.
    /// </summary>
    public override Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var count = _storage.Count;
            _storage.Clear();

            LogClearedItems(count);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogException(ex, "ClearAsync");
            throw;
        }
    }

    /// <summary>
    /// Counts all memory items.
    /// Pure data query - no business logic.
    /// </summary>
    public override Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var count = _storage.Count;
            LogCountedItems(count);
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            LogException(ex, "CountAsync");
            throw;
        }
    }

    /// <summary>
    /// Lists memory keys with pagination.
    /// Pure data query - no business logic.
    /// </summary>
    public override Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var keys = _storage.Keys
                .Skip(skip)
                .Take(take)
                .ToList();

            LogListedKeys(keys.Count, skip, take);
            return Task.FromResult(keys);
        }
        catch (Exception ex)
        {
            LogException(ex, "ListKeysAsync");
            throw;
        }
    }

    /// <summary>
    /// Stores a memory item with an associated embedding vector.
    /// </summary>
    public Task StoreWithEmbeddingAsync(
        string key,
        MemoryItem item,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetEmbedding(embedding);
        return StoreAsync(key, item, cancellationToken);
    }

    /// <summary>
    /// Searches for memory items similar to the provided query embedding.
    /// Uses cosine similarity via VectorMath for scoring, honoring the optional metadata
    /// <paramref name="filter"/> (<c>source</c> equality, <c>tag</c>/<c>tags</c> membership,
    /// any other key matched against custom properties — same semantics as the Redis and
    /// SQLite providers); the legacy dictionary is converted to a typed <see cref="MemoryFilter"/>.
    /// Overrides the abstract <see cref="MemoryProviderBase.SearchSimilarAsync"/> so calls
    /// made through <see cref="IMemoryProvider"/> dispatch here instead of the interface's
    /// empty default body.
    /// </summary>
    public override Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(
            SearchSimilarCore(queryEmbedding, topK, minScore, MemoryFilter.FromDictionary(filter)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(
            SearchSimilarCore(embedding.ToArray(), topK, minScore, filter));
    }

    /// <summary>
    /// Shared cosine-similarity scan over the store: embedding compatibility check, typed
    /// metadata filter, scoring, threshold, descending order, top-K — keys preserved.
    /// </summary>
    private List<ScoredMemoryItem> SearchSimilarCore(
        float[] queryEmbedding,
        int topK,
        float minScore,
        MemoryFilter? filter)
    {
        try
        {
            var scored = new List<ScoredMemoryItem>();

            foreach (var (key, item) in _storage)
            {
                if (item.Embedding == null || item.Embedding.Count == 0)
                    continue;

                if (item.Embedding.Count != queryEmbedding.Length)
                    continue;

                if (filter != null && !filter.Matches(item))
                    continue;

                var score = VectorMath.CosineSimilarity(queryEmbedding, item.Embedding.ToArray());

                if (score >= minScore)
                {
                    scored.Add(new ScoredMemoryItem(item, score, key));
                }
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
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reasonable atomicity (see <see cref="IBatchUpsert"/>): every entry is validated before
    /// any write, so an invalid entry never yields a partially applied batch. Writes are then
    /// applied per key; concurrent readers may observe the batch mid-application.
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

        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.Embedding is { } embedding)
                    entry.Item.SetEmbedding(embedding.ToArray());

                _storage.AddOrUpdate(entry.Key, entry.Item, (_, _) => entry.Item);
            }

            LogBatchUpserted(entries.Count);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogException(ex, "UpsertBatchAsync");
            throw;
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Stored memory item with key: {Key}")]
    private partial void LogStoredItem(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Retrieved memory item with key: {Key}")]
    private partial void LogRetrievedItem(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Memory item not found with key: {Key}")]
    private partial void LogItemNotFound(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Cannot update non-existent memory item with key: {Key}")]
    private partial void LogCannotUpdateNonExistent(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Updated memory item with key: {Key}")]
    private partial void LogUpdatedItem(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Deleted memory item with key: {Key}, Success: {Success}")]
    private partial void LogDeletedItem(string key, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Found {Count} memory items for empty query")]
    private partial void LogFoundItemsEmpty(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Found {Count} memory items matching query: {Query}")]
    private partial void LogFoundItems(int count, string query);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Cleared {Count} memory items")]
    private partial void LogClearedItems(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Counted {Count} memory items")]
    private partial void LogCountedItems(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Listed {Count} memory keys (skip: {Skip}, take: {Take})")]
    private partial void LogListedKeys(int count, int skip, int take);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Vector search found {Count} items above min score {MinScore} (topK={TopK})")]
    private partial void LogVectorSearchResults(int count, float minScore, int topK);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Batch-upserted {Count} memory items")]
    private partial void LogBatchUpserted(int count);
}
