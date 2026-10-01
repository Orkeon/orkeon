using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using StackExchange.Redis;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Memory.Base;
using Orkeon.Infrastructure.Resilience;
using System.Text.Json;
using Orkeon.Domain.Constants.Serialization;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Simplified Redis memory provider focused on data access only.
/// Business logic (embedding generation, similarity search) moved to domain services.
/// </summary>
/// <remarks>
/// <para>
/// Ready on construction: the connection described by <see cref="RedisMemoryOptions"/>
/// (<c>Orkeon:Redis</c>) is opened on the first call, once, whatever the number of concurrent
/// callers (GAP-08). A failed connection is not cached — the next call tries again.
/// </para>
/// <para>
/// <see cref="IMemoryProvider"/> is re-listed on purpose (same pattern as
/// <c>SqliteMemoryProvider</c>/<c>LanceDbMemoryProvider</c>, R10.1): without
/// re-implementation, calls made through the interface would resolve
/// <c>SearchSimilarAsync</c> to the default interface method (empty results) instead of
/// the client-side cosine search defined here (interface mapping is otherwise frozen at
/// <see cref="MemoryProviderBase"/>).
/// </para>
/// </remarks>
public partial class RedisMemoryProvider : MemoryProviderBase, IMemoryProvider, IDisposable
{
    private static readonly JsonSerializerOptions s_camelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    private readonly string _connectionString;
    private readonly string _keyPrefix;
    private readonly IAsyncPolicy _redisPolicy;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private ConnectionMultiplexer? _redis;
    private IDatabase? _database;
    private volatile bool _disposed;

    /// <inheritdoc />
    public override string Name => "redis";

    /// <summary>Initializes a new instance of <see cref="RedisMemoryProvider"/>.</summary>
    /// <param name="options">Connection options, bound from <c>Orkeon:Redis</c>.</param>
    /// <param name="logger">Optional logger.</param>
    public RedisMemoryProvider(IOptions<RedisMemoryOptions> options, ILogger<RedisMemoryProvider>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ConnectionString, nameof(options));
        _connectionString = value.ConnectionString;
        _keyPrefix = value.KeyPrefix ?? string.Empty;
        _redisPolicy = ResiliencePolicies.GetRedisRetryPolicy(Logger);
    }

    /// <summary>
    /// Returns the database, opening the connection on the first call. Thread-safe: concurrent
    /// first callers share one <see cref="ConnectionMultiplexer"/>. A failure propagates (a
    /// <see cref="RedisConnectionException"/> when the server is unreachable) and is not cached.
    /// </summary>
    private async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var database = Volatile.Read(ref _database);
        if (database is not null)
            return database;

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_database is not null)
                return _database;

            var redis = await ConnectionMultiplexer.ConnectAsync(_connectionString).ConfigureAwait(false);
            _redis = redis;
            database = redis.GetDatabase();
            Volatile.Write(ref _database, database);

            // Once per provider lifetime: the cost of formatting the endpoints is irrelevant.
            var endpoints = string.Join(", ", redis.GetEndPoints().Select(endpoint => endpoint.ToString()));
            LogConnected(endpoints);
            return database;
        }
        catch (Exception ex) when (ex is not ObjectDisposedException and not OperationCanceledException)
        {
            LogConnectFailed(ex);
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>The first server of the open connection, for key scans.</summary>
    private IServer FirstServer() => _redis!.GetServer(_redis.GetEndPoints().First());

    /// <summary>
    /// Stores a memory item in Redis.
    /// Pure data storage - no business logic.
    /// </summary>
    public override async Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);
        var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var redisKey = GetRedisKey(key);
                var serializedItem = SerializeMemoryItem(item);
                await database.StringSetAsync(redisKey, serializedItem).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            LogStoredItem(key);
        }
        catch (Exception ex)
        {
            LogException(ex, "StoreAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a memory item from Redis.
    /// Pure data retrieval - no business logic.
    /// </summary>
    public override async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var redisKey = GetRedisKey(key);
                var value = await database.StringGetAsync(redisKey).ConfigureAwait(false);

                if (!value.HasValue)
                    return (MemoryItem?)null;

                return DeserializeMemoryItem(value!);
            }, cancellationToken).ConfigureAwait(false);

            if (result != null)
                LogRetrievedItem(key);
            else
                LogItemNotFound(key);

            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Updates a memory item in Redis.
    /// Pure data update - no business logic.
    /// </summary>
    public override async Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);
        var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var updated = await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var redisKey = GetRedisKey(key);

                // Check if key exists first
                var exists = await database.KeyExistsAsync(redisKey).ConfigureAwait(false);
                if (!exists)
                    return false;

                var serializedItem = SerializeMemoryItem(item);
                await database.StringSetAsync(redisKey, serializedItem).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);

            if (updated)
                LogUpdatedItem(key);
            else
                LogCannotUpdateNonExistent(key);

            return updated;
        }
        catch (Exception ex)
        {
            LogException(ex, "UpdateAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Deletes a memory item from Redis.
    /// Pure data deletion - no business logic.
    /// </summary>
    public override async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var deleted = await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var redisKey = GetRedisKey(key);
                return await database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            LogDeletedItem(key, deleted);
            return deleted;
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteAsync", key);
            throw;
        }
    }

    /// <summary>
    /// Searches for memories by scanning all keys.
    /// Pure data query - search logic implemented in domain services.
    /// </summary>
    public override async Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<MemoryItem>();

        var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var results = await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var items = new List<MemoryItem>();
                var pattern = $"{_keyPrefix}*";
                var server = FirstServer();

                // Scan for matching keys - this is basic pattern matching
                // Complex similarity search would be handled by domain services
                await foreach (var key in server.KeysAsync(pattern: pattern).ConfigureAwait(false))
                {
                    if (items.Count >= limit)
                        break;

                    var value = await database.StringGetAsync(key).ConfigureAwait(false);
                    if (value.HasValue)
                    {
                        var item = DeserializeMemoryItem(value!);
                        if (item != null && item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            items.Add(item);
                        }
                    }
                }

                return items;
            }, cancellationToken).ConfigureAwait(false);

            LogFoundItems(results.Count, query);
            return results;
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchAsync");
            throw;
        }
    }

    /// <summary>
    /// Clears all memories with the configured prefix.
    /// Pure data operation - no business logic.
    /// </summary>
    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var count = await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var pattern = $"{_keyPrefix}*";
                var server = FirstServer();
                var keys = new List<StackExchange.Redis.RedisKey>();
                await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct).ConfigureAwait(false))
                {
                    keys.Add(key);
                }

                if (keys.Count > 0)
                {
                    await database.KeyDeleteAsync(keys.ToArray()).ConfigureAwait(false);
                }

                return keys.Count;
            }, cancellationToken).ConfigureAwait(false);

            LogClearedItems(count);
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
    public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var count = await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var pattern = $"{_keyPrefix}*";
                var server = FirstServer();
                var total = 0;

                await foreach (var key in server.KeysAsync(pattern: pattern).ConfigureAwait(false))
                {
                    total++;
                }

                return total;
            }, cancellationToken).ConfigureAwait(false);

            LogCountedItems(count);
            return count;
        }
        catch (Exception ex)
        {
            LogException(ex, "CountAsync");
            throw;
        }
    }

    /// <summary>
    /// Searches for memory items similar to the provided query embedding vector.
    /// Redis is used here as a plain JSON key/value store (no RediSearch module
    /// dependency), so items are streamed out of the keyspace with the same full-scan
    /// strategy as <see cref="SearchAsync"/> and scored client-side with
    /// <see cref="VectorMath.CosineSimilarity(ReadOnlySpan{float}, ReadOnlySpan{float})"/>.
    /// Overrides the abstract <see cref="MemoryProviderBase.SearchSimilarAsync"/> so calls
    /// made through <see cref="IMemoryProvider"/> dispatch here instead of the interface's
    /// empty default body.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return.</param>
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
        return SearchSimilarCoreAsync();

        async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarCoreAsync()
        {
            var database = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var results = await _redisPolicy.ExecuteAsync(
                    ct => CollectScoredItemsAsync(database, queryEmbedding, topK, minScore, filter),
                    cancellationToken).ConfigureAwait(false);

                LogVectorSearchResults(results.Count, minScore, topK);
                return results;
            }
            catch (Exception ex)
            {
                LogException(ex, "SearchSimilarAsync");
                throw;
            }
        }
    }

    private async Task<IReadOnlyList<ScoredMemoryItem>> CollectScoredItemsAsync(
        IDatabase database,
        float[] queryEmbedding,
        int topK,
        float minScore,
        Dictionary<string, object>? filter)
    {
        var scored = new List<ScoredMemoryItem>();
        var pattern = $"{_keyPrefix}*";
        var server = FirstServer();

        await foreach (var key in server.KeysAsync(pattern: pattern).ConfigureAwait(false))
        {
            var value = await database.StringGetAsync(key).ConfigureAwait(false);
            if (!value.HasValue)
                continue;

            var candidate = ScoreCandidate(value!, queryEmbedding, minScore, filter);
            if (candidate is not null)
                scored.Add(candidate);
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
    }

    private ScoredMemoryItem? ScoreCandidate(
        string json,
        float[] queryEmbedding,
        float minScore,
        Dictionary<string, object>? filter)
    {
        var item = DeserializeMemoryItem(json);
        if (item?.Embedding == null || item.Embedding.Count != queryEmbedding.Length)
            return null;

        if (filter != null && !MatchesMetadataFilter(item, filter))
            return null;

        var score = VectorMath.CosineSimilarity(queryEmbedding, item.Embedding.ToArray());
        return score >= minScore ? new ScoredMemoryItem(item, score) : null;
    }

    /// <summary>
    /// Applies the metadata filter to a candidate item, mirroring the semantics of the
    /// SQLite provider: <c>source</c> equality, <c>tag</c>/<c>tags</c> membership, any other
    /// key matched against the item's custom properties.
    /// </summary>
    private static bool MatchesMetadataFilter(MemoryItem item, Dictionary<string, object> filter)
    {
        foreach (var (key, value) in filter)
        {
            var filterValue = value?.ToString();

#pragma warning disable CA1308 // lowercase is the required wire/storage form, not a comparison normalization
            switch (key.ToLowerInvariant())
#pragma warning restore CA1308
            {
                case "source":
                    if (!string.Equals(item.Source, filterValue, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;

                case "tag" or "tags":
                    if (filterValue == null || !item.Tags.Contains(filterValue, StringComparer.OrdinalIgnoreCase))
                        return false;
                    break;

                default:
                    var props = item.Metadata.CustomProperties;
                    if (props == null ||
                        !props.TryGetValue(key, out var propValue) ||
                        !string.Equals(propValue, filterValue, StringComparison.OrdinalIgnoreCase))
                        return false;
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Lists memory keys with pagination.
    /// Pure data query - no business logic.
    /// </summary>
    public override async Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var keys = await _redisPolicy.ExecuteAsync(async (ct) =>
            {
                var result = new List<string>();
                var pattern = $"{_keyPrefix}*";
                var server = FirstServer();
                var current = 0;

                await foreach (var key in server.KeysAsync(pattern: pattern).ConfigureAwait(false))
                {
                    if (current < skip)
                    {
                        current++;
                        continue;
                    }

                    if (result.Count >= take)
                        break;

                    // Remove prefix to get the original key
                    var originalKey = key.ToString().Substring(_keyPrefix.Length);
                    result.Add(originalKey);
                    current++;
                }

                return result;
            }, cancellationToken).ConfigureAwait(false);

            LogListedKeys(keys.Count, skip, take);
            return keys;
        }
        catch (Exception ex)
        {
            LogException(ex, "ListKeysAsync");
            throw;
        }
    }

    /// <summary>
    /// Creates Redis key with prefix.
    /// Pure utility method.
    /// </summary>
    private string GetRedisKey(string key)
    {
        return $"{_keyPrefix}{key}";
    }

    /// <summary>
    /// Serializes memory item to JSON.
    /// Pure data transformation.
    /// </summary>
    private static string SerializeMemoryItem(MemoryItem item)
    {
        return JsonSerializer.Serialize(item, s_camelCaseOptions);
    }

    /// <summary>
    /// Deserializes memory item from JSON.
    /// Pure data transformation.
    /// </summary>
    private MemoryItem? DeserializeMemoryItem(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<MemoryItem>(json, s_camelCaseOptions);
        }
        catch (JsonException ex)
        {
            LogDeserializeFailed(ex, json);
            return null;
        }
    }

    /// <summary>
    /// Disposes Redis resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed and unmanaged resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _redis?.Dispose();
            _connectLock.Dispose();
        }
    }

    // The endpoints only — the connection string may carry a password.
    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Redis memory provider connected to {Endpoints}")]
    private partial void LogConnected(string endpoints);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Redis memory provider failed to connect (Orkeon:Redis:ConnectionString)")]
    private partial void LogConnectFailed(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Stored memory item with key: {Key}")]
    private partial void LogStoredItem(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Retrieved memory item with key: {Key}")]
    private partial void LogRetrievedItem(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Memory item not found with key: {Key}")]
    private partial void LogItemNotFound(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Updated memory item with key: {Key}")]
    private partial void LogUpdatedItem(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Cannot update non-existent memory item with key: {Key}")]
    private partial void LogCannotUpdateNonExistent(string key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Deleted memory item with key: {Key}, Success: {Success}")]
    private partial void LogDeletedItem(string key, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Found {Count} memory items matching query: {Query}")]
    private partial void LogFoundItems(int count, string query);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Cleared {Count} memory items")]
    private partial void LogClearedItems(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Counted {Count} memory items")]
    private partial void LogCountedItems(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Listed {Count} memory keys (skip: {Skip}, take: {Take})")]
    private partial void LogListedKeys(int count, int skip, int take);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Failed to deserialize memory item from JSON: {Json}")]
    private partial void LogDeserializeFailed(Exception ex, string json);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Vector search found {Count} items above min score {MinScore} (topK={TopK})")]
    private partial void LogVectorSearchResults(int count, float minScore, int topK);
}
