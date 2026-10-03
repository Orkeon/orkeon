namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// The host's limiter of model calls: what is common to the process — a global cap, a cap per
/// provider, the concurrency. Every model call takes one lease at the entrance of its provider
/// (GAP-38); the per-agent cap belongs to the agent's own window, not here.
/// </summary>
public interface ILlmRateLimiter
{
    /// <summary>
    /// Attempts to acquire a lease for one call to <paramref name="provider"/>.
    /// </summary>
    /// <param name="provider">The provider's name.</param>
    /// <param name="ct">Cancels the wait in the limiter's queue.</param>
    System.Threading.Tasks.Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default);
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
