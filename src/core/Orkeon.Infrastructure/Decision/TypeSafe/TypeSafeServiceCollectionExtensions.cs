using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.Decision.TypeSafe;

/// <summary>Registers the TypeSafe System One decision model.</summary>
public static class TypeSafeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TypeSafeDecisionModel"/> as the <see cref="IDecisionModel"/>, bound to
    /// the <c>Orkeon:TypeSafe</c> section. Opt-in: a host that never calls it has no decision
    /// model. The key is read at the first call, not here, so a host starts without it and the
    /// first decision names the variable to set. Registers with it the <c>system_one_decide</c> tool
    /// and the input guard, which runs when <c>Orkeon:TypeSafe:Guard:Enabled</c>.
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
            .Validate(o => o.TimeoutSeconds > 0, $"{TypeSafeOptions.SectionName}:TimeoutSeconds: one second or more.")
            .Validate(o => o.MaxRetries >= 0, $"{TypeSafeOptions.SectionName}:MaxRetries: zero or more.")
            .Validate(o => o.BaseUrl.IsAbsoluteUri, $"{TypeSafeOptions.SectionName}:BaseUrl: an absolute URL.")
            .Validate(o => o.Guard.BlockThreshold is >= 0 and <= 1 && o.Guard.WarnThreshold is >= 0 and <= 1,
                $"{TypeSafeOptions.SectionName}:Guard:BlockThreshold and WarnThreshold: probabilities in [0, 1].")
            .DeclareSettings(TypeSafeOptions.SectionName);
        services.TryAddSingleton<TypeSafeDecisionModel>();
        services.TryAddSingleton<IDecisionModel>(sp => sp.GetRequiredService<TypeSafeDecisionModel>());
        services.TryAddSingleton<TypeSafeInputGuard>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, SystemOneDecideTool>());
        return services;
    }
}
