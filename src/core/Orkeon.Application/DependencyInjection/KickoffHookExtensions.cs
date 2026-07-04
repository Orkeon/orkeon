using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Callback;

namespace Orkeon.Application.DependencyInjection;

/// <summary>
/// Extension methods for registering crew kickoff hooks.
/// <para>
/// Opt-in subsystem (R4.9 — MORT-004): kickoff hooks are NOT registered by
/// <c>AddOrkeonApplication()</c>. Hosts that need before/after kickoff callbacks call
/// <c>AddOrkeonKickoffHooks()</c> (or the <c>AddBeforeKickoffHook</c> /
/// <c>AddAfterKickoffHook</c> helpers) explicitly and invoke
/// <see cref="ICrewKickoffHookRunner"/> around their crew kickoff call.
/// See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// </summary>
public static class KickoffHookExtensions
{
    /// <summary>
    /// Opt-in: adds the <see cref="ICrewKickoffHookRunner"/> that executes every
    /// <see cref="BeforeKickoffHook"/> / <see cref="AfterKickoffHook"/> registered in DI.
    /// </summary>
    /// <remarks>
    /// Self-contained: the runner only depends on the hook collections (empty by
    /// default) and optional logging.
    /// </remarks>
    public static IServiceCollection AddOrkeonKickoffHooks(this IServiceCollection services)
    {
        services.TryAddSingleton<ICrewKickoffHookRunner, CrewKickoffHookRunner>();
        return services;
    }

    /// <summary>
    /// Registers a <see cref="BeforeKickoffHook"/> and ensures the hook runner is available.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="hook">The hook to execute before crew kickoff.</param>
    public static IServiceCollection AddBeforeKickoffHook(
        this IServiceCollection services, BeforeKickoffHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        services.AddOrkeonKickoffHooks();
        services.AddSingleton(hook);
        return services;
    }

    /// <summary>
    /// Registers an <see cref="AfterKickoffHook"/> and ensures the hook runner is available.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="hook">The hook to execute after crew kickoff.</param>
    public static IServiceCollection AddAfterKickoffHook(
        this IServiceCollection services, AfterKickoffHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        services.AddOrkeonKickoffHooks();
        services.AddSingleton(hook);
        return services;
    }
}
