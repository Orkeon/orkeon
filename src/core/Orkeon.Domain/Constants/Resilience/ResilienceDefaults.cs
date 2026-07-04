namespace Orkeon.Domain.Constants.Resilience;

/// <summary>
/// Default values for retry, circuit-breaker, and rate-limiting configuration.
/// Centralises resilience-related magic values used across the Orkeon platform.
/// </summary>
public static class ResilienceDefaults
{
    /// <summary>Initial delay before the first retry attempt (1 s).</summary>
    public static readonly TimeSpan DefaultRetryInitialDelay = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound on retry delay / circuit-breaker break duration (30 s).</summary>
    public static readonly TimeSpan DefaultRetryMaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>Default sliding window for rate limiters (1 min).</summary>
    public static readonly TimeSpan DefaultRateLimiterWindow = TimeSpan.FromMinutes(1);

    /// <summary>Default rate limit reset strategy.</summary>
    public const string DefaultResetStrategy = "sliding_window";
}
