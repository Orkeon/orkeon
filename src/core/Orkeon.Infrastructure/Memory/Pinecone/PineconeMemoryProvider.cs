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

namespace Orkeon.Infrastructure.Memory.Pinecone;

/// <summary>
/// Memory provider backed by Pinecone vector database.
/// Communicates with Pinecone via its REST API.
/// </summary>
/// <remarks>
/// <see cref="IMemoryProvider"/> is re-listed on purpose (same pattern as
/// <c>SqliteMemoryProvider</c>/<c>LanceDbMemoryProvider</c>, R10.1): without
/// re-implementation, calls made through the interface would resolve
/// <c>SearchSimilarAsync</c> to the default interface method (empty results) instead of
/// the server-side vector query defined here (interface mapping is otherwise frozen at
/// <see cref="MemoryProviderBase"/>).
/// </remarks>
public partial class PineconeMemoryProvider : MemoryProviderBase, IMemoryProvider, IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    private readonly HttpClient _httpClient;
    private readonly PineconeOptions _options;

    /// <inheritdoc />
    public override string Name => "Pinecone";

    /// <summary>Initializes a new instance of <see cref="PineconeMemoryProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client used for Pinecone API calls.</param>
    /// <param name="options">Pinecone configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public PineconeMemoryProvider(
        HttpClient httpClient,
        IOptions<PineconeOptions> options,
        ILogger<PineconeMemoryProvider>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        ConfigureHttpClient();
    }

    /// <inheritdoc />
    public override async Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateMemoryItem(item);

        try
        {
            var vector = new PineconeVector
            {
                Id = key,
                Values = item.Embedding?.ToArray() ?? [],
                Metadata = BuildMetadata(item)
            };

            var payload = new
            {
                vectors = new[] { vector },
                @namespace = _options.Namespace
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/vectors/upsert",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
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

        try
        {
            var response = await _httpClient.GetAsync(
                new Uri($"/vectors/fetch?ids={Uri.EscapeDataString(key)}&namespace={Uri.EscapeDataString(_options.Namespace)}", UriKind.Relative),
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PineconeFetchResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Vectors == null || !result.Vectors.TryGetValue(key, out var vector))
            {
                LogMemoryItemNotFoundWith(key);
                return null;
            }

            var item = BuildMemoryItem(vector);
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

        try
        {
            // Check if key exists first
            var existing = await GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                LogCannotUpdateNonExistentMemory(key);
                return false;
            }

            // Pinecone upsert acts as update
            await StoreAsync(key, item, cancellationToken).ConfigureAwait(false);
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

        try
        {
            var payload = new
            {
                ids = new[] { key },
                @namespace = _options.Namespace
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/vectors/delete",
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
        try
        {
            // Pinecone requires a vector for querying. For text-based search,
            // the caller should provide embeddings via the item. Here we search
            // by metadata filter as a fallback when no embedding is available.
            var payload = new
            {
                // Use a zero vector as placeholder; real usage should provide embeddings
                vector = Array.Empty<float>(),
                topK = limit,
                includeMetadata = true,
                includeValues = true,
                @namespace = _options.Namespace,
                filter = new Dictionary<string, object>
                {
                    ["content"] = new Dictionary<string, object> { ["$eq"] = query }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PineconeQueryResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Matches == null || result.Matches.Count == 0)
            {
                LogNoResultsFoundForQuery();
                return Array.Empty<MemoryItem>();
            }

            var items = new List<MemoryItem>();
            foreach (var match in result.Matches)
            {
                var item = BuildMemoryItem(match);
                if (item != null)
                    items.Add(item);
            }

            LogFoundMemoryItemsInPinecone(items.Count);
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
            var payload = new
            {
                deleteAll = true,
                @namespace = _options.Namespace
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/vectors/delete",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            LogClearedAllMemoryItemsFrom(_options.Namespace);
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
        try
        {
            var response = await _httpClient.GetAsync(
                new Uri("/describe_index_stats", UriKind.Relative),
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PineconeIndexStatsResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Namespaces != null &&
                result.Namespaces.TryGetValue(_options.Namespace, out var nsStats))
            {
                var count = nsStats.VectorCount;
                LogCountedMemoryItemsInPinecone(count);
                return count;
            }

            LogNamespaceNotFoundInPinecone(_options.Namespace);
            return 0;
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
        try
        {
            // Pinecone does not natively support listing vector IDs with pagination.
            // We use the list endpoint if available (Pinecone serverless), or return empty.
            var response = await _httpClient.GetAsync(
                new Uri($"/vectors/list?namespace={Uri.EscapeDataString(_options.Namespace)}&limit={skip + take}", UriKind.Relative),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogPineconeListVectorsEndpointNot();
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<PineconeListResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            var keys = result?.Vectors?
                .Select(v => v.Id)
                .Where(id => id != null)
                .Cast<string>()
                .Skip(skip)
                .Take(take)
                .ToList() ?? [];

            LogListedMemoryKeysFromPinecone(keys.Count, skip, take);
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
    /// Pinecone's server-side vector query (<c>POST /query</c>). Match scores are
    /// returned by Pinecone in the similarity space of the index metric and used as-is.
    /// Overrides the abstract <see cref="MemoryProviderBase.SearchSimilarAsync"/> so calls
    /// made through <see cref="IMemoryProvider"/> dispatch here instead of the interface's
    /// empty default body.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results (defaults to <see cref="MemoryDefaults.DefaultSearchLimit"/> when non-positive).</param>
    /// <param name="minScore">Minimum similarity score threshold.</param>
    /// <param name="filter">Optional metadata filter, mapped to a Pinecone filter (<c>$eq</c> per key).</param>
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
        try
        {
            var effectiveTopK = topK > 0 ? topK : MemoryDefaults.DefaultSearchLimit;

            var payload = new
            {
                vector = queryEmbedding,
                topK = effectiveTopK,
                includeMetadata = true,
                includeValues = true,
                @namespace = _options.Namespace,
                filter = BuildEqualityFilter(filter)
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PineconeQueryResponse>(s_jsonOptions, cancellationToken).ConfigureAwait(false);

            if (result?.Matches == null || result.Matches.Count == 0)
            {
                LogVectorSearchResults(0, minScore, effectiveTopK);
                return Array.Empty<ScoredMemoryItem>();
            }

            var scored = new List<ScoredMemoryItem>();
            foreach (var match in result.Matches)
            {
                if (match.Score < minScore)
                    continue;

                var item = BuildMemoryItem(match);
                if (item != null)
                    scored.Add(new ScoredMemoryItem(item, match.Score));
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

    /// <summary>
    /// Maps the provider-agnostic metadata filter to a Pinecone metadata filter
    /// (one <c>$eq</c> condition per key), or <see langword="null"/> when no filter is set.
    /// </summary>
    private static Dictionary<string, object>? BuildEqualityFilter(Dictionary<string, object>? filter)
    {
        if (filter == null || filter.Count == 0)
#pragma warning disable S1168 // null omits the filter from the payload; {} would send an empty filter
            return null;
#pragma warning restore S1168

        var pineconeFilter = new Dictionary<string, object>(filter.Count);
        foreach (var (key, value) in filter)
        {
            pineconeFilter[key] = new Dictionary<string, object> { ["$eq"] = value };
        }

        return pineconeFilter;
    }

    /// <summary>
    /// Configures the HTTP client with Pinecone-specific headers and base address.
    /// </summary>
    private void ConfigureHttpClient()
    {
        if (_httpClient.BaseAddress == null && !string.IsNullOrEmpty(_options.Environment) && !string.IsNullOrEmpty(_options.IndexName))
        {
            _httpClient.BaseAddress = new Uri(
                $"https://{_options.IndexName}-{_options.Environment}.svc.{_options.Environment}.pinecone.io");
        }

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("Api-Key");
            _httpClient.DefaultRequestHeaders.Add("Api-Key", _options.ApiKey);
        }
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
            ["content"] = item.Content,
            ["importance"] = item.Importance,
            ["source"] = item.Source,
            ["timestamp"] = item.Timestamp.ToString("O")
        };

        if (item.Tags.Count > 0)
            metadata["tags"] = string.Join(",", item.Tags);

        return metadata;
    }

    /// <summary>
    /// Builds a MemoryItem from a Pinecone vector response.
    /// </summary>
    private static MemoryItem? BuildMemoryItem(PineconeVector vector)
    {
        if (vector.Metadata == null || !vector.Metadata.TryGetValue("content", out var contentObj))
            return null;

        var content = ConvertToString(contentObj);
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var importance = MemoryDefaults.DefaultImportance;
        string? source = null;

        if (vector.Metadata.TryGetValue("importance", out var imp))
            importance = ConvertToFloat(imp);
        if (vector.Metadata.TryGetValue("source", out var src))
            source = ConvertToString(src);

        var embedding = vector.Values?.Length > 0 ? vector.Values : null;
        return MemoryItem.Create(content, embedding, importance, source);
    }

    /// <summary>
    /// Builds a MemoryItem from a Pinecone query match.
    /// </summary>
    private static MemoryItem? BuildMemoryItem(PineconeMatch match)
    {
        if (match.Metadata == null || !match.Metadata.TryGetValue("content", out var contentObj))
            return null;

        var content = ConvertToString(contentObj);
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var importance = MemoryDefaults.DefaultImportance;
        string? source = null;

        if (match.Metadata.TryGetValue("importance", out var imp))
            importance = ConvertToFloat(imp);
        if (match.Metadata.TryGetValue("source", out var src))
            source = ConvertToString(src);

        var embedding = match.Values?.Length > 0 ? match.Values : null;
        return MemoryItem.Create(content, embedding, importance, source);
    }

    // --- Internal DTO classes for Pinecone API responses ---

    internal sealed class PineconeVector
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("values")]
        public float[] Values { get; set; } = [];

        [JsonPropertyName("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    internal sealed class PineconeFetchResponse
    {
        [JsonPropertyName("vectors")]
        public Dictionary<string, PineconeVector>? Vectors { get; set; }
    }

    internal sealed class PineconeMatch
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("score")]
        public float Score { get; set; }

        [JsonPropertyName("values")]
        public float[]? Values { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object>? Metadata { get; set; }
    }

    internal sealed class PineconeQueryResponse
    {
        [JsonPropertyName("matches")]
        public List<PineconeMatch>? Matches { get; set; }
    }

    internal sealed class PineconeNamespaceStats
    {
        [JsonPropertyName("vectorCount")]
        public int VectorCount { get; set; }
    }

    internal sealed class PineconeIndexStatsResponse
    {
        [JsonPropertyName("namespaces")]
        public Dictionary<string, PineconeNamespaceStats>? Namespaces { get; set; }

        [JsonPropertyName("totalVectorCount")]
        public int TotalVectorCount { get; set; }
    }

    internal sealed class PineconeListVector
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    internal sealed class PineconeListResponse
    {
        [JsonPropertyName("vectors")]
        public List<PineconeListVector>? Vectors { get; set; }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Stored memory item with key: {Key} in Pinecone")]
    private partial void LogStoredMemoryItemWithKey(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Memory item not found with key: {Key} in Pinecone")]
    private partial void LogMemoryItemNotFoundWith(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Retrieved memory item with key: {Key} from Pinecone")]
    private partial void LogRetrievedMemoryItemWithKey(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Cannot update non-existent memory item with key: {Key} in Pinecone")]
    private partial void LogCannotUpdateNonExistentMemory(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Updated memory item with key: {Key} in Pinecone")]
    private partial void LogUpdatedMemoryItemWithKey(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Deleted memory item with key: {Key} from Pinecone, Success: {Success}")]
    private partial void LogDeletedMemoryItemWithKey(object key, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "No results found for query in Pinecone")]
    private partial void LogNoResultsFoundForQuery();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Found {Count} memory items in Pinecone")]
    private partial void LogFoundMemoryItemsInPinecone(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Cleared all memory items from Pinecone namespace: {Namespace}")]
    private partial void LogClearedAllMemoryItemsFrom(object @namespace);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Counted {Count} memory items in Pinecone")]
    private partial void LogCountedMemoryItemsInPinecone(int count);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Namespace {Namespace} not found in Pinecone, returning count 0")]
    private partial void LogNamespaceNotFoundInPinecone(object @namespace);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Pinecone list vectors endpoint not available, returning empty list")]
    private partial void LogPineconeListVectorsEndpointNot();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Listed {Count} memory keys from Pinecone (skip: {Skip}, take: {Take})")]
    private partial void LogListedMemoryKeysFromPinecone(int count, int skip, int take);

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
