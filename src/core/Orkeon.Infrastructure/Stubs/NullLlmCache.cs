using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Infrastructure.Caching;
using System.Security.Cryptography;
using System.Text;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// No-op implementation of <see cref="ILlmCache"/> that never caches.
/// Logs a warning on first use to indicate no cache is configured.
/// </summary>
public sealed partial class NullLlmCache : ILlmCache
{
    private readonly ILogger<NullLlmCache> _logger;
    private int _warnedOnce;
    private long _misses;

    /// <summary>Initializes a new instance of <see cref="NullLlmCache"/>.</summary>
    public NullLlmCache(ILogger<NullLlmCache> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogNullCacheFallback();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using NullLlmCache — LLM responses are not cached. Register a real ILlmCache (e.g. Redis-backed) for production.")]
    private partial void LogNullCacheFallback();

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        Interlocked.Increment(ref _misses);
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        // No-op: discard the value
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CacheStatistics(
            TotalHits: 0,
            TotalMisses: Interlocked.Read(ref _misses),
            HitRate: 0.0,
            AverageLatency: TimeSpan.Zero,
            EstimatedTokensSaved: 0,
            CacheSizeBytes: 0,
            LastResetTime: DateTime.UtcNow));
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _misses, 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GenerateCacheKey(string model, string prompt, object? parameters = null)
    {
        var input = $"{model}:{prompt}:{parameters}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
