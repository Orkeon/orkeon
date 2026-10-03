using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Constants.Orchestration;
using static Orkeon.Infrastructure.Constants.Security.RateLimitDefaults;
using Orkeon.Domain.Constants.Resilience;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// The host's limiter of model calls (<c>RateLimiting</c>): sliding windows of a minute for the
/// process (<see cref="RateLimitingOptions.GlobalRequestsPerMinute"/>) and for each provider
/// (<see cref="RateLimitingOptions.ProviderRequestsPerMinute"/>), and the in-flight bound
/// (<see cref="RateLimitingOptions.MaxConcurrentRequests"/>), each holding up to
/// <see cref="RateLimitingOptions.QueueLimit"/> requests before it refuses. Every model call takes one
/// lease at the entrance of its provider (<c>RateLimitedLlmProvider</c>). The per-agent cap is not
/// here: it bounds each agent's own window, where a request waits its turn (GAP-38).
/// </summary>
public sealed partial class LlmRateLimiter : ILlmRateLimiter, IDisposable
{
    private readonly RateLimiter _globalLimiter;
    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly ConcurrentDictionary<string, RateLimiter> _providerLimiters = new();
    private readonly RateLimitingOptions _options;
    private readonly ILogger<LlmRateLimiter> _logger;
    private bool _disposed;

    /// <summary>Initializes a new instance of <see cref="LlmRateLimiter"/>.</summary>
    /// <param name="options">The rate limiting options.</param>
    /// <param name="logger">The logger.</param>
    public LlmRateLimiter(IOptions<RateLimitingOptions> options, ILogger<LlmRateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        _globalLimiter = CreateLimiter(_options.GlobalRequestsPerMinute);

        if (_options.MaxConcurrentRequests > 0)
        {
            _concurrencyLimiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = _options.MaxConcurrentRequests,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = _options.QueueLimit
            });
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "On the success path ownership of the CompositeDisposable (and the acquired leases it wraps) is transferred to the returned RateLimitAcquisition, which the caller disposes to release the rate-limit permits; on every failure path the leases are disposed via DisposeAll before returning.")]
    public async Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var leases = new List<IDisposable>(3);

        // 1. Concurrency gate — blocks until a slot is free (most important for local LLMs)
        if (_concurrencyLimiter != null)
        {
            var concurrencyLease = await _concurrencyLimiter.AcquireAsync(1, ct).ConfigureAwait(false);
            if (!concurrencyLease.IsAcquired)
            {
                LogConcurrencyLimitExceeded(provider, _options.MaxConcurrentRequests);
                return RateLimitAcquisition.Denied(
                    $"Concurrency limit exceeded (max {_options.MaxConcurrentRequests} in-flight)",
                    concurrencyLease.GetRetryAfter() ?? TimeSpan.FromSeconds(OrchestrationDefaults.ConcurrencyRetryDelaySeconds));
            }
            leases.Add(concurrencyLease);
        }

        // 2. Global rate limit (requests/minute)
        var globalLease = await _globalLimiter.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!globalLease.IsAcquired)
        {
            DisposeAll(leases);
            LogGlobalLlmRateLimitExceeded(provider);
            return RateLimitAcquisition.Denied(
                "Global LLM rate limit exceeded",
                globalLease.GetRetryAfter() ?? ResilienceDefaults.DefaultRetryInitialDelay);
        }
        leases.Add(globalLease);

        // 3. Per-provider rate limit
        var providerLimiter = _providerLimiters.GetOrAdd(provider, _ => CreateLimiter(_options.ProviderRequestsPerMinute));
        var providerLease = await providerLimiter.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!providerLease.IsAcquired)
        {
            DisposeAll(leases);
            LogProviderRateLimitExceededFor(provider);
            return RateLimitAcquisition.Denied(
                $"Provider '{provider}' rate limit exceeded",
                providerLease.GetRetryAfter() ?? ResilienceDefaults.DefaultRetryInitialDelay);
        }
        leases.Add(providerLease);

        return RateLimitAcquisition.Acquired(new CompositeDisposable([.. leases]));
    }

    private static void DisposeAll(List<IDisposable> leases)
    {
        foreach (var lease in leases)
            lease.Dispose();
    }

    private SlidingWindowRateLimiter CreateLimiter(int permitLimit)
    {
        return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = WindowDuration,
            SegmentsPerWindow = SlidingWindowSegments,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = _options.QueueLimit
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposed) return;
        _disposed = true;

        _concurrencyLimiter?.Dispose();
        _globalLimiter.Dispose();
        foreach (var limiter in _providerLimiters.Values)
            limiter.Dispose();

        _providerLimiters.Clear();
    }

    /// <summary>
    /// Helper that disposes multiple leases when disposed.
    /// </summary>
    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;

        public CompositeDisposable(params IDisposable[] disposables)
        {
            _disposables = disposables;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            foreach (var d in _disposables)
                d.Dispose();
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "LLM concurrency limit exceeded (max {MaxConcurrent}) for provider={Provider}")]
    private partial void LogConcurrencyLimitExceeded(object provider, int maxConcurrent);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Global LLM rate limit exceeded for provider={Provider}")]
    private partial void LogGlobalLlmRateLimitExceeded(object provider);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Provider rate limit exceeded for provider={Provider}")]
    private partial void LogProviderRateLimitExceededFor(object provider);
}

internal static class RateLimitLeaseExtensions
{
    public static TimeSpan? GetRetryAfter(this RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            return retryAfter;
        return null;
    }
}
