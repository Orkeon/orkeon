using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Stores;

/// <summary>
/// <see cref="IDocumentStore"/> adapter over any <see cref="IMemoryProvider"/>, using the
/// optional Domain vector capabilities when available (RAG-02/C4, plan §6.2).
/// </summary>
/// <remarks>
/// <para><b>Key schema</b> (plan §6.2, prefixed keys for key-value providers):</para>
/// <list type="bullet">
///   <item><description>Chunk: <c>rag:{collection}:{sourceHash}:{chunkIndex}</c> where
///   <c>sourceHash</c> is the first 16 lowercase hex characters of SHA-256(UTF-8(sourceId))
///   — source ids are arbitrary strings (paths, URLs) and cannot be embedded verbatim in a
///   colon-delimited key. <c>chunkIndex</c> is <see cref="Chunk.Index"/> (invariant culture),
///   assumed unique within a source.</description></item>
///   <item><description>Per-source manifest: <c>rag:{collection}:{sourceHash}:manifest</c> —
///   newline-separated list of the source's chunk keys (cannot collide with chunk keys,
///   whose last segment is numeric).</description></item>
///   <item><description>Collection registry: <c>rag:{collection}:sources</c> —
///   newline-separated list of the collection's source hashes.</description></item>
/// </list>
/// <para>
/// Keys are only ever generated, never parsed. Collection names must not contain <c>':'</c>
/// (validated) so distinct collections can never produce colliding keys. The collection is
/// additionally carried as a metadata property so searches filter by metadata, not by key
/// prefix. Metadata property values are matched case-insensitively by
/// <see cref="MemoryFilter"/>, so collection names differing only by case are not isolated.
/// </para>
/// <para><b>Score provenance</b> — scores are never invented:</para>
/// <list type="number">
///   <item><description><see cref="IScoredVectorSearch"/> capability available → native
///   provider scores end to end, <see cref="ScoredChunk.ScoreOrigin"/> = <c>vector</c>.</description></item>
///   <item><description>Otherwise the legacy <see cref="IMemoryProvider.SearchSimilarAsync"/>
///   is tried: providers that implement it return true cosine similarities, so hits also
///   carry <c>vector</c> (the interface's default body returns empty, which falls
///   through).</description></item>
///   <item><description>Otherwise cosine similarity is recomputed locally from the stored
///   embeddings (enumerated via registry + manifests + <see cref="IMemoryProvider.GetAsync"/>),
///   <c>ScoreOrigin</c> = <c>local-cosine</c>.</description></item>
/// </list>
/// <para>
/// No path emits a rank-derived score, so the <c>rank</c> origin is never produced by this
/// adapter. All three paths pass no similarity threshold (callers apply their own cut-offs).
/// </para>
/// <para><b>Deletion complexity</b>: <see cref="DeleteBySourceAsync"/> reads the source's
/// manifest (1 <c>GetAsync</c>) and deletes its N chunk keys plus the manifest and the
/// registry entry — O(N) provider calls in the number of chunks of that source, with no
/// store-wide scan (the <see cref="IMemoryProvider"/> surface offers no enumeration API).
/// </para>
/// <para><b>Concurrency</b>: manifest and registry updates are read-merge-write and therefore
/// best-effort under concurrent writers to the same (collection, source) — same guarantees as
/// the underlying provider, no cross-call locking.</para>
/// </remarks>
public sealed class MemoryProviderDocumentStore : IDocumentStore
{
    /// <summary><see cref="ScoredChunk.ScoreOrigin"/> for provider-computed similarity scores.</summary>
    public const string VectorScoreOrigin = "vector";

    /// <summary><see cref="ScoredChunk.ScoreOrigin"/> for locally recomputed cosine scores.</summary>
    public const string LocalCosineScoreOrigin = "local-cosine";

    private const string KeyPrefix = "rag";
    private const string ManifestSegment = "manifest";
    private const string RegistrySegment = "sources";
    private const int SourceHashLength = 16;

    private const string KindProperty = "rag.kind";
    private const string CollectionProperty = "rag.collection";
    private const string DocumentIdProperty = "rag.document_id";
    private const string ChunkIdProperty = "rag.chunk_id";
    private const string IndexProperty = "rag.index";
    private const string StartOffsetProperty = "rag.start_offset";
    private const string EndOffsetProperty = "rag.end_offset";
    private const string EmbeddingModelProperty = "rag.embedding_model";
    private const string MetadataPrefix = "meta.";

    private const string ChunkKind = "chunk";
    private const string ManifestKind = "manifest";
    private const string RegistryKind = "registry";

    private readonly IMemoryProvider _provider;

    /// <summary>Initializes the store over <paramref name="provider"/>.</summary>
    /// <param name="provider">The backing memory provider (possibly decorated).</param>
    public MemoryProviderDocumentStore(IMemoryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ValidateCollection(collection);
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Count == 0)
            return;

        // Last write wins on key collisions within the batch — upsert semantics.
        var entries = new Dictionary<string, (MemoryItem Item, float[] Embedding)>(StringComparer.Ordinal);
        var keysBySource = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var embedded in chunks)
        {
            ArgumentNullException.ThrowIfNull(embedded);

            if (embedded.Embedding.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    $"Chunk '{embedded.Chunk.Id}' has no embedding — embed chunks before upserting them.",
                    nameof(chunks));
            }

            var chunk = embedded.Chunk;
            var key = ChunkKey(collection, SourceHash(chunk.SourceId), chunk.Index);
            entries[key] = (ToMemoryItem(collection, embedded), [.. embedded.Embedding]);

            if (!keysBySource.TryGetValue(chunk.SourceId, out var keys))
            {
                keys = new SortedSet<string>(StringComparer.Ordinal);
                keysBySource[chunk.SourceId] = keys;
            }

            keys.Add(key);
        }

        if (_provider.TryGetCapability<IBatchUpsert>(out var batchUpsert))
        {
            var batch = entries
                .Select(pair => new MemoryUpsertEntry(pair.Key, pair.Value.Item, pair.Value.Embedding))
                .ToList();
            await batchUpsert.UpsertBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var (key, (item, embedding)) in entries)
            {
                await _provider.StoreWithEmbeddingAsync(key, item, embedding, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var (sourceId, keys) in keysBySource)
            await MergeManifestAsync(collection, sourceId, keys, cancellationToken).ConfigureAwait(false);

        await RegisterSourcesAsync(collection, keysBySource.Keys, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string collection,
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateCollection(collection);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Embedding is not { IsDefaultOrEmpty: false } embedding)
        {
            throw new ArgumentException(
                "RetrievalQuery.Embedding is required: MemoryProviderDocumentStore does not embed " +
                "queries — compute the query embedding upstream (RAG pipeline) before searching.",
                nameof(query));
        }

        if (query.TopK <= 0)
        {
            throw new ArgumentException(
                $"RetrievalQuery.TopK must be positive (got {query.TopK}).", nameof(query));
        }

        var filter = BuildChunkFilter(collection, query.Filters);

        // 1. Native capability: provider scores preserved end to end.
        if (_provider.TryGetCapability<IScoredVectorSearch>(out var scoredSearch))
        {
            var hits = await scoredSearch
                .SearchSimilarWithScoresAsync(
                    embedding.AsMemory(), query.TopK, float.MinValue, filter, cancellationToken)
                .ConfigureAwait(false);

            return hits.Select(hit => ToScoredChunk(hit.Item, hit.Score, VectorScoreOrigin)).ToList();
        }

        // 2. Legacy vector search: providers that implement it return true cosine similarities.
        //    The interface's default body returns empty, which falls through to the local path.
        var legacyHits = await _provider
            .SearchSimilarAsync(
                [.. embedding], query.TopK, float.MinValue, filter.ToDictionary(), cancellationToken)
            .ConfigureAwait(false);

        if (legacyHits.Count > 0)
            return legacyHits.Select(hit => ToScoredChunk(hit.Item, hit.Score, VectorScoreOrigin)).ToList();

        // 3. Honest last resort: recompute cosine locally from the stored embeddings.
        return await SearchByLocalCosineAsync(collection, embedding, query.TopK, filter, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteBySourceAsync(
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ValidateCollection(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var sourceHash = SourceHash(sourceId);
        var manifestKey = ManifestKey(collection, sourceHash);
        var manifest = await _provider.GetAsync(manifestKey, cancellationToken).ConfigureAwait(false);

        if (manifest is null)
            return; // Unknown source in this collection — nothing to delete.

        foreach (var chunkKey in ParseLines(manifest.Content))
            await _provider.DeleteAsync(chunkKey, cancellationToken).ConfigureAwait(false);

        await _provider.DeleteAsync(manifestKey, cancellationToken).ConfigureAwait(false);

        var registryKey = RegistryKey(collection);
        var registry = await _provider.GetAsync(registryKey, cancellationToken).ConfigureAwait(false);
        if (registry is null)
            return;

        var hashes = new SortedSet<string>(ParseLines(registry.Content), StringComparer.Ordinal);
        if (!hashes.Remove(sourceHash))
            return;

        if (hashes.Count == 0)
        {
            await _provider.DeleteAsync(registryKey, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _provider
                .StoreAsync(registryKey, RegistryItem(collection, hashes), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ScoredChunk>> SearchByLocalCosineAsync(
        string collection,
        ImmutableArray<float> embedding,
        int topK,
        MemoryFilter filter,
        CancellationToken cancellationToken)
    {
        var registry = await _provider.GetAsync(RegistryKey(collection), cancellationToken)
            .ConfigureAwait(false);
        if (registry is null)
            return [];

        var scored = new List<(MemoryItem Item, float Score)>();

        foreach (var sourceHash in ParseLines(registry.Content))
        {
            var manifest = await _provider
                .GetAsync(ManifestKey(collection, sourceHash), cancellationToken)
                .ConfigureAwait(false);
            if (manifest is null)
                continue;

            foreach (var chunkKey in ParseLines(manifest.Content))
            {
                var item = await _provider.GetAsync(chunkKey, cancellationToken).ConfigureAwait(false);

                if (item?.Embedding is not { Count: > 0 } stored
                    || stored.Count != embedding.Length
                    || !filter.Matches(item))
                {
                    continue;
                }

                scored.Add((item, VectorMath.CosineSimilarity(embedding.AsSpan(), [.. stored])));
            }
        }

        return scored
            .OrderByDescending(entry => entry.Score)
            .Take(topK)
            .Select(entry => ToScoredChunk(entry.Item, entry.Score, LocalCosineScoreOrigin))
            .ToList();
    }

    private async Task MergeManifestAsync(
        string collection,
        string sourceId,
        IReadOnlyCollection<string> newKeys,
        CancellationToken cancellationToken)
    {
        var manifestKey = ManifestKey(collection, SourceHash(sourceId));
        var existing = await _provider.GetAsync(manifestKey, cancellationToken).ConfigureAwait(false);

        var keys = new SortedSet<string>(ParseLines(existing?.Content), StringComparer.Ordinal);
        keys.UnionWith(newKeys);

        var item = MemoryItem.Create(
            content: string.Join('\n', keys),
            source: sourceId,
            customProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [KindProperty] = ManifestKind,
                [CollectionProperty] = collection,
            });

        await _provider.StoreAsync(manifestKey, item, cancellationToken).ConfigureAwait(false);
    }

    private async Task RegisterSourcesAsync(
        string collection,
        IReadOnlyCollection<string> sourceIds,
        CancellationToken cancellationToken)
    {
        var registryKey = RegistryKey(collection);
        var existing = await _provider.GetAsync(registryKey, cancellationToken).ConfigureAwait(false);

        var hashes = new SortedSet<string>(ParseLines(existing?.Content), StringComparer.Ordinal);
        var countBefore = hashes.Count;

        foreach (var sourceId in sourceIds)
            hashes.Add(SourceHash(sourceId));

        if (existing is not null && hashes.Count == countBefore)
            return; // Registry already up to date.

        await _provider
            .StoreAsync(registryKey, RegistryItem(collection, hashes), cancellationToken)
            .ConfigureAwait(false);
    }

    private static MemoryItem RegistryItem(string collection, IReadOnlyCollection<string> hashes)
    {
        return MemoryItem.Create(
            content: string.Join('\n', hashes),
            source: "rag-registry",
            customProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [KindProperty] = RegistryKind,
                [CollectionProperty] = collection,
            });
    }

    private static MemoryItem ToMemoryItem(string collection, EmbeddedChunk embedded)
    {
        var chunk = embedded.Chunk;
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [KindProperty] = ChunkKind,
            [CollectionProperty] = collection,
            [DocumentIdProperty] = chunk.DocumentId,
            [ChunkIdProperty] = chunk.Id,
            [IndexProperty] = chunk.Index.ToString(CultureInfo.InvariantCulture),
            [StartOffsetProperty] = chunk.StartOffset.ToString(CultureInfo.InvariantCulture),
            [EndOffsetProperty] = chunk.EndOffset.ToString(CultureInfo.InvariantCulture),
        };

        if (embedded.EmbeddingModel is { Length: > 0 } model)
            properties[EmbeddingModelProperty] = model;

        foreach (var (key, value) in chunk.Metadata)
            properties[MetadataPrefix + key] = value;

        return MemoryItem.Create(
            content: chunk.Content,
            source: chunk.SourceId,
            customProperties: properties);
    }

    private static MemoryFilter BuildChunkFilter(
        string collection,
        ImmutableDictionary<string, string> queryFilters)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [KindProperty] = ChunkKind,
            [CollectionProperty] = collection,
        };

        foreach (var (key, value) in queryFilters)
            properties[MetadataPrefix + key] = value;

        return new MemoryFilter { CustomProperties = properties };
    }

    private static ScoredChunk ToScoredChunk(MemoryItem item, double score, string scoreOrigin)
    {
        return new ScoredChunk
        {
            Chunk = ToChunk(item),
            Score = score,
            ScoreOrigin = scoreOrigin,
        };
    }

    private static Chunk ToChunk(MemoryItem item)
    {
        var properties = item.Metadata.CustomProperties
            ?? throw CorruptEntry(item, KindProperty);

        var metadata = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var (key, value) in properties)
        {
            if (key.StartsWith(MetadataPrefix, StringComparison.Ordinal))
                metadata[key[MetadataPrefix.Length..]] = value;
        }

        return new Chunk
        {
            Id = RequireProperty(properties, ChunkIdProperty, item),
            DocumentId = RequireProperty(properties, DocumentIdProperty, item),
            SourceId = item.Source,
            Content = item.Content,
            Index = RequireInt(properties, IndexProperty, item),
            StartOffset = RequireInt(properties, StartOffsetProperty, item),
            EndOffset = RequireInt(properties, EndOffsetProperty, item),
            Metadata = metadata.ToImmutable(),
        };
    }

    private static string RequireProperty(
        Dictionary<string, string> properties, string name, MemoryItem item)
    {
        return properties.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : throw CorruptEntry(item, name);
    }

    private static int RequireInt(
        Dictionary<string, string> properties, string name, MemoryItem item)
    {
        return properties.TryGetValue(name, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw CorruptEntry(item, name);
    }

    private static InvalidOperationException CorruptEntry(MemoryItem item, string property)
    {
        return new InvalidOperationException(
            $"Memory item '{item.Id}' is not a valid RAG chunk entry: " +
            $"missing or invalid property '{property}'.");
    }

    private static void ValidateCollection(string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        if (collection.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Collection name '{collection}' must not contain ':' (reserved as the key separator).",
                nameof(collection));
        }
    }

    private static string SourceHash(string sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceId));
        return Convert.ToHexStringLower(hash)[..SourceHashLength];
    }

    private static string ChunkKey(string collection, string sourceHash, int chunkIndex) =>
        $"{KeyPrefix}:{collection}:{sourceHash}:{chunkIndex.ToString(CultureInfo.InvariantCulture)}";

    private static string ManifestKey(string collection, string sourceHash) =>
        $"{KeyPrefix}:{collection}:{sourceHash}:{ManifestSegment}";

    private static string RegistryKey(string collection) =>
        $"{KeyPrefix}:{collection}:{RegistrySegment}";

    private static string[] ParseLines(string? content)
    {
        return content is null
            ? []
            : content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
