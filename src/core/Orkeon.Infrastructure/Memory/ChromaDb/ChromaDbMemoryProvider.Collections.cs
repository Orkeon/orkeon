using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.ChromaDb;

/// <summary>
/// Native collection support of <see cref="ChromaDbMemoryProvider"/> (RAG-03/C2, plan §6.2):
/// each logical collection maps to a dedicated ChromaDB collection (created lazily via
/// <c>get_or_create</c> on the configured tenant/database), so collection isolation is
/// enforced server-side instead of by key prefixes. The single-collection members of the
/// base provider keep operating on <see cref="ChromaDbOptions.CollectionName"/> unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the legacy single-collection path, collection-scoped writes persist the item's
/// custom metadata properties (flattened into the ChromaDB metadata document) and reads
/// restore them, so typed consumers (e.g. the RAG document store) round-trip their
/// per-chunk metadata. Reserved metadata keys (<c>importance</c>, <c>source</c>,
/// <c>timestamp</c>, <c>tags</c>) are never overridden by custom properties.
/// </para>
/// <para>
/// <see cref="MemoryFilter.Tags"/> cannot be compiled to a ChromaDB <c>where</c> clause
/// (tags are stored as a single joined string) and is rejected with
/// <see cref="NotSupportedException"/> rather than silently ignored.
/// </para>
/// </remarks>
public partial class ChromaDbMemoryProvider : ICollectionAwareMemory
{
    /// <summary>Metadata keys reserved by the provider mapping (not custom properties).</summary>
    private static readonly string[] s_reservedMetadataKeys = ["importance", "source", "timestamp", "tags"];

    /// <summary>Lazily resolved ChromaDB collection ids, one per logical collection.</summary>
    private readonly ConcurrentDictionary<string, string> _scopedCollectionIds = new(StringComparer.Ordinal);

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
            [key],
            [item.Content],
            [BuildScopedMetadata(item)],
            [embedding.ToArray()],
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Validate-first: every entry is checked (non-empty key, valid item, an embedding on
    /// the entry or the item) before the single <c>upsert</c> request is issued, so an
    /// invalid entry never results in a partially applied batch.
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

        var ids = new string[entries.Count];
        var documents = new string[entries.Count];
        var metadatas = new Dictionary<string, object>[entries.Count];
        var embeddings = new float[entries.Count][];

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            ArgumentNullException.ThrowIfNull(entry);
            ValidateKey(entry.Key);
            ValidateMemoryItem(entry.Item);

            var vector = entry.Embedding?.ToArray() ?? entry.Item.Embedding?.ToArray();
            if (vector is not { Length: > 0 })
            {
                throw new ArgumentException(
                    $"Entry '{entry.Key}' carries no embedding — ChromaDB collection-scoped upserts " +
                    "require one (on the entry or the item).", nameof(entries));
            }

            ids[i] = entry.Key;
            documents[i] = entry.Item.Content;
            metadatas[i] = BuildScopedMetadata(entry.Item);
            embeddings[i] = vector;
        }

        return UpsertScopedAsync(collection, ids, documents, metadatas, embeddings, cancellationToken);
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

        var collectionId = await EnsureScopedCollectionAsync(collection, cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new
            {
                query_embeddings = new[] { embedding.ToArray() },
                n_results = topK,
                where = BuildScopedWhereClause(filter),
                include = s_similarityQueryIncludes
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{collectionId}/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<ChromaQueryResponse>(s_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (result?.Ids == null || result.Ids.Count == 0 || result.Ids[0].Count == 0)
                return Array.Empty<ScoredMemoryItem>();

            var scored = new List<ScoredMemoryItem>(result.Ids[0].Count);
            for (var i = 0; i < result.Ids[0].Count; i++)
            {
                var item = BuildScopedMemoryItem(result, 0, i);
                if (item == null)
                    continue;

                var score = GetSimilarityScore(result, 0, i);
                if (score >= minScore)
                    scored.Add(new ScoredMemoryItem(item, score, result.Ids[0][i]));
            }

            return scored.OrderByDescending(s => s.Score).ToList();
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

        var collectionId = await EnsureScopedCollectionAsync(collection, cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new { where = BuildScopedWhereClause(filter) };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{collectionId}/delete",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            LogDeletedByFilterFromCollection(collection);
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
            var response = await _httpClient.DeleteAsync(
                new Uri($"{_collectionsRoute}/{Uri.EscapeDataString(collection)}", UriKind.Relative),
                cancellationToken).ConfigureAwait(false);

            // Absent collection: dropping is a documented no-op.
            if (response.StatusCode != HttpStatusCode.NotFound)
                response.EnsureSuccessStatusCode();

            _scopedCollectionIds.TryRemove(collection, out _);
            LogDroppedCollection(collection);
        }
        catch (Exception ex)
        {
            LogException(ex, "DropCollectionAsync");
            throw;
        }
    }

    // --- Collection-scoped helpers ---

    /// <summary>
    /// Resolves (and caches) the ChromaDB collection id of a logical collection, creating
    /// the collection lazily via <c>get_or_create</c>.
    /// </summary>
    private async Task<string> EnsureScopedCollectionAsync(string collection, CancellationToken cancellationToken)
    {
        if (_scopedCollectionIds.TryGetValue(collection, out var cached))
            return cached;

        var payload = new { name = collection, get_or_create = true };

        var response = await _httpClient.PostAsJsonAsync(
            _collectionsRoute,
            payload,
            s_jsonOptions,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<ChromaCollectionResponse>(s_jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var id = result?.Id
            ?? throw new InvalidOperationException($"Failed to create or get ChromaDB collection '{collection}'.");

        _scopedCollectionIds[collection] = id;
        LogEnsuredChromadbCollectionExistsId(collection, id);
        return id;
    }

    private async Task UpsertScopedAsync(
        string collection,
        string[] ids,
        string[] documents,
        Dictionary<string, object>[] metadatas,
        float[][] embeddings,
        CancellationToken cancellationToken)
    {
        var collectionId = await EnsureScopedCollectionAsync(collection, cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = new { ids, documents, metadatas, embeddings };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_collectionsRoute}/{collectionId}/upsert",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            LogUpsertedIntoCollection(ids.Length, collection);
        }
        catch (Exception ex)
        {
            LogException(ex, "UpsertBatchAsync (collection-scoped)");
            throw;
        }
    }

    /// <summary>
    /// Builds the ChromaDB metadata document of an item for collection-scoped writes:
    /// the reserved provider keys plus the item's custom properties flattened verbatim
    /// (reserved keys win on collision).
    /// </summary>
    private static Dictionary<string, object> BuildScopedMetadata(MemoryItem item)
    {
        var metadata = BuildMetadata(item);

        if (item.Metadata.CustomProperties is { Count: > 0 } custom)
        {
            foreach (var (propertyKey, value) in custom)
            {
                if (!s_reservedMetadataKeys.Contains(propertyKey, StringComparer.OrdinalIgnoreCase))
                    metadata[propertyKey] = value;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Rebuilds a <see cref="MemoryItem"/> from a query response row, restoring tags and
    /// custom properties (every non-reserved metadata key) in addition to the base fields.
    /// </summary>
    private static MemoryItem? BuildScopedMemoryItem(ChromaQueryResponse response, int queryIndex, int resultIndex)
    {
        if (response.Documents == null ||
            queryIndex >= response.Documents.Count ||
            resultIndex >= response.Documents[queryIndex].Count)
            return null;

        var content = response.Documents[queryIndex][resultIndex];
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var importance = Orkeon.Domain.Constants.Memory.MemoryDefaults.DefaultImportance;
        string? source = null;
        string[]? tags = null;
        Dictionary<string, string>? custom = null;

        if (response.Metadatas != null &&
            queryIndex < response.Metadatas.Count &&
            resultIndex < response.Metadatas[queryIndex].Count &&
            response.Metadatas[queryIndex][resultIndex] is { } meta)
        {
            foreach (var (metaKey, value) in meta)
            {
                if (string.Equals(metaKey, "importance", StringComparison.OrdinalIgnoreCase))
                {
                    importance = ConvertToFloat(value);
                }
                else if (string.Equals(metaKey, "source", StringComparison.OrdinalIgnoreCase))
                {
                    source = ConvertToString(value);
                }
                else if (string.Equals(metaKey, "tags", StringComparison.OrdinalIgnoreCase))
                {
                    tags = ConvertToString(value)?.Split(',', StringSplitOptions.RemoveEmptyEntries);
                }
                else if (!string.Equals(metaKey, "timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    custom ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    custom[metaKey] = ConvertToString(value) ?? string.Empty;
                }
            }
        }

        return MemoryItem.Create(content, importance: importance, source: source, tags: tags, customProperties: custom);
    }

    /// <summary>
    /// Compiles a typed <see cref="MemoryFilter"/> to a ChromaDB <c>where</c> clause:
    /// one <c>$eq</c> per criterion, wrapped in <c>$and</c> when several apply.
    /// </summary>
    /// <exception cref="NotSupportedException">The filter carries tag criteria.</exception>
    private static Dictionary<string, object>? BuildScopedWhereClause(MemoryFilter? filter)
    {
        if (filter is null || filter.IsEmpty)
#pragma warning disable S1168 // null omits the `where` key from the payload; {} would send an empty clause
            return null;
#pragma warning restore S1168

        if (filter.Tags is { Count: > 0 })
        {
            throw new NotSupportedException(
                "MemoryFilter.Tags cannot be compiled to a ChromaDB where clause (tags are stored " +
                "as a single joined string). Filter on custom properties or source instead.");
        }

        var conditions = new List<Dictionary<string, object>>();

        if (filter.Source is not null)
            conditions.Add(new Dictionary<string, object> { ["source"] = new Dictionary<string, object> { ["$eq"] = filter.Source } });

        if (filter.CustomProperties is { Count: > 0 })
        {
            foreach (var (propertyKey, value) in filter.CustomProperties)
                conditions.Add(new Dictionary<string, object> { [propertyKey] = new Dictionary<string, object> { ["$eq"] = value } });
        }

        return conditions.Count == 1
            ? conditions[0]
            : new Dictionary<string, object> { ["$and"] = conditions };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Upserted {Count} memory items into ChromaDB collection: {Collection}")]
    private partial void LogUpsertedIntoCollection(int count, string collection);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Deleted memory items by filter from ChromaDB collection: {Collection}")]
    private partial void LogDeletedByFilterFromCollection(string collection);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Dropped ChromaDB collection: {Collection}")]
    private partial void LogDroppedCollection(string collection);
}
