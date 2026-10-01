using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Outcome of <see cref="A2ACredentialValidator.ValidateAsync"/>: whether the caller presented
/// a credential one of the declared schemes accepts, which scheme accepted it, and — for a
/// bearer token — the principal its provider extracted.
/// </summary>
/// <param name="IsValid">Whether the request may proceed.</param>
/// <param name="Scheme">The scheme that accepted the credential (<c>Bearer</c> or <c>ApiKey</c>); null when no scheme is declared or the credential was refused.</param>
/// <param name="Principal">The principal a bearer provider extracted; null otherwise.</param>
/// <param name="Error">Why the credential was refused; never contains the credential.</param>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record A2ACredentialValidation(bool IsValid, string? Scheme, ClaimsPrincipal? Principal, string? Error)
{
    /// <summary>The request carries no credential requirement (no scheme declared).</summary>
    public static A2ACredentialValidation Open { get; } = new(true, null, null, null);

    internal static A2ACredentialValidation Refused(string error) => new(false, null, null, error);
}

/// <summary>
/// Validates the <c>Authorization</c> header of an incoming A2A request against
/// <see cref="A2ASecurityOptions.AllowedAuthSchemes"/>. The scheme is only the first step:
/// <list type="bullet">
///   <item><c>Bearer</c> — the token must be accepted by one of the registered
///   <see cref="IAuthenticationProvider"/>s (Azure AD, OIDC, or a host's own);</item>
///   <item><c>ApiKey</c> — the key must equal one of the secrets named by
///   <see cref="A2ASecurityOptions.ApiKeySecretNames"/>, read through the
///   <see cref="ISecretProvider"/> on every request and compared in constant time.</item>
/// </list>
/// Any other scheme has no validator. <see cref="EnsureReadyAsync"/> refuses a configuration
/// that declares a scheme without one (fail-closed, like <c>RequireMutualTls</c> without a
/// trust anchor), so a server never starts accepting a header it cannot check.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class A2ACredentialValidator
{
    /// <summary>The bearer-token scheme, validated by an <see cref="IAuthenticationProvider"/>.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>The API-key scheme, validated against secrets read through the <see cref="ISecretProvider"/>.</summary>
    public const string ApiKeyScheme = "ApiKey";

    private readonly A2ASecurityOptions _security;
    private readonly List<IAuthenticationProvider> _bearerProviders;
    private readonly ISecretProvider? _secretProvider;

    /// <summary>Initializes a new instance of <see cref="A2ACredentialValidator"/>.</summary>
    /// <param name="security">The A2A security options (declared schemes, API-key secret names).</param>
    /// <param name="bearerProviders">The providers a bearer token is offered to, in order.</param>
    /// <param name="secretProvider">The secret provider the API keys are read from.</param>
    public A2ACredentialValidator(
        A2ASecurityOptions security,
        IEnumerable<IAuthenticationProvider> bearerProviders,
        ISecretProvider? secretProvider)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(bearerProviders);
        _security = security;
        _bearerProviders = bearerProviders.ToList();
        _secretProvider = secretProvider;
    }

    /// <summary>Whether any scheme is declared — when none is, every request is let through.</summary>
    public bool IsRequired => _security.AllowedAuthSchemes.Count > 0;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a declared scheme has no validator:
    /// <c>Bearer</c> without any <see cref="IAuthenticationProvider"/>, <c>ApiKey</c> without a
    /// secret provider, without <see cref="A2ASecurityOptions.ApiKeySecretNames"/>, or whose
    /// named secrets all fail to resolve, and any scheme other than these two.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        foreach (var scheme in _security.AllowedAuthSchemes)
        {
            if (IsScheme(scheme, BearerScheme))
            {
                if (_bearerProviders.Count == 0)
                    throw new InvalidOperationException(
                        "A2A:Security:AllowedAuthSchemes declares 'Bearer' but no token validator is registered: " +
                        "configure A2A:Security:AzureAD or A2A:Security:Oidc, or register an IAuthenticationProvider. " +
                        "A bearer token that nothing validates is not authentication.");
            }
            else if (IsScheme(scheme, ApiKeyScheme))
            {
                var keys = await ReadApiKeysAsync(ct).ConfigureAwait(false);
                if (keys.Count == 0)
                    throw new InvalidOperationException(
                        "A2A:Security:AllowedAuthSchemes declares 'ApiKey' but no key can be read: list the secret " +
                        "names in A2A:Security:ApiKeySecretNames (each one resolved through the secret provider, " +
                        "e.g. ORKEON_<NAME>) — keys are never written in the configuration itself.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"A2A:Security:AllowedAuthSchemes declares '{scheme}', which has no validator: only " +
                    $"'{BearerScheme}' and '{ApiKeyScheme}' are supported (mutual TLS is RequireMutualTls).");
            }
        }
    }

    /// <summary>
    /// Validates an <c>Authorization</c> header value. Returns <see cref="A2ACredentialValidation.Open"/>
    /// when no scheme is declared; otherwise valid only when the header's scheme is declared and its
    /// credential passes that scheme's validator.
    /// </summary>
    /// <param name="authorizationHeader">The raw <c>Authorization</c> header, or null when absent.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<A2ACredentialValidation> ValidateAsync(string? authorizationHeader, CancellationToken ct = default)
    {
        if (!IsRequired)
            return A2ACredentialValidation.Open;

        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return A2ACredentialValidation.Refused("missing Authorization header");

        var parts = authorizationHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            return A2ACredentialValidation.Refused("malformed Authorization header");

        var scheme = parts[0];
        var credential = parts[1];
        if (!_security.AllowedAuthSchemes.Any(allowed => IsScheme(scheme, allowed)))
            return A2ACredentialValidation.Refused("authentication scheme not allowed");

        if (IsScheme(scheme, BearerScheme))
            return await ValidateBearerAsync(credential, ct).ConfigureAwait(false);

        if (IsScheme(scheme, ApiKeyScheme))
            return await ValidateApiKeyAsync(credential, ct).ConfigureAwait(false);

        return A2ACredentialValidation.Refused("authentication scheme has no validator");
    }

    private async Task<A2ACredentialValidation> ValidateBearerAsync(string token, CancellationToken ct)
    {
        foreach (var provider in _bearerProviders)
        {
            var result = await provider.AuthenticateAsync(token, ct).ConfigureAwait(false);
            if (result.IsAuthenticated)
                return new A2ACredentialValidation(true, BearerScheme, result.Principal, null);
        }

        return A2ACredentialValidation.Refused("bearer token rejected");
    }

    private async Task<A2ACredentialValidation> ValidateApiKeyAsync(string presented, CancellationToken ct)
    {
        // Hashing first makes the comparison constant-time in the key length too:
        // FixedTimeEquals alone returns early on a length mismatch.
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var matched = false;
        foreach (var key in await ReadApiKeysAsync(ct).ConfigureAwait(false))
        {
            var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            matched |= CryptographicOperations.FixedTimeEquals(presentedHash, keyHash);
        }

        return matched
            ? new A2ACredentialValidation(true, ApiKeyScheme, null, null)
            : A2ACredentialValidation.Refused("API key rejected");
    }

    /// <summary>
    /// Reads every configured API key. Read per request, not cached, so a rotated secret takes
    /// effect without a restart; a name that does not resolve is skipped.
    /// </summary>
    private async Task<List<string>> ReadApiKeysAsync(CancellationToken ct)
    {
        var keys = new List<string>();
        if (_secretProvider is null)
            return keys;

        foreach (var name in _security.ApiKeySecretNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            try
            {
                using var secret = await _secretProvider.GetSecretAsync(name, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(secret.Value))
                    keys.Add(secret.Value);
            }
            catch (KeyNotFoundException)
            {
                // An unresolved name contributes no key; EnsureReadyAsync refuses when none resolves.
            }
        }

        return keys;
    }

    private static bool IsScheme(string candidate, string scheme)
        => string.Equals(candidate, scheme, StringComparison.OrdinalIgnoreCase);
}
