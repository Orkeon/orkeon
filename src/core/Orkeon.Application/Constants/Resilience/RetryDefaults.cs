namespace Orkeon.Application.Constants.Resilience;

/// <summary>
/// Default values for retry, backoff, and circuit-breaker configuration.
/// Centralises magic numbers used in <see cref="Orkeon.Application.Services.ErrorHandling.PatternMatchingErrorHandler"/>
/// and related retry infrastructure.
/// </summary>
public static class RetryDefaults
{
    /// <summary>Base delay for HTTP request retries (seconds).</summary>
    public const int HttpRetryDelaySeconds = 2;

    /// <summary>Base delay for timeout retries (seconds).</summary>
    public const int TimeoutRetryDelaySeconds = 5;

    /// <summary>Backoff delay when a rate-limit error is detected (minutes).</summary>
    public const int RateLimitDelayMinutes = 1;

    /// <summary>Backoff delay when a quota-exceeded error is detected (minutes).</summary>
    public const int QuotaExceededDelayMinutes = 5;

    /// <summary>Default initial delay before the first retry (seconds).</summary>
    public const int DefaultInitialDelaySeconds = 3;

    /// <summary>
    /// Exponential backoff multipliers indexed by attempt count (1-based).
    /// Index 0 = attempt 1 (no scaling), subsequent entries double the delay.
    /// </summary>
    public static readonly double[] ExponentialBackoffMultipliers = [1.0, 2.0, 4.0, 8.0, 16.0];

    /// <summary>Maximum random jitter added to retry delays (milliseconds).</summary>
    public const int MaxJitterMilliseconds = 1000;

    /// <summary>How long the circuit breaker stays open before allowing a probe request (minutes).</summary>
    public const int DefaultTrippedDelayMinutes = 5;

    /// <summary>Default exponential backoff multiplier applied between successive retry attempts.</summary>
    public const double DefaultBackoffMultiplier = 2.0;
}
