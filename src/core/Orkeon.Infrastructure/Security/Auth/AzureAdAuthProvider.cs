using System.Security.Claims;
using Orkeon.Application.Interfaces.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Orkeon.Infrastructure.Constants.Network;
using Orkeon.Infrastructure.Constants.Orchestration;
using AppTokenValidationResult = Orkeon.Application.Interfaces.Security.TokenValidationResult;

namespace Orkeon.Infrastructure.Security.Auth;

/// <summary>
/// Configuration options for Azure AD authentication.
/// </summary>
public record AzureAdOptions
{
    /// <summary>Azure AD tenant ID.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Application (client) ID registered in Azure AD.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Authority URL (e.g. https://login.microsoftonline.com/{tenantId}/v2.0).</summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>Valid token issuers. Defaults to standard Azure AD issuers for the tenant.</summary>
    public IReadOnlyList<string> ValidIssuers { get; init; } = Array.Empty<string>();

    /// <summary>Valid audiences for token validation.</summary>
    public IReadOnlyList<string> ValidAudiences { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Authenticates tokens issued by Azure Active Directory using JWT validation.
/// </summary>
public class AzureAdAuthProvider : IAuthenticationProvider
{
    private readonly TokenValidationParameters _validationParameters;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    /// <inheritdoc />
    public string ProviderName => "AzureAD";

    /// <summary>
    /// Initializes a new instance of <see cref="AzureAdAuthProvider"/>.
    /// </summary>
    /// <param name="options">Azure AD configuration.</param>
    /// <param name="signingKeys">Signing keys for token validation. When omitted, the keys are read
    /// from the tenant's OpenID configuration (<c>{Authority}/.well-known/openid-configuration</c>) on
    /// first use and refreshed by the configuration manager — a provider built from configuration
    /// alone would otherwise have no key and reject every token.</param>
    public AzureAdAuthProvider(AzureAdOptions options, IEnumerable<SecurityKey>? signingKeys = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validIssuers = options.ValidIssuers.Count > 0
            ? options.ValidIssuers.ToList()
            :
            [
                NetworkDefaults.AzureAuthorityUrlTemplate.Replace("{tenantId}", options.TenantId, StringComparison.Ordinal),
                $"https://sts.windows.net/{options.TenantId}/"
            ];

        var validAudiences = options.ValidAudiences.Count > 0
            ? options.ValidAudiences.ToList()
            : [options.ClientId];

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = true,
            ValidAudiences = validAudiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = OrchestrationDefaults.ClockSkew
        };

        if (signingKeys != null)
        {
            _validationParameters.IssuerSigningKeys = signingKeys;
            return;
        }

        var authority = string.IsNullOrWhiteSpace(options.Authority)
            ? NetworkDefaults.AzureAuthorityUrlTemplate.Replace("{tenantId}", options.TenantId, StringComparison.Ordinal)
            : options.Authority;
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            authority.TrimEnd('/') + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AzureAdAuthProvider"/> with explicit validation parameters.
    /// </summary>
    public AzureAdAuthProvider(TokenValidationParameters validationParameters)
    {
        ArgumentNullException.ThrowIfNull(validationParameters);
        _validationParameters = validationParameters;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fail-closed auth barrier: any error validating the token is converted into a failed AuthenticationResult so an unexpected validation fault denies access rather than throwing.")]
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fail-closed auth barrier: any error validating the token is converted into a failed AppTokenValidationResult so an unexpected validation fault denies access rather than throwing.")]
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
        if (_configurationManager is null)
            return _validationParameters;

        var configuration = await _configurationManager.GetConfigurationAsync(ct).ConfigureAwait(false);
        var parameters = _validationParameters.Clone();
        parameters.IssuerSigningKeys = configuration.SigningKeys;
        return parameters;
    }
}
