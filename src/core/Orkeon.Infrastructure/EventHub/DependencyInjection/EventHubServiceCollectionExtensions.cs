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
    /// Default number of consumed message identifiers the idempotency stage remembers.
    /// Bounded on purpose: an unbounded set behind a long-running hub is a leak that only
    /// shows up in production.
    /// </summary>
    public const int DefaultIdempotencyCapacity = 10_000;

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

        // Idempotent per middleware type: composition roots that layer modules call these
        // helpers more than once, and a duplicated stage means doubled log lines, nested
        // spans, or a second ACL refusal — never what the second call meant.
        if (!services.Any(d => d.ServiceType == typeof(IEventHubMiddleware) && d.ImplementationType == typeof(TMiddleware)))
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

    /// <summary>
    /// Registers the ACL stage (HUB-03). It comes after the observation pair so a refusal is
    /// logged and spanned before it travels back to the caller — the whole reason logging
    /// sits outermost. The policy decides what an undeclared crew may do; the permissive
    /// default keeps every existing crew working the day the stage is switched on.
    /// </summary>
    public static IServiceCollection AddOrkeonEventHubAcl(
        this IServiceCollection services, ICrewLinkPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(policy ?? PermissiveCrewLinkPolicy.Instance);

        // One instance behind both faces: the factory writes what a crew declared, the ACL
        // reads it. Registering them separately would give the ACL an empty registry.
        services.TryAddSingleton<InMemoryCrewLinkRegistry>();
        services.TryAddSingleton<ICrewLinkRegistry>(sp => sp.GetRequiredService<InMemoryCrewLinkRegistry>());
        services.TryAddSingleton<ICrewLinkProvider>(sp => sp.GetRequiredService<InMemoryCrewLinkRegistry>());

        services.AddEventHubMiddleware<Middleware.AclEventHubMiddleware>();
        return services;
    }

    /// <summary>
    /// Registers the idempotency stage (HUB-04). It refuses a message a mailbox already
    /// consumed, and only there: a topic message legitimately reaches every subscriber, so
    /// deduplicating it by identifier would starve all but the first.
    /// <para>
    /// The memory is bounded and does not survive the process — rc.2's hub is in-memory too, so
    /// a durable ledger behind a volatile hub would guard a scenario the subsystem cannot cross.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="capacity">How many consumed identifiers to remember.</param>
    public static IServiceCollection AddOrkeonEventHubIdempotency(
        this IServiceCollection services, int capacity = DefaultIdempotencyCapacity)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Idempotent, like the other stage helpers: the concrete type doubles as the marker.
        if (!services.Any(d => d.ServiceType == typeof(Middleware.IdempotencyEventHubMiddleware)))
        {
            services.AddSingleton(_ => new Middleware.IdempotencyEventHubMiddleware(capacity));
            services.AddSingleton<IEventHubMiddleware>(sp => sp.GetRequiredService<Middleware.IdempotencyEventHubMiddleware>());
        }

        return services;
    }

    /// <summary>
    /// Registers the validation stage (HUB-04), last of the five because it is the only one
    /// that consults a store. It checks that a declared <c>SchemaId</c> names a contract the
    /// registry holds — not that the payload conforms to it, which would need a JSON Schema
    /// engine this repository does not ship and should not half-implement.
    /// </summary>
    public static IServiceCollection AddOrkeonEventHubValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEventSchemaRegistry, InMemoryEventSchemaRegistry>();
        services.AddEventHubMiddleware<Middleware.ValidationEventHubMiddleware>();
        return services;
    }
}
