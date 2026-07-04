using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Generic decorator that encrypts memory content before storing and decrypts after retrieval.
/// Wraps any <see cref="IMemoryProvider"/> implementation (Redis, SQLite, in-memory, etc.).
/// Metadata and embeddings are NOT encrypted (needed for search/indexing): vector search
/// (<see cref="StoreWithEmbeddingAsync"/>/<see cref="SearchSimilarAsync"/>) is delegated to
/// the inner provider and result contents are decrypted on the way out.
/// </summary>
public sealed partial class EncryptedMemoryProviderDecorator : IMemoryProvider
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

            var encryptedContent = await _encryption.EncryptStringAsync(item.Content, cancellationToken).ConfigureAwait(false);
            var encryptedItem = MemoryItem.Create(
                encryptedContent,
                item.Embedding,
                item.Importance,
                item.Source,
                item.Tags);

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

        var decryptedContent = await _encryption.DecryptStringAsync(item.Content, cancellationToken).ConfigureAwait(false);
        var decryptedItem = MemoryItem.Create(
            decryptedContent,
            item.Embedding,
            item.Importance,
            item.Source,
            item.Tags);

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
                var decryptedContent = await _encryption.DecryptStringAsync(item.Content, cancellationToken).ConfigureAwait(false);
                decryptedItems.Add(MemoryItem.Create(
                    decryptedContent,
                    item.Embedding,
                    item.Importance,
                    item.Source,
                    item.Tags));
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

            var encryptedContent = await _encryption.EncryptStringAsync(item.Content, cancellationToken).ConfigureAwait(false);
            var encryptedItem = MemoryItem.Create(
                encryptedContent,
                item.Embedding,
                item.Importance,
                item.Source,
                item.Tags);

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-item decrypt fault barrier: an item whose content fails to decrypt is logged and skipped so the remaining scored results are still returned.")]
    public async Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        var results = await _inner.SearchSimilarAsync(queryEmbedding, topK, minScore, filter, cancellationToken).ConfigureAwait(false);
        if (!_encryption.IsEnabled || results.Count == 0)
            return results;

        var decrypted = new List<ScoredMemoryItem>(results.Count);
        foreach (var scored in results)
        {
            try
            {
                var decryptedContent = await _encryption.DecryptStringAsync(scored.Item.Content, cancellationToken).ConfigureAwait(false);
                var decryptedItem = MemoryItem.Create(
                    decryptedContent,
                    scored.Item.Embedding,
                    scored.Item.Importance,
                    scored.Item.Source,
                    scored.Item.Tags);
                decrypted.Add(new ScoredMemoryItem(decryptedItem, scored.Score));
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

}
