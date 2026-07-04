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
        services.TryAddSingleton<ISessionBufferService>(_ => new InMemorySessionBufferService());
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
