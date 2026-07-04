namespace Orkeon.Infrastructure.Constants.Orchestration;

/// <summary>
/// Default timeout and interval values for orchestration and infrastructure services.
/// Centralises miscellaneous timeout magic values that did not fit other categories.
/// </summary>
public static class OrchestrationDefaults
{
    /// <summary>Default cache TTL for secrets retrieved from vault providers (5 min).</summary>
    public static readonly TimeSpan SecretCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Default clock skew tolerance for JWT/OIDC token validation (5 min).</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Default interval between execution-state cleanup passes (5 min).</summary>
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>Default expiry duration for crew execution state entries (24 h).</summary>
    public static readonly TimeSpan ExecutionExpiryHours = TimeSpan.FromHours(24);

    /// <summary>Delay in seconds before retrying after a concurrency limit is hit.</summary>
    public const int ConcurrencyRetryDelaySeconds = 2;
}
