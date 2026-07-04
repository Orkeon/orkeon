namespace Orkeon.Domain.Constants.Security;

/// <summary>
/// Default values for security contexts, secret caching, and risk assessment.
/// Centralises security-related magic values used across the Orkeon platform.
/// </summary>
public static class SecurityDefaults
{
    /// <summary>Default validity duration for security contexts (8 h).</summary>
    public static readonly TimeSpan DefaultSecurityContextValidity = TimeSpan.FromHours(8);

    /// <summary>Default TTL for cached secrets from vault providers (5 min).</summary>
    public static readonly TimeSpan DefaultSecretCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Default security risk level for tools and API DTOs.</summary>
    public const string DefaultRiskLevel = "low";
}
