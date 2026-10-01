using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Configuration;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Builds the <see cref="CircuitBreakerPolicy"/> of a Graph-mode crew from its YAML
/// <c>graphConfig</c> block — the only circuit-breaker setting a crew carries.
/// </summary>
public static class CircuitBreakerPolicyFactory
{
    /// <summary>
    /// Resolves the effective <see cref="CircuitBreakerPolicy"/> for a graph execution: the
    /// <paramref name="graphConfig"/> preset with its node/visit/duration limits applied, or
    /// <paramref name="fallback"/> (the strategy's built-in default) when the crew has no
    /// <c>graphConfig</c>.
    /// </summary>
    /// <param name="graphConfig">Graph-specific config (preset + node/visit/duration limits), nullable.</param>
    /// <param name="fallback">Policy to use when no config is supplied.</param>
    public static CircuitBreakerPolicy ResolveGraph(
        GraphConfig? graphConfig,
        CircuitBreakerPolicy fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        return graphConfig is null
            ? fallback
            : ApplyGraphOverrides(ResolvePreset(graphConfig.CircuitBreakerPreset), graphConfig);
    }

    private static CircuitBreakerPolicy ResolvePreset(string? presetName)
    {
#pragma warning disable CA1308 // lowercase is the required normalized form matched by the switch, not a comparison normalization
        return presetName?.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "strict" => CircuitBreakerPolicy.Strict,
            "permissive" => CircuitBreakerPolicy.Permissive,
            "default" => CircuitBreakerPolicy.Default,
            _ => CircuitBreakerPolicy.Strict // Safe default for LLM workloads
        };
    }

    // GraphConfig exposes only the node/visit/duration limits (no state timeout or degraded-mode
    // toggle); those stay at the preset value.
    private static CircuitBreakerPolicy ApplyGraphOverrides(
        CircuitBreakerPolicy policy, GraphConfig config)
    {
        return policy with
        {
            MaxTransitions = config.MaxTransitions ?? policy.MaxTransitions,
            MaxStateVisits = config.MaxStateVisits ?? policy.MaxStateVisits,
            MaxTotalDuration = config.MaxTotalDurationSeconds.HasValue
                ? TimeSpan.FromSeconds(config.MaxTotalDurationSeconds.Value)
                : policy.MaxTotalDuration,
        };
    }
}
