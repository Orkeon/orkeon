using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.EventHub;

namespace Orkeon.Infrastructure.EventHub.DependencyInjection;

/// <summary>
/// DI registration helpers for the EventHub v1.0 adapters (in-memory only).
/// </summary>
public static class EventHubServiceCollectionExtensions
{
    /// <summary>
    /// Registers the v1.0 in-memory <see cref="IEventHub"/>, <see cref="IEventHubCallerContext"/>,
    /// and <see cref="IEventSchemaRegistry"/> implementations as singletons.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    public static IServiceCollection AddOrkeonInMemoryEventHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEventHubCallerContext, DefaultEventHubCallerContext>();
        services.TryAddSingleton<IEventSchemaRegistry, InMemoryEventSchemaRegistry>();
        services.TryAddSingleton<IEventHub, InMemoryEventHub>();
        return services;
    }

    /// <summary>
    /// Registers one middleware on the hub pipeline (HUB-01). Order of registration is order
    /// of execution on the publish path, and the reverse on the receive path — the spec's §12
    /// sequence is Logging → Telemetry → Acl → Idempotency → Validation, and it matters:
    /// logging must see what the ACL later rejects.
    /// </summary>
    public static IServiceCollection AddEventHubMiddleware<TMiddleware>(this IServiceCollection services)
        where TMiddleware : class, IEventHubMiddleware
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEventHubMiddleware, TMiddleware>();
        return services;
    }

    /// <summary>
    /// Registers the two observation stages (HUB-02) in the spec's §12 order: logging first
    /// so it sees what a later stage rejects, then telemetry. Opt-in — a hub that nobody
    /// watches pays nothing.
    /// </summary>
    public static IServiceCollection AddOrkeonEventHubObservability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddEventHubMiddleware<Middleware.LoggingEventHubMiddleware>();
        services.AddEventHubMiddleware<Middleware.TelemetryEventHubMiddleware>();
        return services;
    }
}
