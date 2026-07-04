namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Rate limiter for LLM API calls, supporting global, per-provider, and per-agent limits.
/// </summary>
public interface ILlmRateLimiter
{
    /// <summary>
    /// Attempts to acquire a rate limit lease for the given provider and agent.
    /// </summary>
    System.Threading.Tasks.Task<RateLimitAcquisition> AcquireAsync(string provider, string agentRole, CancellationToken ct = default);
}

/// <summary>
/// Result of a rate limit acquisition attempt.
/// </summary>
public record RateLimitAcquisition(bool IsAcquired, IDisposable? Lease, string? DenialReason, TimeSpan? RetryAfter)
{
    /// <summary>
    /// Creates a successful acquisition with the given lease.
    /// </summary>
    public static RateLimitAcquisition Acquired(IDisposable lease) => new(true, lease, null, null);

    /// <summary>
    /// Creates a denied acquisition with the reason and retry-after duration.
    /// </summary>
    public static RateLimitAcquisition Denied(string reason, TimeSpan retryAfter) => new(false, null, reason, retryAfter);
}
