using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Decision.TypeSafe;

/// <summary>Registers the TypeSafe System One decision model.</summary>
public static class TypeSafeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TypeSafeDecisionModel"/> as the <see cref="IDecisionModel"/>, bound to
    /// the <c>Orkeon:TypeSafe</c> section. Opt-in: a host that never calls it has no decision
    /// model. The key is read at the first call, not here, so a host starts without it and the
    /// first decision names the variable to set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    public static IServiceCollection AddOrkeonTypeSafe(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient();
        services.AddOptions<TypeSafeOptions>()
            .Bind(configuration.GetSection(TypeSafeOptions.SectionName))
            .DeclareSettings(TypeSafeOptions.SectionName);
        services.TryAddSingleton<TypeSafeDecisionModel>();
        services.TryAddSingleton<IDecisionModel>(sp => sp.GetRequiredService<TypeSafeDecisionModel>());
        return services;
    }
}
