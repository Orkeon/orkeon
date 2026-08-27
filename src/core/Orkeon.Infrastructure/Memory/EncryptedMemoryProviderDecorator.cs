using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Generic decorator that encrypts memory content before storing and decrypts after retrieval.
/// Wraps any <see cref="IMemoryProvider"/> implementation (Redis, SQLite, in-memory, etc.).
/// Metadata and embeddings are NOT encrypted (needed for search/indexing): vector search
/// (<see cref="StoreWithEmbeddingAsync(string, MemoryItem, float[], CancellationToken)"/>/<see cref="SearchSimilarAsync"/>)
/// is delegated to the inner provider and result contents are decrypted on the way out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Conditional capability forwarding (RAG-02/C4).</b> C# has no conditional interface
/// implementation, so this decorator statically implements every optional capability
/// (<see cref="IScoredVectorSearch"/>, <see cref="IBatchUpsert"/>,
/// <see cref="IHybridSearchCapable"/>, <see cref="ICollectionAwareMemory"/>) and reports the ones the wrapped provider actually has
/// through <see cref="IMemoryCapabilityProbe"/>. Discover capabilities with
/// <see cref="MemoryCapabilityExtensions.TryGetCapability{TCapability}"/> — a raw
/// <c>is IScoredVectorSearch</c> test on the decorator is always true and therefore NOT a
/// reliable capability check. Calling a capability member the inner provider lacks throws
/// <see cref="NotSupportedException"/>.
/// </para>
/// <para>
/// Encryption semantics are unchanged for capabilities: batch upserts encrypt content before
/// delegation (embeddings stay clear for indexing); scored/hybrid search delegates to the
/// inner provider and decrypts result contents on the way out, preserving scores and keys.
/// </para>
/// </remarks>
public sealed partial class EncryptedMemoryProviderDecorator
    : IMemoryProvider, IMemoryCapabilityProbe, IScoredVectorSearch, IBatchUpsert, IHybridSearchCapable, ICollectionAwareMemory
{
    private readonly IMemoryProvider _inner;
    private readonly IEncryptionProvider _encryption;
    private readonly ILogger<EncryptedMemoryProviderDecorator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedMemoryProviderDecorator"/> class.
    /// </summary>
    /// <param name="inner">The underlying memory provider to wrap.</param>
    /// <param name="encryption">The encryption provider for content protection.</param>
    /// <param name="logger">The logger instance.</param>
    public EncryptedMemoryProviderDecorator(
        IMemoryProvider inner,
        IEncryptionProvider encryption,
        ILogger<EncryptedMemoryProviderDecorator> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        ArgumentNullException.ThrowIfNull(encryption);
        _encryption = encryption;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return StoreCoreAsync();

        async Task StoreCoreAsync()
        {
            if (!_encryption.IsEnabled)
            {
                await _inner.StoreAsync(key, item, cancellationToken).ConfigureAwait(false);
                return;
            }

            var encryptedItem = await EncryptItemAsync(item, cancellationToken).ConfigureAwait(false);

            await _inner.StoreAsync(key, encryptedItem, cancellationToken).ConfigureAwait(false);
            LogStoredEncryptedMemoryItemWith(key);
        }
    }

    /// <inheritdoc />
    public async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var item = await _inner.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (item == null || !_encryption.IsEnabled)
            return item;

        var decryptedItem = await DecryptItemAsync(item, cancellationToken).ConfigureAwait(false);

        LogRetrievedAndDecryptedMemoryItem(key);
        return decryptedItem;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-item decrypt fault barrier: an item whose content fails to decrypt (e.g. wrong key / corrupt ciphertext) is logged and skipped so the remaining search results are still returned.")]
    public async Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken cancellationToken = default)
    {
        var items = await _inner.SearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
        if (!_encryption.IsEnabled)
            return items;

        var decryptedItems = new List<MemoryItem>();
        foreach (var item in items)
        {
            try
            {
                decryptedItems.Add(await DecryptItemAsync(item, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                LogFailedToDecryptMemoryItem(ex);
            }
        }

        return decryptedItems;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(key, cancellationToken);

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
        => _inner.ClearAsync(cancellationToken);

    /// <summary>
    /// Stores a memory item with an associated embedding vector, encrypting the content
    /// before delegating to the inner provider. The embedding itself stays clear so the
    /// inner store can index it for similarity search.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="item">The memory item to store.</param>
    /// <param name="embedding">The embedding vector to associate with the item.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task StoreWithEmbeddingAsync(
        string key,
        MemoryItem item,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(embedding);
        return StoreWithEmbeddingCoreAsync();

        async Task StoreWithEmbeddingCoreAsync()
        {
            if (!_encryption.IsEnabled)
            {
                await _inner.StoreWithEmbeddingAsync(key, item, embedding, cancellationToken).ConfigureAwait(false);
                return;
            }

            var encryptedItem = await EncryptItemAsync(item, cancellationToken).ConfigureAwait(false);

            await _inner.StoreWithEmbeddingAsync(key, encryptedItem, embedding, cancellationToken).ConfigureAwait(false);
            LogStoredEncryptedMemoryItemWith(key);
        }
    }

    /// <summary>
    /// Delegates similarity search to the inner provider (embeddings are stored in clear,
    /// so vector scoring is unaffected by encryption) and decrypts the content of each
    /// scored result. Items that fail to decrypt are skipped with a warning.
    /// </summary>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results.</param>
    /// <param name="minScore">Minimum similarity score threshold.</param>
    /// <param name="filter">Optional metadata filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The scored results with decrypted contents.</returns>
    public async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var results = await _inner.SearchSimilarAsync(queryEmbedding, topK, minScore, filter, cancellationToken).ConfigureAwait(false);
        return await DecryptScoredResultsAsync(results, cancellationToken).ConfigureAwait(false);
    }

    // ── Optional capability forwarding (RAG-02/C4) ────────────────────────
    // See the class remarks: capabilities are statically implemented, effectively
    // advertised via IMemoryCapabilityProbe, and forwarded only when the inner
    // provider possesses them (NotSupportedException otherwise).

    /// <inheritdoc />
    public bool HasCapability<TCapability>() where TCapability : class
        => _inner.TryGetCapability<TCapability>(out _);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="IScoredVectorSearch"/>.</exception>
    public async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var search = RequireCapability<IScoredVectorSearch>();
        var results = await search.SearchSimilarWithScoresAsync(embedding, topK, minScore, filter, cancellationToken).ConfigureAwait(false);
        return await DecryptScoredResultsAsync(results, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="IHybridSearchCapable"/>.</exception>
    public async Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var hybrid = RequireCapability<IHybridSearchCapable>();
        var results = await hybrid.HybridSearchAsync(query, embedding, topK, filter, cancellationToken).ConfigureAwait(false);
        return await DecryptScoredResultsAsync(results, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Collection-scoped hybrid search (RAG-04/C2): forwarded so the interface's default
    /// body (<see cref="NotSupportedException"/>) never masks an inner provider that does
    /// implement it (e.g. LanceDB per-collection tables). Results are decrypted on the
    /// way out, preserving fused scores and keys.
    /// </remarks>
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="IHybridSearchCapable"/> (or not its collection-scoped member).</exception>
    public async Task<IReadOnlyList<ScoredMemoryItem>> HybridSearchAsync(
        string collection,
        string query,
        ReadOnlyMemory<float> embedding,
        int topK,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var hybrid = RequireCapability<IHybridSearchCapable>();
        var results = await hybrid.HybridSearchAsync(collection, query, embedding, topK, filter, cancellationToken).ConfigureAwait(false);
        return await DecryptScoredResultsAsync(results, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When encryption is enabled, each entry's content is encrypted before delegation;
    /// embeddings (both the explicit entry embedding and the one carried by the item)
    /// stay clear so the inner store can index them.
    /// </remarks>
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="IBatchUpsert"/>.</exception>
    public async Task UpsertBatchAsync(
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var batch = RequireCapability<IBatchUpsert>();
        ArgumentNullException.ThrowIfNull(entries);

        if (!_encryption.IsEnabled)
        {
            await batch.UpsertBatchAsync(entries, cancellationToken).ConfigureAwait(false);
            return;
        }

        var encryptedEntries = new List<MemoryUpsertEntry>(entries.Count);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Item);

            var encryptedItem = await EncryptItemAsync(entry.Item, cancellationToken).ConfigureAwait(false);

            encryptedEntries.Add(entry with { Item = encryptedItem });
        }

        await batch.UpsertBatchAsync(encryptedEntries, cancellationToken).ConfigureAwait(false);
        LogBatchUpsertedEncrypted(encryptedEntries.Count);
    }

    // ── ICollectionAwareMemory forwarding (RAG-03/C2) ─────────────────────
    // Same conditional-forwarding pattern: encryption semantics are unchanged
    // (contents encrypted before delegation, embeddings clear, results decrypted).

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="ICollectionAwareMemory"/>.</exception>
    public async Task StoreWithEmbeddingAsync(
        string collection,
        string key,
        MemoryItem item,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken = default)
    {
        var scoped = RequireCapability<ICollectionAwareMemory>();
        ArgumentNullException.ThrowIfNull(item);

        if (!_encryption.IsEnabled)
        {
            await scoped.StoreWithEmbeddingAsync(collection, key, item, embedding, cancellationToken).ConfigureAwait(false);
            return;
        }

        var encryptedItem = await EncryptItemAsync(item, cancellationToken).ConfigureAwait(false);
        await scoped.StoreWithEmbeddingAsync(collection, key, encryptedItem, embedding, cancellationToken).ConfigureAwait(false);
        LogStoredEncryptedMemoryItemWith(key);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When encryption is enabled, each entry's content is encrypted before delegation;
    /// embeddings (both the explicit entry embedding and the one carried by the item)
    /// stay clear so the inner store can index them.
    /// </remarks>
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="ICollectionAwareMemory"/>.</exception>
    public async Task UpsertBatchAsync(
        string collection,
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var scoped = RequireCapability<ICollectionAwareMemory>();
        ArgumentNullException.ThrowIfNull(entries);

        if (!_encryption.IsEnabled)
        {
            await scoped.UpsertBatchAsync(collection, entries, cancellationToken).ConfigureAwait(false);
            return;
        }

        var encryptedEntries = new List<MemoryUpsertEntry>(entries.Count);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.Item);
            var encryptedItem = await EncryptItemAsync(entry.Item, cancellationToken).ConfigureAwait(false);
            encryptedEntries.Add(entry with { Item = encryptedItem });
        }

        await scoped.UpsertBatchAsync(collection, encryptedEntries, cancellationToken).ConfigureAwait(false);
        LogBatchUpsertedEncrypted(encryptedEntries.Count);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="ICollectionAwareMemory"/>.</exception>
    public async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarWithScoresAsync(
        string collection,
        ReadOnlyMemory<float> embedding,
        int topK,
        float minScore,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var scoped = RequireCapability<ICollectionAwareMemory>();
        var results = await scoped
            .SearchSimilarWithScoresAsync(collection, embedding, topK, minScore, filter, cancellationToken)
            .ConfigureAwait(false);
        return await DecryptScoredResultsAsync(results, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="ICollectionAwareMemory"/>.</exception>
    public Task DeleteByFilterAsync(
        string collection,
        MemoryFilter filter,
        CancellationToken cancellationToken = default)
        => RequireCapability<ICollectionAwareMemory>().DeleteByFilterAsync(collection, filter, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The wrapped provider does not implement <see cref="ICollectionAwareMemory"/>.</exception>
    public Task DropCollectionAsync(string collection, CancellationToken cancellationToken = default)
        => RequireCapability<ICollectionAwareMemory>().DropCollectionAsync(collection, cancellationToken);

    /// <summary>
    /// Returns a copy of <paramref name="item"/> whose content is encrypted. Content is the
    /// only thing that changes: identifier, embedding, importance and the whole metadata
    /// record travel through untouched.
    /// <para>
    /// Every path goes through this and its decrypting twin. Four of them used to rebuild the
    /// item inline with five of the seven fields, so <c>Metadata.CustomProperties</c> and
    /// <c>CreatedBy</c> were dropped on the ordinary store/get/search — against a class whose
    /// own contract is "metadata remains in clear text".
    /// </para>
    /// <para>
    /// Naming the fields was still the wrong shape, because a rebuild via
    /// <c>MemoryItem.Create</c> mints a fresh <c>MemoryItemId</c> and stamps
    /// <c>CreatedAt = UtcNow</c>, <c>AccessCount = 0</c>, <c>LastAccessedAt = null</c> — fields
    /// no caller can pass. <c>SqliteMemoryRecord.ToMemoryItem</c> goes out of its way to
    /// restore exactly those from storage; wrapping that provider in this decorator threw the
    /// work away again, so an encrypted long-term memory reported the moment it was decrypted
    /// as its creation time and never accumulated an access count. Ageing and
    /// recency-ordering read wrong values, and only with encryption switched on.
    /// <c>Restore</c> is the factory for "same item, other content" and is what this uses.
    /// </para>
    /// </summary>
    private async Task<MemoryItem> EncryptItemAsync(MemoryItem item, CancellationToken cancellationToken) =>
        Rebuild(item, await _encryption.EncryptStringAsync(item.Content, cancellationToken).ConfigureAwait(false));

    /// <summary>The decrypting twin of <see cref="EncryptItemAsync"/>.</summary>
    private async Task<MemoryItem> DecryptItemAsync(MemoryItem item, CancellationToken cancellationToken) =>
        Rebuild(item, await _encryption.DecryptStringAsync(item.Content, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Same item, other content. Nothing is enumerated, so nothing can be forgotten: the
    /// identity and the metadata record are carried over whole.
    /// </summary>
    private static MemoryItem Rebuild(MemoryItem item, string content) =>
        MemoryItem.Restore(item.Id, content, item.Embedding, item.Importance, item.Metadata);

    /// <summary>
    /// Resolves the requested capability on the wrapped provider or throws a
    /// <see cref="NotSupportedException"/> with an actionable message.
    /// </summary>
    private TCapability RequireCapability<TCapability>() where TCapability : class
    {
        if (_inner.TryGetCapability<TCapability>(out var capability))
            return capability;

        throw new NotSupportedException(
            $"The wrapped memory provider '{_inner.GetType().Name}' does not implement {typeof(TCapability).Name}. " +
            $"Check availability with IMemoryProvider.TryGetCapability<{typeof(TCapability).Name}>() before calling this member.");
    }

    /// <summary>
    /// Decrypts the content of each scored result while preserving score and storage key.
    /// Items that fail to decrypt are skipped with a warning. No-op when encryption is
    /// disabled or the result set is empty.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-item decrypt fault barrier: an item whose content fails to decrypt is logged and skipped so the remaining scored results are still returned.")]
    private async Task<IReadOnlyList<ScoredMemoryItem>> DecryptScoredResultsAsync(
        IReadOnlyList<ScoredMemoryItem> results,
        CancellationToken cancellationToken)
    {
        if (!_encryption.IsEnabled || results.Count == 0)
            return results;

        var decrypted = new List<ScoredMemoryItem>(results.Count);
        foreach (var scored in results)
        {
            try
            {
                var decryptedItem = await DecryptItemAsync(scored.Item, cancellationToken).ConfigureAwait(false);
                decrypted.Add(scored with { Item = decryptedItem });
            }
            catch (Exception ex)
            {
                LogFailedToDecryptMemoryItem(ex);
            }
        }

        return decrypted;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Stored encrypted memory item with key: {Key}")]
    private partial void LogStoredEncryptedMemoryItemWith(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Retrieved and decrypted memory item with key: {Key}")]
    private partial void LogRetrievedAndDecryptedMemoryItem(object key);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to decrypt memory item during search, skipping.")]
    private partial void LogFailedToDecryptMemoryItem(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Batch-upserted {Count} encrypted memory items")]
    private partial void LogBatchUpsertedEncrypted(int count);

}
