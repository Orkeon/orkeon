using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Task;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Creates <see cref="CircuitBreakerPolicy"/> and <see cref="TaskExecutionStateMachine"/>
/// instances from YAML-sourced <see cref="CircuitBreakerConfig"/>.
/// Supports crew-level defaults with per-task overrides.
/// </summary>
public static class CircuitBreakerPolicyFactory
{
    /// <summary>
    /// Resolves the effective <see cref="CircuitBreakerPolicy"/> for a task,
    /// merging crew-level defaults with task-level overrides.
    /// </summary>
    /// <param name="crewDefault">Crew-level circuit breaker config (nullable).</param>
    /// <param name="taskOverride">Task-level circuit breaker config (nullable, takes precedence).</param>
    /// <returns>A fully resolved <see cref="CircuitBreakerPolicy"/>.</returns>
    public static CircuitBreakerPolicy Resolve(
        CircuitBreakerConfig? crewDefault,
        CircuitBreakerConfig? taskOverride)
    {
        // If nothing is configured, use Strict (safe default for LLM orchestration)
        if (crewDefault == null && taskOverride == null)
            return CircuitBreakerPolicy.Strict;

        // Start from preset (task override preset takes precedence)
        var presetName = taskOverride?.Preset ?? crewDefault?.Preset;
        var basePolicy = ResolvePreset(presetName);

        // Apply crew-level overrides
        if (crewDefault != null)
            basePolicy = ApplyOverrides(basePolicy, crewDefault);

        // Apply task-level overrides (highest precedence)
        if (taskOverride != null)
            basePolicy = ApplyOverrides(basePolicy, taskOverride);

        return basePolicy;
    }

    /// <summary>
    /// Resolves the effective <see cref="CircuitBreakerPolicy"/> for a graph execution.
    /// Precedence: the graph-specific <paramref name="graphConfig"/> wins; failing that, a
    /// crew-level <paramref name="crewDefault"/> circuit-breaker config; failing both,
    /// <paramref name="fallback"/> (the strategy's built-in default). Keeps the graph default
    /// path byte-identical when neither config is present.
    /// </summary>
    /// <param name="graphConfig">Graph-specific config (preset + node/visit/duration limits), nullable.</param>
    /// <param name="crewDefault">Crew-level circuit-breaker config, nullable.</param>
    /// <param name="fallback">Policy to use when no config is supplied.</param>
    public static CircuitBreakerPolicy ResolveGraph(
        GraphConfig? graphConfig,
        CircuitBreakerConfig? crewDefault,
        CircuitBreakerPolicy fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        if (graphConfig is not null)
            return ApplyGraphOverrides(ResolvePreset(graphConfig.CircuitBreakerPreset), graphConfig);

        if (crewDefault is not null)
            return Resolve(crewDefault, null);

        return fallback;
    }

    /// <summary>
    /// Creates a fully configured <see cref="TaskExecutionStateMachine"/> for a task.
    /// </summary>
    public static StateMachine<TaskExecutionState, TaskExecutionEvent> CreateTaskFsm(
        CircuitBreakerConfig? crewDefault,
        CircuitBreakerConfig? taskOverride)
    {
        var policy = Resolve(crewDefault, taskOverride);
        return TaskExecutionStateMachine.Create(policy);
    }

    /// <summary>
    /// Extracts the guard context limits from the effective configuration.
    /// </summary>
    public static TaskExecutionGuardContext CreateGuardContext(
        CircuitBreakerConfig? crewDefault,
        CircuitBreakerConfig? taskOverride)
    {
        var effective = taskOverride ?? crewDefault;

        return new TaskExecutionGuardContext
        {
            MaxRetries = effective?.MaxRetries ?? 3,
            MaxToolCallsPerRound = effective?.MaxToolCallsPerRound ?? 10,
            MaxValidationRetries = effective?.MaxValidationRetries ?? 3,
            IsToolRegistered = true, // Runtime concern, not config
        };
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

    private static CircuitBreakerPolicy ApplyOverrides(
        CircuitBreakerPolicy policy, CircuitBreakerConfig config)
    {
        return policy with
        {
            MaxTransitions = config.MaxTransitions ?? policy.MaxTransitions,
            StateTimeout = config.StateTimeoutSeconds.HasValue
                ? TimeSpan.FromSeconds(config.StateTimeoutSeconds.Value)
                : policy.StateTimeout,
            MaxStateVisits = config.MaxStateVisits ?? policy.MaxStateVisits,
            MaxTotalDuration = config.MaxTotalDurationSeconds.HasValue
                ? TimeSpan.FromSeconds(config.MaxTotalDurationSeconds.Value)
                : policy.MaxTotalDuration,
            UseDegradedMode = config.UseDegradedMode ?? policy.UseDegradedMode,
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
