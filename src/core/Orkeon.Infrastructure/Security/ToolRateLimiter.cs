using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using static Orkeon.Infrastructure.Constants.Security.RateLimitDefaults;
using Orkeon.Domain.Constants.Resilience;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Rate limiter for tool invocations using sliding window rate limiters.
/// Supports global and per-tool rate limits with configurable per-tool overrides.
/// </summary>
public sealed partial class ToolRateLimiter : IToolRateLimiter, IDisposable
{
    private readonly RateLimiter _globalLimiter;
    private readonly ConcurrentDictionary<string, RateLimiter> _toolLimiters = new();
    private readonly ToolRateLimitOptions _options;
    private readonly ILogger<ToolRateLimiter> _logger;
    private bool _disposed;

    /// <summary>Initializes a new instance of <see cref="ToolRateLimiter"/>.</summary>
    /// <param name="options">The tool rate limiting options.</param>
    /// <param name="logger">The logger.</param>
    public ToolRateLimiter(IOptions<ToolRateLimitOptions> options, ILogger<ToolRateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        _globalLimiter = CreateLimiter(_options.GlobalToolRequestsPerMinute);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "On the success path ownership of the CompositeDisposable wrapping the acquired global and tool leases is transferred to the returned RateLimitAcquisition, which the caller disposes to release the permits; the tool-denied path disposes the already-acquired global lease before returning.")]
    public async Task<RateLimitAcquisition> AcquireAsync(string toolName, string agentRole, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var globalLease = await _globalLimiter.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!globalLease.IsAcquired)
        {
            LogGlobalToolRateLimitExceeded(toolName, agentRole);
            return RateLimitAcquisition.Denied(
                "Global tool rate limit exceeded",
                globalLease.GetRetryAfter() ?? ResilienceDefaults.DefaultRetryInitialDelay);
        }

        var toolLimit = _options.ToolSpecificLimits.GetValueOrDefault(toolName, _options.DefaultToolRequestsPerMinute);
        var toolLimiter = _toolLimiters.GetOrAdd(toolName, _ => CreateLimiter(toolLimit));

        var toolLease = await toolLimiter.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!toolLease.IsAcquired)
        {
            globalLease.Dispose();
            LogToolRateLimitExceededFor(toolName, agentRole);
            return RateLimitAcquisition.Denied(
                $"Tool '{toolName}' rate limit exceeded",
                toolLease.GetRetryAfter() ?? ResilienceDefaults.DefaultRetryInitialDelay);
        }

        var compositeLease = new CompositeDisposable(globalLease, toolLease);
        return RateLimitAcquisition.Acquired(compositeLease);
    }

    private static SlidingWindowRateLimiter CreateLimiter(int permitLimit)
    {
        return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = WindowDuration,
            SegmentsPerWindow = SlidingWindowSegments,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0 // No queuing for tools - fail fast
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_disposed) return;
        _disposed = true;

        _globalLimiter.Dispose();
        foreach (var limiter in _toolLimiters.Values)
            limiter.Dispose();

        _toolLimiters.Clear();
    }

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

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Global tool rate limit exceeded for tool={Tool}, agent={Agent}")]
    private partial void LogGlobalToolRateLimitExceeded(object tool, object agent);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Tool rate limit exceeded for tool={Tool}, agent={Agent}")]
    private partial void LogToolRateLimitExceededFor(object tool, object agent);

}
