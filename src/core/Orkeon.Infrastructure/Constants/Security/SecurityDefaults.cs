namespace Orkeon.Infrastructure.Constants.Security;

/// <summary>
/// Default values for security configuration.
/// Centralises magic numbers and strings used across the security infrastructure.
/// </summary>
public static class SecurityDefaults
{
    /// <summary>Maximum allowed file size in bytes (50 MB).</summary>
    public const long MaxFileSizeBytes = 50 * 1024 * 1024;

    /// <summary>Maximum allowed length for a session identifier.</summary>
    public const int MaxSessionIdLength = 128;

    /// <summary>Number of days to retain audit logs.</summary>
    public const int AuditRetentionDays = 90;

    /// <summary>Ports blocked from network access (common service ports).</summary>
    public static readonly IReadOnlyList<int> BlockedPorts = [22, 23, 25, 110, 143, 445, 3306, 5432, 6379, 27017];

    /// <summary>Default security risk level for tools and API DTOs.</summary>
    public const string DefaultRiskLevel = "low";

    /// <summary>Timeout in seconds for regex operations to prevent ReDoS attacks.</summary>
    public const int RegexTimeoutSeconds = 5;
}
