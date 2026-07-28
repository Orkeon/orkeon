using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory.Pinecone;

/// <summary>
/// Native collection support of <see cref="PineconeMemoryProvider"/> (RAG-03/C2, plan §6.2):
/// each logical collection maps to a dedicated Pinecone namespace within the configured
/// index, so collection isolation is enforced server-side instead of by key prefixes.
/// Namespaces are created implicitly by Pinecone on first upsert. The single-namespace
/// members of the base provider keep operating on <see cref="PineconeOptions.Namespace"/>
/// unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the legacy single-namespace path, collection-scoped writes persist the item's
/// custom metadata properties (flattened into the vector metadata) and reads restore them,
/// so typed consumers (e.g. the RAG document store) round-trip their per-chunk metadata.
/// Reserved metadata keys (<c>content</c>, <c>importance</c>, <c>source</c>,
/// <c>timestamp</c>, <c>tags</c>) are never overridden by custom properties.
/// </para>
/// <para>
/// <see cref="MemoryFilter.Tags"/> cannot be compiled to a Pinecone metadata filter
/// (tags are stored as a single joined string) and is rejected with
/// <see cref="NotSupportedException"/> rather than silently ignored.
/// </para>
/// </remarks>
public partial class PineconeMemoryProvider : ICollectionAwareMemory
{
    /// <summary>Metadata keys reserved by the provider mapping (not custom properties).</summary>
    private static readonly string[] s_reservedMetadataKeys = ["content", "importance", "source", "timestamp", "tags"];

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

        var vector = new PineconeVector
        {
            Id = key,
            Values = embedding.ToArray(),
            Metadata = BuildScopedMetadata(item)
        };

        return UpsertScopedAsync(collection, [vector], cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Validate-first: every entry is checked (non-empty key, valid item, an embedding on
    /// the entry or the item — Pinecone vectors require values) before the single
    /// <c>/vectors/upsert</c> request is issued, so an invalid entry never results in a
    /// partially applied batch.
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

        var vectors = new PineconeVector[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            ArgumentNullException.ThrowIfNull(entry);
            ValidateKey(entry.Key);
            ValidateMemoryItem(entry.Item);

            var values = entry.Embedding?.ToArray() ?? entry.Item.Embedding?.ToArray();
            if (values is not { Length: > 0 })
            {
                throw new ArgumentException(
                    $"Entry '{entry.Key}' carries no embedding — Pinecone vectors require values " +
                    "(on the entry or the item).", nameof(entries));
            }

            vectors[i] = new PineconeVector
            {
                Id = entry.Key,
                Values = values,
                Metadata = BuildScopedMetadata(entry.Item)
            };
        }

        return UpsertScopedAsync(collection, vectors, cancellationToken);
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

        try
        {
            var payload = new
            {
                vector = embedding.ToArray(),
                topK,
                includeMetadata = true,
                includeValues = true,
                @namespace = collection,
                filter = BuildScopedFilter(filter)
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/query",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<PineconeQueryResponse>(s_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (result?.Matches == null || result.Matches.Count == 0)
                return Array.Empty<ScoredMemoryItem>();

            var scored = new List<ScoredMemoryItem>(result.Matches.Count);
            foreach (var match in result.Matches)
            {
                if (match.Score < minScore)
                    continue;

                var item = BuildScopedMemoryItem(match);
                if (item != null)
                    scored.Add(new ScoredMemoryItem(item, match.Score, match.Id));
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

        try
        {
            var payload = new
            {
                filter = BuildScopedFilter(filter),
                @namespace = collection
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/vectors/delete",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            LogDeletedByFilterFromNamespace(collection);
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
            var payload = new
            {
                deleteAll = true,
                @namespace = collection
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/vectors/delete",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            LogDroppedNamespace(collection);
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
        PineconeVector[] vectors,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                vectors,
                @namespace = collection
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/vectors/upsert",
                payload,
                s_jsonOptions,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            LogUpsertedIntoNamespace(vectors.Length, collection);
        }
        catch (Exception ex)
        {
            LogException(ex, "UpsertBatchAsync (collection-scoped)");
            throw;
        }
    }

    /// <summary>
    /// Builds the Pinecone vector metadata of an item for collection-scoped writes: the
    /// reserved provider keys plus the item's custom properties flattened verbatim
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
    /// Rebuilds a <see cref="MemoryItem"/> from a query match, restoring tags and custom
    /// properties (every non-reserved metadata key) in addition to the base fields.
    /// </summary>
    private static MemoryItem? BuildScopedMemoryItem(PineconeMatch match)
    {
        if (match.Metadata == null || !match.Metadata.TryGetValue("content", out var contentObj))
            return null;

        var content = ConvertToString(contentObj);
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var importance = Orkeon.Domain.Constants.Memory.MemoryDefaults.DefaultImportance;
        string? source = null;
        string[]? tags = null;
        Dictionary<string, string>? custom = null;

        foreach (var (metaKey, value) in match.Metadata)
        {
            if (string.Equals(metaKey, "content", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(metaKey, "timestamp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
            else
            {
                custom ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                custom[metaKey] = ConvertToString(value) ?? string.Empty;
            }
        }

        var vector = match.Values?.Length > 0 ? match.Values : null;
        return MemoryItem.Create(content, vector, importance, source, tags, customProperties: custom);
    }

    /// <summary>
    /// Compiles a typed <see cref="MemoryFilter"/> to a Pinecone metadata filter:
    /// one <c>$eq</c> per criterion.
    /// </summary>
    /// <exception cref="NotSupportedException">The filter carries tag criteria.</exception>
    private static Dictionary<string, object>? BuildScopedFilter(MemoryFilter? filter)
    {
        if (filter is null || filter.IsEmpty)
#pragma warning disable S1168 // null omits the filter from the payload; {} would send an empty filter
            return null;
#pragma warning restore S1168

        if (filter.Tags is { Count: > 0 })
        {
            throw new NotSupportedException(
                "MemoryFilter.Tags cannot be compiled to a Pinecone metadata filter (tags are stored " +
                "as a single joined string). Filter on custom properties or source instead.");
        }

        var pineconeFilter = new Dictionary<string, object>();

        if (filter.Source is not null)
            pineconeFilter["source"] = new Dictionary<string, object> { ["$eq"] = filter.Source };

        if (filter.CustomProperties is { Count: > 0 })
        {
            foreach (var (propertyKey, value) in filter.CustomProperties)
                pineconeFilter[propertyKey] = new Dictionary<string, object> { ["$eq"] = value };
        }

        return pineconeFilter;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Upserted {Count} memory items into Pinecone namespace: {Namespace}")]
    private partial void LogUpsertedIntoNamespace(int count, string @namespace);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Deleted memory items by filter from Pinecone namespace: {Namespace}")]
    private partial void LogDeletedByFilterFromNamespace(string @namespace);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Dropped Pinecone namespace: {Namespace}")]
    private partial void LogDroppedNamespace(string @namespace);
}
