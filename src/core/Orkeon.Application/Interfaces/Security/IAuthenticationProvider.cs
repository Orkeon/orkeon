using System.Security.Claims;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Result of an authentication attempt.
/// </summary>
public record AuthenticationResult(bool IsAuthenticated, ClaimsPrincipal? Principal, string? Error);

/// <summary>
/// Result of a token validation check.
/// </summary>
public record TokenValidationResult(bool IsValid, string? Error, DateTimeOffset? ExpiresAt);

/// <summary>
/// Provides authentication via token validation and claims extraction.
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// The name of this authentication provider (e.g. "AzureAD", "OIDC").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Authenticates a bearer token and returns the result with a claims principal.
    /// </summary>
    System.Threading.Tasks.Task<AuthenticationResult> AuthenticateAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Validates a token without full authentication, returning validity and expiration.
    /// </summary>
    System.Threading.Tasks.Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Extracts the claims principal from a valid token.
    /// </summary>
    System.Threading.Tasks.Task<ClaimsPrincipal?> GetPrincipalAsync(string token, CancellationToken ct = default);
}
