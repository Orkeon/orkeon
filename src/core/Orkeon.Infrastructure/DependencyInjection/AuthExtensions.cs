using Orkeon.Infrastructure.Security.Auth;
using Orkeon.Infrastructure.Security.Guards;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering enterprise authentication services.
/// </summary>
public static class AuthExtensions
{
    /// <summary>
    /// Adds enterprise authentication and authorization support (Azure AD, OIDC, claims-based policies).
    /// Auth providers and policies are opt-in: register them via Options pattern or direct DI.
    /// </summary>
    public static IServiceCollection AddOrkeonAuth(this IServiceCollection services)
    {
        // Register the AuthenticationGuard (opt-in: does nothing if no IAuthenticationProvider is registered)
        services.TryAddSingleton<AuthenticationGuard>();

        // Options patterns for provider configuration
        services.AddOptions<AzureAdOptions>()
            .BindConfiguration("Orkeon:Auth:AzureAD");

        services.AddOptions<OidcOptions>()
            .BindConfiguration("Orkeon:Auth:OIDC");

        return services;
    }
}
