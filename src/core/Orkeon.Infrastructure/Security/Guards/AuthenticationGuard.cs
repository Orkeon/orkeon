using System.Security.Claims;
using Orkeon.Application.Interfaces.Security;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian that enforces authentication on incoming requests.
/// Operates on <see cref="GuardPhase.Input"/> and checks for a valid authenticated principal.
/// Security is opt-in: if no authentication providers are configured, all requests are allowed.
/// </summary>
public partial class AuthenticationGuard : IGuardian
{
    private readonly IEnumerable<IAuthenticationProvider> _providers;
    private readonly ILogger<AuthenticationGuard> _logger;

    /// <summary>Key used to store/retrieve the bearer token in GuardContext.ToolArgs.</summary>
    public const string TokenKey = "auth_token";

    /// <summary>Key used to store/retrieve the ClaimsPrincipal in GuardContext.ToolArgs.</summary>
    public const string PrincipalKey = "auth_principal";

    /// <summary>
    /// Initializes a new instance of <see cref="AuthenticationGuard"/>.
    /// </summary>
    public AuthenticationGuard(
        IEnumerable<IAuthenticationProvider> providers,
        ILogger<AuthenticationGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CheckCoreAsync();

        async Task<GuardResult> CheckCoreAsync()
        {
            if (context.Phase != GuardPhase.Input)
                return GuardResult.Allow();

            var providers = _providers.ToList();
            if (providers.Count == 0)
            {
                // No auth providers configured — opt-in security, allow everything
                return GuardResult.Allow();
            }

            if (HasAuthenticatedPrincipal(context))
            {
                return GuardResult.Allow();
            }

            var token = ExtractToken(context);
            if (string.IsNullOrWhiteSpace(token))
            {
                var violation = new GuardViolation(
                    nameof(AuthenticationGuard),
                    GuardPhase.Input,
                    "Authentication required: no bearer token provided",
                    GuardThreatSeverity.High,
                    DateTime.UtcNow);

                LogAuthenticationBlockedNoTokenProvided(context.AgentId);
                return GuardResult.Block("Authentication required", [violation]);
            }

            if (await TryAuthenticateWithProvidersAsync(providers, token, context, ct).ConfigureAwait(false))
                return GuardResult.Allow();

            var authViolation = new GuardViolation(
                nameof(AuthenticationGuard),
                GuardPhase.Input,
                "Authentication failed: token could not be validated by any provider",
                GuardThreatSeverity.High,
                DateTime.UtcNow);

            LogAuthenticationFailedForAgentNo(context.AgentId);
            return GuardResult.Block("Authentication failed", [authViolation]);
        }
    }

    /// <summary>True when the context already carries an authenticated <see cref="ClaimsPrincipal"/>.</summary>
    private static bool HasAuthenticatedPrincipal(GuardContext context)
        => context.ToolArgs?.TryGetValue(PrincipalKey, out var existingPrincipal) == true
            && existingPrincipal is ClaimsPrincipal { Identity.IsAuthenticated: true };

    /// <summary>Extracts the bearer token from the context tool args, or null when absent.</summary>
    private static string? ExtractToken(GuardContext context)
        => context.ToolArgs?.TryGetValue(TokenKey, out var tokenObj) == true
            ? tokenObj as string
            : null;

    /// <summary>Tries each provider until one authenticates the token, logging the winner.</summary>
    private async Task<bool> TryAuthenticateWithProvidersAsync(
        List<IAuthenticationProvider> providers,
        string token,
        GuardContext context,
        CancellationToken ct)
    {
        foreach (var provider in providers)
        {
            var result = await provider.AuthenticateAsync(token, ct).ConfigureAwait(false);
            if (result.IsAuthenticated)
            {
                LogAuthenticationSucceededViaForAgent(provider.ProviderName, context.AgentId);
                return true;
            }
        }
        return false;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Authentication blocked: no token provided for agent {AgentId}")]
    private partial void LogAuthenticationBlockedNoTokenProvided(object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Authentication succeeded via {Provider} for agent {AgentId}")]
    private partial void LogAuthenticationSucceededViaForAgent(object provider, object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Authentication failed for agent {AgentId}: no provider accepted the token")]
    private partial void LogAuthenticationFailedForAgentNo(object agentId);

}
