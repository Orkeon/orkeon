using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.Base;
using Orkeon.Domain.Constants.Serialization;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.ChromaDb;

/// <summary>
/// Memory provider backed by ChromaDB vector database.
/// Communicates with ChromaDB via its REST API v2
/// (<c>/api/v2/tenants/{tenant}/databases/{database}/collections/...</c>),
/// the only API surface exposed by ChromaDB ≥ 0.6.x / 1.x — the legacy
/// <c>/api/v1</c> routes were removed server-side (HTTP 410).
/// Tenant and database default to <c>default_tenant</c>/<c>default_database</c>
/// and are configurable via <see cref="ChromaDbOptions"/>.
/// </summary>
/// <remarks>
/// <see cref="IMemoryProvider"/> is re-listed on purpose (same pattern as
/// <c>SqliteMemoryProvider</c>/<c>LanceDbMemoryProvider</c>, R10.1): without
/// re-implementation, calls made through the interface would resolve
/// <c>SearchSimilarAsync</c> to the default interface method (empty results) instead of
/// the server-side vector query defined here (interface mapping is otherwise frozen at
/// <see cref="MemoryProviderBase"/>).
/// </remarks>
public partial class ChromaDbMemoryProvider : MemoryProviderBase, IMemoryProvider, IDisposable
{
    private const string ApiV2Root = "/api/v2";

    /// <summary>Liveness probe route of the ChromaDB v2 API.</summary>
    private const string HeartbeatRoute = $"{ApiV2Root}/heartbeat";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    private readonly HttpClient _httpClient;
    private readonly ChromaDbOptions _options;
    private readonly string _collectionsRoute;
    private string? _collectionId;

    /// <inheritdoc />
    public override string Name => "ChromaDB";

    /// <summary>Initializes a new instance of <see cref="ChromaDbMemoryProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client used for ChromaDB API calls.</param>
    /// <param name="options">ChromaDB configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public ChromaDbMemoryProvider(
        HttpClient httpClient,
        IOptions<ChromaDbOptions> options,
        ILogger<ChromaDbMemoryProvider>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.ToString().TrimEnd('/'));
        _collectionsRoute =
            $"{ApiV2Root}/tenants/{Uri.EscapeDataString(_options.Tenant)}" +
            $"/databases/{Uri.EscapeDataString(_options.Database)}/collections";
    }

    /// <inheritdoc />
    public override async Task InitializeAsync(MemoryProviderConfig config, CancellationToken cancellationToken = default)
    {
        await base.InitializeAsync(config, cancellationToken).ConfigureAwait(false);
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new
            {
                ids = new[] { key },
                documents = new[] { item.Content },
                metadatas = new[] { BuildMetadata(item) },
                embeddings = item.Embedding != null ? new[] { item.Embedding } : null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{_collectionId}/add",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // If the item already exists, try update instead
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (errorContent.Contains("already exist", StringComparison.OrdinalIgnoreCase))
                {
                    await UpdateCoreAsync(key, item, cancellationToken).ConfigureAwait(false);
                    return;
                }

                response.EnsureSuccessStatusCode();
            }

            LogStoredMemoryItemWithKey(key);
        }
        catch (Exception ex)
        {
            LogException(ex, "StoreAsync", key);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new { ids = new[] { key } };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{_collectionId}/get",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ChromaGetResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Ids == null || result.Ids.Count == 0)
            {
                LogMemoryItemNotFoundWith(key);
                return null;
            }

            var item = BuildMemoryItem(result, 0);
            LogRetrievedMemoryItemWithKey(key);
            return item;
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAsync", key);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Check if key exists first
            var existing = await GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                LogCannotUpdateNonExistentMemory(key);
                return false;
            }

            await UpdateCoreAsync(key, item, cancellationToken).ConfigureAwait(false);
            LogUpdatedMemoryItemWithKey(key);
            return true;
        }
        catch (Exception ex)
        {
            LogException(ex, "UpdateAsync", key);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new { ids = new[] { key } };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{_collectionId}/delete",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            var success = response.IsSuccessStatusCode;
            LogDeletedMemoryItemWithKey(key, success);
            return success;
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteAsync", key);
            throw;
        }
    }

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
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new
            {
                query_texts = new[] { query },
                n_results = limit > 0 ? limit : _options.DefaultTopK
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{_collectionId}/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ChromaQueryResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Ids == null || result.Ids.Count == 0 || result.Ids[0].Count == 0)
            {
                LogNoResultsFoundForQuery(query);
                return Array.Empty<MemoryItem>();
            }

            var items = new List<MemoryItem>();
            for (var i = 0; i < result.Ids[0].Count; i++)
            {
                var item = BuildMemoryItemFromQuery(result, 0, i);
                if (item != null)
                    items.Add(item);
            }

            LogFoundMemoryItemsMatchingQuery(items.Count, query);
            return items;
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchAsync");
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Delete the collection and recreate it
            if (_collectionId != null)
            {
                _ = await _httpClient.DeleteAsync(
                    new Uri($"{_collectionsRoute}/{Uri.EscapeDataString(_options.CollectionName)}", UriKind.Relative),
                    cancellationToken).ConfigureAwait(false);

                // Ignore errors on delete (collection may not exist)
                _collectionId = null;
            }

            await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
            LogClearedAllMemoryItemsFrom(_options.CollectionName);
        }
        catch (Exception ex)
        {
            LogException(ex, "ClearAsync");
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await _httpClient.GetAsync(
                new Uri($"{_collectionsRoute}/{_collectionId}/count", UriKind.Relative),
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var count = await response.Content.ReadFromJsonAsync<int>(s_jsonOptions, cancellationToken).ConfigureAwait(false);
            LogCountedMemoryItemsInChromadb(count);
            return count;
        }
        catch (Exception ex)
        {
            LogException(ex, "CountAsync");
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new
            {
                limit = skip + take,
                offset = 0
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{_collectionId}/get",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ChromaGetResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            var keys = result?.Ids?
                .Skip(skip)
                .Take(take)
                .ToList() ?? [];

            LogListedMemoryKeysFromChromadb(keys.Count, skip, take);
            return keys;
        }
        catch (Exception ex)
        {
            LogException(ex, "ListKeysAsync");
            throw;
        }
    }

    /// <summary>
    /// Searches for memory items similar to the provided query embedding vector using
    /// ChromaDB's server-side vector query (<c>POST .../query</c> with
    /// <c>query_embeddings</c>). The returned distances are converted to similarity
    /// scores as <c>1 - distance</c> (cosine convention, same as the LanceDB provider).
    /// Overrides the abstract <see cref="MemoryProviderBase.SearchSimilarAsync"/> so calls
    /// made through <see cref="IMemoryProvider"/> dispatch here instead of the interface's
    /// empty default body.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results (provider default when non-positive).</param>
    /// <param name="minScore">Minimum similarity score threshold.</param>
    /// <param name="filter">Optional metadata filter, mapped to a ChromaDB <c>where</c> clause (<c>$eq</c> per key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching items scored by similarity, best first.</returns>
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
        await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var effectiveTopK = topK > 0 ? topK : _options.DefaultTopK;

            var payload = new
            {
                query_embeddings = new[] { queryEmbedding },
                n_results = effectiveTopK,
                where = BuildWhereClause(filter),
                include = s_similarityQueryIncludes
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{_collectionId}/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ChromaQueryResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Ids == null || result.Ids.Count == 0 || result.Ids[0].Count == 0)
            {
                LogVectorSearchResults(0, minScore, effectiveTopK);
                return Array.Empty<ScoredMemoryItem>();
            }

            var scored = new List<ScoredMemoryItem>();
            for (var i = 0; i < result.Ids[0].Count; i++)
            {
                var item = BuildMemoryItemFromQuery(result, 0, i);
                if (item == null)
                    continue;

                var score = GetSimilarityScore(result, 0, i);
                if (score >= minScore)
                    scored.Add(new ScoredMemoryItem(item, score));
            }

            var results = scored
                .OrderByDescending(s => s.Score)
                .Take(effectiveTopK)
                .ToList();

            LogVectorSearchResults(results.Count, minScore, effectiveTopK);
            return results;
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchSimilarAsync");
            throw;
        }
    }

    /// <summary>Fields requested from ChromaDB for similarity queries.</summary>
    private static readonly string[] s_similarityQueryIncludes = ["documents", "metadatas", "distances"];

    /// <summary>
    /// Converts a ChromaDB distance into a similarity score (<c>1 - distance</c>,
    /// cosine convention). Missing distances yield a score of 0.
    /// </summary>
    private static float GetSimilarityScore(ChromaQueryResponse response, int queryIndex, int resultIndex)
    {
        if (response.Distances == null ||
            queryIndex >= response.Distances.Count ||
            resultIndex >= response.Distances[queryIndex].Count)
            return 0f;

        return 1f - response.Distances[queryIndex][resultIndex];
    }

    /// <summary>
    /// Maps the provider-agnostic metadata filter to a ChromaDB <c>where</c> clause
    /// (one <c>$eq</c> condition per key), or <see langword="null"/> when no filter is set.
    /// </summary>
    private static Dictionary<string, object>? BuildWhereClause(Dictionary<string, object>? filter)
    {
        if (filter == null || filter.Count == 0)
#pragma warning disable S1168 // null omits the `where` key from the payload; {} would send an empty clause
            return null;
#pragma warning restore S1168

        var where = new Dictionary<string, object>(filter.Count);
        foreach (var (key, value) in filter)
        {
            where[key] = new Dictionary<string, object> { ["$eq"] = value };
        }

        return where;
    }

    /// <summary>
    /// Probes the ChromaDB server liveness endpoint (<c>GET /api/v2/heartbeat</c>).
    /// Returns <see langword="true"/> when the server answers with a success status code,
    /// <see langword="false"/> when it answers with an error or is unreachable.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the probe.</param>
    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(HeartbeatRoute, UriKind.Relative), cancellationToken).ConfigureAwait(false);
            var healthy = response.IsSuccessStatusCode;
            LogChromadbHeartbeatResult(healthy);
            return healthy;
        }
        catch (HttpRequestException ex)
        {
            LogException(ex, "HeartbeatAsync");
            return false;
        }
    }

    /// <summary>
    /// Ensures the ChromaDB collection exists, creating it if necessary.
    /// </summary>
    private async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken)
    {
        if (_collectionId != null)
            return;

        var payload = new
        {
            name = _options.CollectionName,
            get_or_create = true
        };

        var response = await _httpClient.PostAsJsonAsync(
            _collectionsRoute,
            payload,
            s_jsonOptions,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChromaCollectionResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);
        _collectionId = result?.Id ?? throw new InvalidOperationException("Failed to create or get ChromaDB collection.");

        LogEnsuredChromadbCollectionExistsId(_options.CollectionName, _collectionId);
    }

    /// <summary>
    /// Sends an update request for the given key and item.
    /// </summary>
    private async Task UpdateCoreAsync(string key, MemoryItem item, CancellationToken cancellationToken)
    {
        var payload = new
        {
            ids = new[] { key },
            documents = new[] { item.Content },
            metadatas = new[] { BuildMetadata(item) },
            embeddings = item.Embedding != null ? new[] { item.Embedding } : null
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_collectionsRoute}/{_collectionId}/update",
            payload,
            s_jsonOptions,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Safely converts a metadata value to float, handling JsonElement types.
    /// </summary>
    private static float ConvertToFloat(object? value, float defaultValue = 0.5f)
    {
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => jsonElement.GetSingle(),
                System.Text.Json.JsonValueKind.String when float.TryParse(jsonElement.GetString(), out var f) => f,
                _ => defaultValue
            };
        }
        try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return defaultValue; }
    }

    /// <summary>
    /// Safely converts a metadata value to string, handling JsonElement types.
    /// </summary>
    private static string? ConvertToString(object? value)
    {
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? jsonElement.GetString()
                : jsonElement.GetRawText();
        }
        return value?.ToString();
    }

    /// <summary>
    /// Builds metadata dictionary from a MemoryItem.
    /// </summary>
    private static Dictionary<string, object> BuildMetadata(MemoryItem item)
    {
        var metadata = new Dictionary<string, object>
        {
            ["importance"] = item.Importance,
            ["source"] = item.Source,
            ["timestamp"] = item.Timestamp.ToString("O")
        };

        if (item.Tags.Count > 0)
            metadata["tags"] = string.Join(",", item.Tags);

        return metadata;
    }

    /// <summary>
    /// Builds a MemoryItem from a ChromaDB get response.
    /// </summary>
    private static MemoryItem? BuildMemoryItem(ChromaGetResponse response, int index)
    {
        if (response.Documents == null || index >= response.Documents.Count)
            return null;

        var content = response.Documents[index];
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var importance = MemoryDefaults.DefaultImportance;
        string? source = null;

        if (response.Metadatas != null && index < response.Metadatas.Count)
        {
            var meta = response.Metadatas[index];
            if (meta != null)
            {
                if (meta.TryGetValue("importance", out var imp))
                    importance = ConvertToFloat(imp);
                if (meta.TryGetValue("source", out var src))
                    source = ConvertToString(src);
            }
        }

        float[]? embedding = null;
        if (response.Embeddings != null && index < response.Embeddings.Count)
            embedding = response.Embeddings[index];

        return MemoryItem.Create(content, embedding, importance, source);
    }

    /// <summary>
    /// Builds a MemoryItem from a ChromaDB query response.
    /// </summary>
    private static MemoryItem? BuildMemoryItemFromQuery(ChromaQueryResponse response, int queryIndex, int resultIndex)
    {
        if (response.Documents == null ||
            queryIndex >= response.Documents.Count ||
            resultIndex >= response.Documents[queryIndex].Count)
            return null;

        var content = response.Documents[queryIndex][resultIndex];
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var importance = MemoryDefaults.DefaultImportance;
        string? source = null;

        if (response.Metadatas != null &&
            queryIndex < response.Metadatas.Count &&
            resultIndex < response.Metadatas[queryIndex].Count)
        {
            var meta = response.Metadatas[queryIndex][resultIndex];
            if (meta != null)
            {
                if (meta.TryGetValue("importance", out var imp))
                    importance = ConvertToFloat(imp);
                if (meta.TryGetValue("source", out var src))
                    source = ConvertToString(src);
            }
        }

        return MemoryItem.Create(content, importance: importance, source: source);
    }

    // --- Internal DTO classes for ChromaDB API responses ---

    internal sealed class ChromaCollectionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    internal sealed class ChromaGetResponse
    {
        [JsonPropertyName("ids")]
        public List<string>? Ids { get; set; }

        [JsonPropertyName("documents")]
        public List<string>? Documents { get; set; }

        [JsonPropertyName("metadatas")]
        public List<Dictionary<string, object>?>? Metadatas { get; set; }

        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }

    internal sealed class ChromaQueryResponse
    {
        [JsonPropertyName("ids")]
        public List<List<string>>? Ids { get; set; }

        [JsonPropertyName("documents")]
        public List<List<string>>? Documents { get; set; }

        [JsonPropertyName("metadatas")]
        public List<List<Dictionary<string, object>?>>? Metadatas { get; set; }

        [JsonPropertyName("distances")]
        public List<List<float>>? Distances { get; set; }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Stored memory item with key: {Key} in ChromaDB")]
    private partial void LogStoredMemoryItemWithKey(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Memory item not found with key: {Key} in ChromaDB")]
    private partial void LogMemoryItemNotFoundWith(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Retrieved memory item with key: {Key} from ChromaDB")]
    private partial void LogRetrievedMemoryItemWithKey(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Cannot update non-existent memory item with key: {Key} in ChromaDB")]
    private partial void LogCannotUpdateNonExistentMemory(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Updated memory item with key: {Key} in ChromaDB")]
    private partial void LogUpdatedMemoryItemWithKey(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Deleted memory item with key: {Key} from ChromaDB, Success: {Success}")]
    private partial void LogDeletedMemoryItemWithKey(object key, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "No results found for query: {Query} in ChromaDB")]
    private partial void LogNoResultsFoundForQuery(object query);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Found {Count} memory items matching query: {Query} in ChromaDB")]
    private partial void LogFoundMemoryItemsMatchingQuery(int count, object query);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Cleared all memory items from ChromaDB collection: {Collection}")]
    private partial void LogClearedAllMemoryItemsFrom(object collection);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Counted {Count} memory items in ChromaDB")]
    private partial void LogCountedMemoryItemsInChromadb(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Listed {Count} memory keys from ChromaDB (skip: {Skip}, take: {Take})")]
    private partial void LogListedMemoryKeysFromChromadb(int count, int skip, int take);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Ensured ChromaDB collection exists: {Collection} (ID: {CollectionId})")]
    private partial void LogEnsuredChromadbCollectionExistsId(object collection, object collectionId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "ChromaDB heartbeat probe returned healthy: {Healthy}")]
    private partial void LogChromadbHeartbeatResult(bool healthy);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Vector search found {Count} items above min score {MinScore} (topK={TopK})")]
    private partial void LogVectorSearchResults(int count, float minScore, int topK);

    /// <summary>Releases the HTTP client whose ownership was transferred to this provider.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources held by the provider.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient.Dispose();
        }
    }
}
