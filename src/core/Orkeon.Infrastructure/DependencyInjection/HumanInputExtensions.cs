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
        services.TryAddSingleton<HumanInputOptions>();
        services.TryAddSingleton<IHumanInputProvider, AutoApproveHumanInputProvider>();
        services.DecorateHumanInputWithDeadline();
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
        services.DecorateHumanInputWithDeadline();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, HumanInputTool>());
        return services;
    }

    /// <summary>
    /// Wraps the last <see cref="IHumanInputProvider"/> registration with the deadline-pausing decorator;
    /// idempotent. Runs once the host's own registrations are in.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection DecorateHumanInputWithDeadline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Wraps the registered provider, whichever it is, so the wait for a human never counts against a deadline (CH-01).
        // The registered descriptor moves under InnerKey with its lifetime; the decorator takes its place.
        var index = services.ToList().FindLastIndex(static d => d.ServiceType == typeof(IHumanInputProvider) && !d.IsKeyedService);
        if (index < 0)
        {
            return services;
        }

        var registered = services[index];
        if (registered.ImplementationFactory?.Target is Decorating)
        {
            return services;
        }

        services[index] = InnerDescriptor(registered);
        services.Add(new ServiceDescriptor(typeof(IHumanInputProvider), new Decorating(InnerKey).Create, registered.Lifetime));
        return services;
    }

    private const string InnerKey = "orkeon.human-input.inner";

    // The registration as it was, moved under the inner key: an instance, a factory or a type.
    private static ServiceDescriptor InnerDescriptor(ServiceDescriptor registered)
    {
        if (registered.ImplementationInstance is { } instance)
            return new ServiceDescriptor(typeof(IHumanInputProvider), InnerKey, instance);
        if (registered.ImplementationFactory is { } factory)
            return new ServiceDescriptor(typeof(IHumanInputProvider), InnerKey, (sp, _) => factory(sp), registered.Lifetime);

        return new ServiceDescriptor(typeof(IHumanInputProvider), InnerKey, registered.ImplementationType!, registered.Lifetime);
    }

    private sealed class Decorating(string innerKey)
    {
        public DeadlinePausingHumanInputProvider Create(IServiceProvider sp) =>
            new(sp.GetRequiredKeyedService<IHumanInputProvider>(innerKey));
    }
}
