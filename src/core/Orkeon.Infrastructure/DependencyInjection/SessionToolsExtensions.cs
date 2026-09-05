using Microsoft.Extensions.Configuration;
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
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Optional host configuration. When supplied, <c>Llm:AvailableModels</c> is read into the
    /// session metadata so a scripted agent can offer the models this provider serves. Passed in
    /// rather than resolved from the provider: Infrastructure does not go looking for
    /// configuration it was not handed.
    /// </param>
    public static IServiceCollection AddOrkeonSessionTools(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var availableModels = ReadAvailableModels(configuration);

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
                sp.GetService<Orkeon.Domain.SharedKernel.ILlmProvider>()?.BaseConfig?.Model,
                availableModels));
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

    /// <summary>
    /// Reads <c>Llm:AvailableModels</c> — a string array, or a comma-separated string so the
    /// value can also arrive as one environment variable
    /// (<c>Llm__AvailableModels=a,b</c>), which is how a container usually passes it. An
    /// absent or unreadable section reads as an empty array: the session buffer already
    /// treats "none configured" and "none declared" as the same thing.
    /// </summary>
    private static string[] ReadAvailableModels(IConfiguration? configuration)
    {
        var section = configuration?.GetSection("Llm:AvailableModels");
        if (section is null || !section.Exists()) return [];

        // An array binds to children; a scalar binds to Value. Both shapes are accepted because
        // a settings file naturally writes the first and an env var can only write the second.
        var asArray = section.GetChildren()
            .Select(c => c.Value)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToArray();
        if (asArray.Length > 0) return asArray;

        return section.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
    }
}
