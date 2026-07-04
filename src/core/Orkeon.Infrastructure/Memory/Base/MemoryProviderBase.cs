using Microsoft.Extensions.Logging;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Memory.Base;

/// <summary>
/// Simplified base class for memory providers.
/// Focuses only on data access concerns - no business logic.
/// Business logic (similarity calculations, validation) moved to domain services.
/// </summary>
public abstract partial class MemoryProviderBase : Orkeon.Domain.Memory.IMemoryProvider
{
    /// <summary>
    /// The logger for this memory provider. Kept as a <see langword="protected"/> field (rather than a
    /// property) because the <c>[LoggerMessage]</c> source generator used by derived providers'
    /// partial logging classes resolves the inherited <see cref="ILogger"/> instance via a field;
    /// exposing it as a property breaks generation (SYSLIB1019).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Must remain a protected field: the [LoggerMessage] source generator in derived partial classes resolves the inherited ILogger via a field; a property breaks generation (SYSLIB1019).")]
    protected readonly ILogger Logger;

    /// <summary>The runtime memory provider configuration, set during initialization.</summary>
    protected MemoryProviderConfig? Configuration { get; set; }

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>Initializes a new instance of <see cref="MemoryProviderBase"/>.</summary>
    /// <param name="logger">Optional logger.</param>
    protected MemoryProviderBase(ILogger? logger = null)
    {
        Logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>
    /// Initializes the memory provider.
    /// Pure configuration setup - no business logic.
    /// </summary>
    public virtual Task InitializeAsync(MemoryProviderConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        Configuration = config;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stores a memory item.
    /// Pure data storage - no business logic.
    /// </summary>
    public abstract Task StoreAsync(
        string key,
        MemoryItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a memory item by key.
    /// Pure data retrieval - no business logic.
    /// </summary>
    public abstract Task<MemoryItem?> GetAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing memory item.
    /// Pure data update - no business logic.
    /// </summary>
    public abstract Task<bool> UpdateAsync(
        string key,
        MemoryItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a memory item by key.
    /// Pure data deletion - no business logic.
    /// </summary>
    public abstract Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for memories using embeddings.
    /// Pure data query - similarity calculation done in domain.
    /// </summary>
    public abstract Task<IEnumerable<MemoryItem>> SearchAsync(
        string query,
        int limit = MemoryDefaults.DefaultSearchLimit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all memories.
    /// Pure data operation - no business logic.
    /// </summary>
    public abstract Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of memories.
    /// Pure data query - no business logic.
    /// </summary>
    public abstract Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all memory keys with pagination.
    /// Pure data query - no business logic.
    /// </summary>
    public abstract Task<List<string>> ListKeysAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for memory items similar to the provided query embedding vector.
    /// Returns items scored by similarity (best first), filtered by minimum score threshold.
    /// </summary>
    /// <remarks>
    /// Declared <see langword="abstract"/> here — instead of inheriting the empty
    /// default interface method of <see cref="IMemoryProvider"/> — to close the
    /// default-interface-method trap (MAT-017 / R10.1): C# freezes interface mapping at the
    /// class that lists the interface (this one) and does not re-map members declared by
    /// derived classes. Without this member, a derived provider's <c>SearchSimilarAsync</c>
    /// would never be reached through <see cref="IMemoryProvider"/> and semantic recall
    /// would silently return zero results. The abstract declaration both forces every
    /// provider to supply a real vector-search implementation and routes interface calls
    /// to it via virtual dispatch.
    /// </remarks>
    /// <param name="queryEmbedding">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="minScore">Minimum similarity score threshold.</param>
    /// <param name="filter">Optional metadata filter (e.g. <c>source</c>, <c>tag</c>/<c>tags</c>, custom properties).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching items scored by similarity, best first.</returns>
    public abstract Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK = 10,
        float minScore = 0.0f,
        Dictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates configuration parameters.
    /// Pure validation - no business rules.
    /// </summary>
    protected virtual void ValidateConfiguration()
    {
        if (Configuration == null)
            throw new InvalidOperationException("Provider not initialized. Call InitializeAsync first.");
    }

    /// <summary>
    /// Creates a timestamped key for memory storage.
    /// Pure utility method - no business logic.
    /// </summary>
    protected static string CreateTimestampedKey(string? prefix = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var uniqueId = Guid.NewGuid().ToString("N")[..8];

        return string.IsNullOrEmpty(prefix)
            ? $"{timestamp}_{uniqueId}"
            : $"{prefix}_{timestamp}_{uniqueId}";
    }

    /// <summary>
    /// Validates input parameters.
    /// Pure input validation - no business rules.
    /// </summary>
    protected static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }

    /// <summary>
    /// Validates memory item.
    /// Pure input validation - no business rules.
    /// </summary>
    protected static void ValidateMemoryItem(MemoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(item.Content))
            throw new ArgumentException("Memory item content cannot be null or whitespace.", nameof(item));
    }

    /// <summary>
    /// Safely handles exceptions and logs them.
    /// Pure exception handling - no business logic.
    /// </summary>
    protected void LogException(Exception ex, string operation, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (key != null)
        {
            LogOperationErrorWithKey(ex, operation, key, ex.Message);
        }
        else
        {
            LogOperationError(ex, operation, ex.Message);
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error in {Operation} for key {Key}: {Message}")]
    private partial void LogOperationErrorWithKey(Exception ex, string operation, string key, string message);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error in {Operation}: {Message}")]
    private partial void LogOperationError(Exception ex, string operation, string message);
}
