
namespace Orkeon.Application.Interfaces.Infrastructure.Caching;

/// <summary>
/// Interface for LLM response caching to reduce costs and improve performance.
/// </summary>
public interface ILlmCache
{
    /// <summary>
    /// Retrieves a cached LLM response.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The cached response or null if not found</returns>
    System.Threading.Tasks.Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an LLM response in the cache.
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="value">The response to cache</param>
    /// <param name="expiration">Optional expiration time (defaults to 1 hour)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    System.Threading.Tasks.Task SetAsync(string key, string value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics for monitoring and optimization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current cache statistics</returns>
    System.Threading.Tasks.Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a cache key from LLM parameters.
    /// </summary>
    /// <param name="model">The LLM model name</param>
    /// <param name="prompt">The prompt text</param>
    /// <param name="parameters">Additional parameters (temperature, max_tokens, etc.)</param>
    /// <returns>A deterministic cache key</returns>
    string GenerateCacheKey(string model, string prompt, object? parameters = null);
}

/// <summary>
/// Cache statistics for monitoring and optimization.
/// </summary>
public record CacheStatistics(
    long TotalHits,
    long TotalMisses,
    double HitRate,
    TimeSpan AverageLatency,
    long EstimatedTokensSaved,
    long CacheSizeBytes,
    DateTime LastResetTime)
{
    /// <summary>
    /// Creates empty statistics.
    /// </summary>
    public static CacheStatistics Empty => new(0, 0, 0.0, TimeSpan.Zero, 0, 0, DateTime.UtcNow);
}
