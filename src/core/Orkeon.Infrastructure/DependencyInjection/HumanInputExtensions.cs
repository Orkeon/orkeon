using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Infrastructure.Tools.HumanInput;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// DI extension methods for the human-input tool and providers.
/// </summary>
public static class HumanInputExtensions
{
    /// <summary>
    /// Registers the <c>human_input</c> tool and a default
    /// <see cref="AutoApproveHumanInputProvider"/> fallback. Use
    /// <see cref="AddOrkeonHumanInput{TProvider}"/> to override the provider
    /// with a runner-specific implementation (e.g. Terminal.Gui modal).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddOrkeonHumanInput(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IHumanInputProvider, AutoApproveHumanInputProvider>();
        // TryAddEnumerable de-duplicates by ImplementationType — multiple calls
        // to AddOrkeonHumanInput() in the same container won't add the tool
        // twice (e.g. when both RunnerExecution defaults and a per-runner
        // configureServices call this extension).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, HumanInputTool>());
        return services;
    }

    /// <summary>
    /// Registers the <c>human_input</c> tool with a specific
    /// <see cref="IHumanInputProvider"/> implementation.
    /// </summary>
    /// <typeparam name="TProvider">Concrete provider type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddOrkeonHumanInput<TProvider>(this IServiceCollection services)
        where TProvider : class, IHumanInputProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IHumanInputProvider, TProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, HumanInputTool>());
        return services;
    }
}
