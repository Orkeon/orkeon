using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Constants.Orchestration;
using static Orkeon.Infrastructure.Constants.Security.RateLimitDefaults;
using Orkeon.Domain.Constants.Resilience;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Rate limiter for LLM API calls using sliding window rate limiters.
/// Supports global, per-provider, and per-agent rate limits.
/// </summary>
public sealed partial class LlmRateLimiter : ILlmRateLimiter, IDisposable
{
    private readonly RateLimiter _globalLimiter;
    private readonly ConcurrencyLimiter? _concurrencyLimiter;
    private readonly ConcurrentDictionary<string, RateLimiter> _providerLimiters = new();
    private readonly ConcurrentDictionary<string, RateLimiter> _agentLimiters = new();
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
    public async Task<RateLimitAcquisition> AcquireAsync(string provider, string agentRole, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var leases = new List<IDisposable>(4);

        // 1. Concurrency gate — blocks until a slot is free (most important for local LLMs)
        if (_concurrencyLimiter != null)
        {
            var concurrencyLease = await _concurrencyLimiter.AcquireAsync(1, ct).ConfigureAwait(false);
            if (!concurrencyLease.IsAcquired)
            {
                LogConcurrencyLimitExceeded(provider, agentRole, _options.MaxConcurrentRequests);
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
            LogGlobalLlmRateLimitExceeded(provider, agentRole);
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
            LogProviderRateLimitExceededFor(provider, agentRole);
            return RateLimitAcquisition.Denied(
                $"Provider '{provider}' rate limit exceeded",
                providerLease.GetRetryAfter() ?? ResilienceDefaults.DefaultRetryInitialDelay);
        }
        leases.Add(providerLease);

        // 4. Per-agent rate limit
        var agentLimiter = _agentLimiters.GetOrAdd(agentRole, _ => CreateLimiter(_options.AgentRequestsPerMinute));
        var agentLease = await agentLimiter.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!agentLease.IsAcquired)
        {
            DisposeAll(leases);
            LogAgentRateLimitExceededFor(provider, agentRole);
            return RateLimitAcquisition.Denied(
                $"Agent '{agentRole}' rate limit exceeded",
                agentLease.GetRetryAfter() ?? ResilienceDefaults.DefaultRetryInitialDelay);
        }
        leases.Add(agentLease);

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
        foreach (var limiter in _agentLimiters.Values)
            limiter.Dispose();

        _providerLimiters.Clear();
        _agentLimiters.Clear();
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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "LLM concurrency limit exceeded (max {MaxConcurrent}) for provider={Provider}, agent={Agent}")]
    private partial void LogConcurrencyLimitExceeded(object provider, object agent, int maxConcurrent);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Global LLM rate limit exceeded for provider={Provider}, agent={Agent}")]
    private partial void LogGlobalLlmRateLimitExceeded(object provider, object agent);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Provider rate limit exceeded for provider={Provider}, agent={Agent}")]
    private partial void LogProviderRateLimitExceededFor(object provider, object agent);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Agent rate limit exceeded for provider={Provider}, agent={Agent}")]
    private partial void LogAgentRateLimitExceededFor(object provider, object agent);
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
