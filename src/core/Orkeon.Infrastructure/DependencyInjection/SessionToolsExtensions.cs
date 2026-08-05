using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Session;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// DI extension for the coding-agent session primitives (exp 07 SPEC §7): the
/// <see cref="ISessionBufferService"/> singleton, the typed memory store, the cost-tracking
/// substrate, and the six create-tools (<c>session_store</c>, <c>session_snip</c>,
/// <c>token_budget</c>, <c>memory_store</c>, <c>session_cost</c>, <c>session_stats</c>).
/// </summary>
public static class SessionToolsExtensions
{
    /// <summary>
    /// Registers the session buffer, typed memory store, cost-tracking substrate, and the
    /// six session/telemetry/memory tools as <see cref="IBaseTool"/> entries. Idempotent.
    /// </summary>
    public static IServiceCollection AddOrkeonSessionTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Backing services.
        //
        // The buffer is seeded with the model the runtime will actually call.
        // `InMemorySessionBufferService(model)` has always accepted one and nobody
        // ever passed it, so `SessionMetadata.Model` was null in every host — and
        // `session_store get_metadata`, the only way a script can learn the active
        // model, reported nothing whatever the configuration said. A coding agent's
        // `/model` command then had to print "(unknown)" for a perfectly well
        // configured provider, which reads as "no provider" and is not the same thing.
        //
        // Resolved lazily through the factory, so registration order does not matter
        // and a host with no provider at all still gets a working buffer (null model,
        // as before). GetService, not GetRequiredService: the provider is optional.
        services.TryAddSingleton<ISessionBufferService>(sp =>
            new InMemorySessionBufferService(
                sp.GetService<Orkeon.Domain.SharedKernel.ILlmProvider>()?.BaseConfig?.Model));
        services.TryAddSingleton<ICategoryMemoryStore, InMemoryCategoryMemoryStore>();

        // Cost-tracking substrate (exp 07 Phase 6) — not registered elsewhere. Both the
        // pricing registry and the manager resolve IOptions<CostTrackingOptions> + ILogger.
        services.AddOptions<CostTrackingOptions>();
        services.TryAddSingleton<IModelPricingRegistry, ModelPricingRegistry>();
        services.TryAddSingleton<ICostBudgetManager, CostBudgetManager>();

        // Tools.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, SessionStoreTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, SessionSnipTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, TokenBudgetTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, MemoryStoreTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, SessionCostTool>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, SessionStatsTool>());
        return services;
    }
}
