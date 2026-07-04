using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering tool-level rate limiting and token budget tracking.
/// <para>
/// Opt-in subsystem (R4.9 — MORT-003): these services are NOT registered by
/// <c>AddOrkeonInfrastructure()</c> (which only wires LLM-level rate limiting,
/// <c>ILlmRateLimiter</c>). Hosts that throttle tool execution or track token budgets
/// call <c>AddOrkeonToolRateLimiting()</c> explicitly.
/// See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// </summary>
public static class ToolRateLimitingExtensions
{
    /// <summary>
    /// Opt-in: adds the per-tool rate limiter (<see cref="IToolRateLimiter"/>) and the
    /// token budget tracker (<see cref="ITokenBudgetTracker"/>).
    /// Options bind from the "ToolRateLimiting" and "TokenBudget" configuration sections.
    /// </summary>
    /// <remarks>
    /// Self-contained: both services only require logging and options. When
    /// <c>AddOrkeonCostTracking()</c> (part of the defaults) registered an
    /// <c>IModelPricingRegistry</c>, the tracker picks it up to convert tokens to cost.
    /// </remarks>
    public static IServiceCollection AddOrkeonToolRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<TokenBudgetOptions>()
            .BindConfiguration("TokenBudget");
        services.AddOptions<ToolRateLimitOptions>()
            .BindConfiguration("ToolRateLimiting");

        services.TryAddSingleton<ITokenBudgetTracker, TokenBudgetTracker>();
        services.TryAddSingleton<IToolRateLimiter, ToolRateLimiter>();

        return services;
    }
}
