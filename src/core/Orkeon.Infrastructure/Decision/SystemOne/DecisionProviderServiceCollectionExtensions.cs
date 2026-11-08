using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.Decision.SystemOne;

/// <summary>Registers a System One decision provider.</summary>
public static class DecisionProviderServiceCollectionExtensions
{
    /// <summary>Registers the active decision provider of <paramref name="configuration"/>, when there is one.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <exception cref="InvalidOperationException">The choice is refused.</exception>
    public static IServiceCollection AddOrkeonDecisionProvider(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var choice = DecisionProviderChoice.Resolve(configuration, Environment.GetEnvironmentVariable);
        services.TryAddSingleton(choice);
        return choice.HasProvider ? services.AddOrkeonDecisionProvider(configuration, choice.Provider!) : services;
    }

    /// <summary>Registers the decision model of <paramref name="provider"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="provider">The provider.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    public static IServiceCollection AddOrkeonDecisionProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        DecisionProviderDescriptor provider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(provider);

        var section = provider.Section;
        services.AddHttpClient();
        services.AddOptions<SystemOneProviderOptions>()
            .Configure(provider.ApplyDefaults)
            .Bind(configuration.GetSection(section))
            .PostConfigure(provider.FillBlanks)
            .Validate(o => o.TimeoutSeconds > 0, $"{section}:TimeoutSeconds: one second or more.")
            .Validate(o => o.MaxRetries >= 0, $"{section}:MaxRetries: zero or more.")
            .Validate(o => o.BaseUrl.IsAbsoluteUri, $"{section}:BaseUrl: an absolute URL.")
            .DeclareSettings(section);
        services.TryAddSingleton(provider);
        services.TryAddSingleton<SystemOneDecisionModel>();
        services.TryAddSingleton<IDecisionModel>(sp => sp.GetRequiredService<SystemOneDecisionModel>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, SystemOneDecideTool>());
        return services;
    }
}
