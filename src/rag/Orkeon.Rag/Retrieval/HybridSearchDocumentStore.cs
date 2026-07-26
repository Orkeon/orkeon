using System.Collections.Concurrent;
using System.Collections.Immutable;
using Orkeon.Domain.Memory;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Retrieval;

/// <summary>
/// <see cref="IDocumentStore"/> decorator adding hybrid (lexical + vector) retrieval
/// (RAG-04/C2, plan §5): upserts are delegated to the inner store <b>and</b> indexed into
/// a per-collection in-process <see cref="Bm25Index"/>; searches fuse the inner store's
/// vector ranking with the BM25 ranking via Reciprocal Rank Fusion — unless the backing
/// memory provider exposes native hybrid search, which is preferred.
/// </summary>
/// <remarks>
/// <para><b>Why a decorator</b> — the alternative (rebuilding a BM25 index per query by
/// enumerating the collection) is impossible in general: <see cref="IDocumentStore"/> has
/// no enumeration surface, and neither does <c>IMemoryProvider</c> for native collections.
/// The decorator keeps <c>Stores/</c> untouched, works over any inner store (including a
/// host-registered one), and pays the indexing cost at ingestion, not per query.</para>
/// <para><b>Score provenance</b> (see <see cref="ScoredChunk.ScoreOrigin"/>):</para>
/// <list type="number">
///   <item><description><c>hybrid-native</c> — the backing provider implements
///   <c>IHybridSearchCapable</c> (discovered with <c>TryGetCapability</c>): the query is
///   fused server/provider-side. When the provider also implements
///   <c>ICollectionAwareMemory</c> (chunks live in real per-collection containers), the
///   collection-scoped hybrid overload is used; otherwise the default-container overload
///   with the store's collection metadata filter.</description></item>
///   <item><description><c>rrf</c> — emulated path: inner vector results + in-process
///   BM25 results fused with RRF. The RRF score is a rank aggregate, <b>not</b> a
///   similarity (see <see cref="ReciprocalRankFusion"/>).</description></item>
///   <item><description>Inner origin (<c>vector</c>/<c>local-cosine</c>) — passthrough
///   when the lexical side has nothing to contribute (empty query text or no BM25 match):
///   the inner scores are more informative than a single-list RRF transform.</description></item>
/// </list>
/// <para><b>In-process index limits</b> — the BM25 side only covers chunks upserted
/// through <i>this</i> decorator instance in the current process (memory cost grows with
/// the corpus; nothing is persisted). Pre-existing collection content still surfaces
/// through the vector list. At scale, prefer a provider with native hybrid search
/// (LanceDB) — the decorator switches to it automatically.</para>
/// </remarks>
public sealed class HybridSearchDocumentStore : IDocumentStore
{
    /// <summary><see cref="ScoredChunk.ScoreOrigin"/> of provider-fused hybrid scores.</summary>
    public const string HybridNativeScoreOrigin = "hybrid-native";

    private readonly IDocumentStore _inner;
    private readonly HybridRetrievalOptions _options;
    private readonly IMemoryProvider? _provider;
    private readonly ConcurrentDictionary<string, Bm25Index> _indexes = new(StringComparer.Ordinal);

    /// <summary>Initializes the decorator.</summary>
    /// <param name="inner">The decorated document store.</param>
    /// <param name="options">Hybrid options (RRF constant).</param>
    /// <param name="provider">
    /// The memory provider backing <paramref name="inner"/>, used only to discover and
    /// call native hybrid search; <see langword="null"/> disables the native path (the
    /// emulated BM25 + RRF path still works).
    /// </param>
    public HybridSearchDocumentStore(
        IDocumentStore inner,
        HybridRetrievalOptions options,
        IMemoryProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RrfK, nameof(options));
        _inner = inner;
        _options = options;
        _provider = provider;
    }

    /// <inheritdoc />
    /// <remarks>Delegates first (inner validation wins — a rejected batch is never indexed),
    /// then indexes the chunks into the collection's BM25 index.</remarks>
    public async Task UpsertAsync(
        string collection,
        IReadOnlyList<EmbeddedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await _inner.UpsertAsync(collection, chunks, cancellationToken).ConfigureAwait(false);

        if (chunks is { Count: > 0 })
            Index(collection).UpsertRange(chunks.Select(embedded => embedded.Chunk));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        string collection,
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Native path: the provider fuses text + vector itself (server-side BM25/FTS).
        if (_provider is not null
            && !string.IsNullOrWhiteSpace(query.Text)
            && query.Embedding is { IsDefaultOrEmpty: false } embedding
            && _provider.TryGetCapability<IHybridSearchCapable>(out var hybrid))
        {
            try
            {
                var hits = _provider.TryGetCapability<ICollectionAwareMemory>(out _)
                    ? await hybrid.HybridSearchAsync(
                            collection, query.Text, embedding.AsMemory(), query.TopK,
                            BuildChunkFilter(collection: null, query.Filters), cancellationToken)
                        .ConfigureAwait(false)
                    : await hybrid.HybridSearchAsync(
                            query.Text, embedding.AsMemory(), query.TopK,
                            BuildChunkFilter(collection, query.Filters), cancellationToken)
                        .ConfigureAwait(false);

                return RagChunkMemoryMapper.ToScoredChunks(hits, HybridNativeScoreOrigin);
            }
            catch (NotSupportedException)
            {
                // Collection-scoped overload not implemented by this provider —
                // fall through to the emulated hybrid path.
            }
        }

        // Emulated path: inner vector ranking + in-process BM25, fused with RRF.
        var vectorHits = await _inner.SearchAsync(collection, query, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ScoredChunk> bm25Hits =
            string.IsNullOrWhiteSpace(query.Text) || !_indexes.TryGetValue(collection, out var index)
                ? []
                : index.Search(query.Text, query.TopK);

        // Nothing lexical to fuse: keep the inner scores (their origin is more informative
        // than a single-list RRF transform).
        if (bm25Hits.Count == 0)
            return vectorHits;

        return ReciprocalRankFusion.Fuse(_options.RrfK, query.TopK, vectorHits, bm25Hits);
    }

    /// <inheritdoc />
    /// <remarks>Delegates first, then purges the source's chunks from the BM25 index.</remarks>
    public async Task DeleteBySourceAsync(
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        await _inner.DeleteBySourceAsync(collection, sourceId, cancellationToken).ConfigureAwait(false);

        if (_indexes.TryGetValue(collection, out var index))
            index.RemoveSource(sourceId);
    }

    private Bm25Index Index(string collection) =>
        _indexes.GetOrAdd(collection, _ => new Bm25Index());

    /// <summary>
    /// Builds the chunk metadata filter of the native path, mirroring the document
    /// store's layout: always <c>rag.kind = chunk</c> plus the caller's <c>meta.*</c>
    /// criteria; <paramref name="collection"/> (default-container layout only) adds the
    /// <c>rag.collection</c> criterion — collection-scoped calls pass the collection to
    /// the provider instead.
    /// </summary>
    private static MemoryFilter BuildChunkFilter(
        string? collection,
        ImmutableDictionary<string, string> queryFilters)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [RagChunkMemoryMapper.KindProperty] = RagChunkMemoryMapper.ChunkKind,
        };

        if (collection is not null)
            properties[RagChunkMemoryMapper.CollectionProperty] = collection;

        foreach (var (key, value) in queryFilters)
            properties[RagChunkMemoryMapper.MetadataPrefix + key] = value;

        return new MemoryFilter { CustomProperties = properties };
    }
}
