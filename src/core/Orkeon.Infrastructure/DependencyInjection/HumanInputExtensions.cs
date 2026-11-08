using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Checkpointing;
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
        AddPauseServices(services);
        services.DecorateHumanInputWithDeadline();
        services.DecorateToolApprovalDurably();
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
        AddPauseServices(services);
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
            services.DecorateToolApprovalDurably();
            return services;
        }

        services[index] = InnerDescriptor(registered);
        services.Add(new ServiceDescriptor(typeof(IHumanInputProvider), new Decorating(InnerKey).Create, registered.Lifetime));
        services.DecorateToolApprovalDurably();
        return services;
    }

    /// <summary>
    /// Puts the durable decorator in front of the last <see cref="IToolApprovalChannel"/> registration, or in its place when the
    /// host has none (FWK-23): the gate's question to an operator is persisted with the run's checkpoint session, and a run with
    /// nobody to answer suspends instead of refusing. Active only with <c>Orkeon:Checkpointing:Enabled</c>; otherwise the
    /// host's own channel, or none, stays as it was. Idempotent.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection DecorateToolApprovalDurably(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var index = services.ToList().FindLastIndex(static d => d.ServiceType == typeof(IToolApprovalChannel) && !d.IsKeyedService);
        if (index >= 0 && services[index].ImplementationFactory?.Target is ApprovalDecorating)
            return services;

        ServiceDescriptor? registered = index >= 0 ? services[index] : null;
        if (registered is not null)
            services[index] = ApprovalInnerDescriptor(registered);

        services.Add(new ServiceDescriptor(
            typeof(IToolApprovalChannel),
            new ApprovalDecorating(registered is not null).Create,
            registered?.Lifetime ?? ServiceLifetime.Singleton));
        return services;
    }

    private const string ApprovalInnerKey = "orkeon.tool-approval.inner";

    private static ServiceDescriptor ApprovalInnerDescriptor(ServiceDescriptor registered)
    {
        if (registered.ImplementationInstance is { } instance)
            return new ServiceDescriptor(typeof(IToolApprovalChannel), ApprovalInnerKey, instance);
        if (registered.ImplementationFactory is { } factory)
            return new ServiceDescriptor(typeof(IToolApprovalChannel), ApprovalInnerKey, (sp, _) => factory(sp), registered.Lifetime);

        return new ServiceDescriptor(typeof(IToolApprovalChannel), ApprovalInnerKey, registered.ImplementationType!, registered.Lifetime);
    }

    private sealed class ApprovalDecorating(bool hasInner)
    {
        public object Create(IServiceProvider sp)
        {
            var inner = hasInner ? sp.GetRequiredKeyedService<IToolApprovalChannel>(ApprovalInnerKey) : null;
            return Decorating.Engaged(sp) is not { } parts
                ? inner!
                : new DurableToolApprovalChannel(
                    inner,
                    parts.RunScope,
                    parts.Store,
                    parts.Broker,
                    sp.GetRequiredService<IOptions<HumanInputPauseOptions>>(),
                    sp.GetService<TimeProvider>() ?? TimeProvider.System,
                    sp.GetRequiredService<ILogger<DurableHumanInputProvider>>());
        }
    }

    /// <summary>
    /// Makes <see cref="HumanInputPause.Hold"/> the policy of <c>Orkeon:HumanInput:Pause</c> when the settings leave it unset
    /// (the daemon, <c>orkeon run --events</c>): the run waits for an answer instead of suspending, up to
    /// <paramref name="holdTimeout"/> when one is given (FWK-23). A value the settings give wins.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="holdTimeout">The hold timeout the host defaults to, or null for none.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection UseHoldByDefault(this IServiceCollection services, TimeSpan? holdTimeout)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.PostConfigure<HumanInputPauseOptions>(options =>
        {
            options.Pause ??= HumanInputPause.Hold;
            options.HoldTimeout ??= holdTimeout;
        });
        return services;
    }

    private static void AddPauseServices(IServiceCollection services)
    {
        services.AddOrkeonSettings<HumanInputPauseOptions>(HumanInputPauseOptions.SectionName);
        services.TryAddSingleton<IPendingInputBroker, PendingInputBroker>();
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
            new(MakeDurable(sp, sp.GetRequiredKeyedService<IHumanInputProvider>(innerKey)));

        // A question outlives the process (FWK-23) only when the run is checkpointed: without a session there is
        // nothing to persist it with, and the provider is left as the host built it.
        internal static (IRunScope RunScope, IPendingInputStore Store, IPendingInputBroker Broker)? Engaged(IServiceProvider sp)
        {
            if (sp.GetService<IOptions<CheckpointingOptions>>()?.Value.Enabled != true
                || sp.GetService<IRunScope>() is not { } runScope
                || sp.GetService<ICheckpointManager>() is not IPendingInputStore store
                || sp.GetService<IPendingInputBroker>() is not { } broker)
            {
                return null;
            }

            return (runScope, store, broker);
        }

        private static IHumanInputProvider MakeDurable(IServiceProvider sp, IHumanInputProvider inner)
        {
            if (Engaged(sp) is not { } parts)
                return inner;

            return new DurableHumanInputProvider(
                inner,
                parts.RunScope,
                parts.Store,
                parts.Broker,
                sp.GetRequiredService<IOptions<HumanInputPauseOptions>>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<ILogger<DurableHumanInputProvider>>());
        }
    }
}
