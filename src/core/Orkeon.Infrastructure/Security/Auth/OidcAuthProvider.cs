using System.Security.Claims;
using Orkeon.Application.Interfaces.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Orkeon.Infrastructure.Constants.Orchestration;
using AppTokenValidationResult = Orkeon.Application.Interfaces.Security.TokenValidationResult;

namespace Orkeon.Infrastructure.Security.Auth;

/// <summary>
/// Configuration options for generic OIDC authentication.
/// </summary>
public record OidcOptions
{
    /// <summary>OIDC authority URL (e.g. https://login.example.com/realms/myapp).</summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>Client ID registered with the OIDC provider.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Valid audiences for token validation.</summary>
    public IReadOnlyList<string> ValidAudiences { get; init; } = Array.Empty<string>();

    /// <summary>Whether to require HTTPS for the metadata endpoint. Defaults to true.</summary>
    public bool RequireHttpsMetadata { get; init; } = true;
}

/// <summary>
/// Authenticates tokens using generic OpenID Connect discovery and JWKS validation.
/// Supports Okta, Auth0, Keycloak, and any OIDC-compliant provider.
/// </summary>
public class OidcAuthProvider : IAuthenticationProvider
{
    private readonly OidcOptions _options;
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;
    private readonly TokenValidationParameters? _staticValidationParameters;

    /// <inheritdoc />
    public string ProviderName => "OIDC";

    /// <summary>
    /// Initializes a new instance of <see cref="OidcAuthProvider"/> with OIDC discovery.
    /// </summary>
    public OidcAuthProvider(OidcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        var metadataAddress = options.Authority.TrimEnd('/') + "/.well-known/openid-configuration";

        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata });
    }

    /// <summary>
    /// Initializes a new instance of <see cref="OidcAuthProvider"/> with explicit validation parameters
    /// (useful for testing without an OIDC discovery endpoint).
    /// </summary>
    public OidcAuthProvider(OidcOptions options, TokenValidationParameters validationParameters)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        ArgumentNullException.ThrowIfNull(validationParameters);
        _staticValidationParameters = validationParameters;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fail-closed auth barrier: any error (incl. OIDC metadata retrieval) is converted into a failed AuthenticationResult so an unexpected fault denies access rather than throwing.")]
    public async Task<AuthenticationResult> AuthenticateAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var parameters = await GetValidationParametersAsync(ct).ConfigureAwait(false);
            var result = await _tokenHandler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);

            if (!result.IsValid)
            {
                return new AuthenticationResult(false, null, result.Exception?.Message ?? "Token validation failed");
            }

            var principal = new ClaimsPrincipal(result.ClaimsIdentity);
            return new AuthenticationResult(true, principal, null);
        }
        catch (Exception ex)
        {
            return new AuthenticationResult(false, null, ex.Message);
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fail-closed auth barrier: any error (incl. OIDC metadata retrieval) is converted into a failed AppTokenValidationResult so an unexpected fault denies access rather than throwing.")]
    public async Task<AppTokenValidationResult> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var parameters = await GetValidationParametersAsync(ct).ConfigureAwait(false);
            var result = await _tokenHandler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);

            if (!result.IsValid)
            {
                return new AppTokenValidationResult(false, result.Exception?.Message ?? "Token validation failed", null);
            }

            DateTimeOffset? expiresAt = result.SecurityToken.ValidTo != DateTime.MinValue
                ? new DateTimeOffset(result.SecurityToken.ValidTo)
                : null;

            return new AppTokenValidationResult(true, null, expiresAt);
        }
        catch (Exception ex)
        {
            return new AppTokenValidationResult(false, ex.Message, null);
        }
    }

    /// <inheritdoc />
    public async Task<ClaimsPrincipal?> GetPrincipalAsync(string token, CancellationToken ct = default)
    {
        var result = await AuthenticateAsync(token, ct).ConfigureAwait(false);
        return result.Principal;
    }

    private async Task<TokenValidationParameters> GetValidationParametersAsync(CancellationToken ct)
    {
        if (_staticValidationParameters != null)
            return _staticValidationParameters;

        var config = await _configurationManager!.GetConfigurationAsync(ct).ConfigureAwait(false);

        var audiences = _options.ValidAudiences.Count > 0
            ? _options.ValidAudiences.ToList()
            : [_options.ClientId];

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config.Issuer,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ClockSkew = OrchestrationDefaults.ClockSkew
        };
    }
}
