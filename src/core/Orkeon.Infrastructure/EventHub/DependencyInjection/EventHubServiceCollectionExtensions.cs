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
}
