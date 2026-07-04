namespace Orkeon.Domain.Configuration;

/// <summary>
/// Circuit breaker configuration for task execution FSM.
/// Immutable record suitable for domain configuration.
/// Maps from YAML <c>circuitBreaker</c> section.
/// </summary>
public sealed record CircuitBreakerConfig
{
    /// <summary>
    /// Preset name: "strict", "permissive", or "default".
    /// When set, individual fields override the preset values.
    /// </summary>
    public string? Preset { get; init; }

    /// <summary>Maximum number of state transitions before tripping. Default: policy-dependent.</summary>
    public int? MaxTransitions { get; init; }

    /// <summary>Maximum time (in seconds) the FSM can stay in one state. Default: policy-dependent.</summary>
    public int? StateTimeoutSeconds { get; init; }

    /// <summary>Maximum times a state can be revisited (cycle detection). Default: policy-dependent.</summary>
    public int? MaxStateVisits { get; init; }

    /// <summary>Maximum total execution time in seconds. Default: policy-dependent.</summary>
    public int? MaxTotalDurationSeconds { get; init; }

    /// <summary>When true, the FSM transitions to a degraded state instead of throwing. Default: policy-dependent.</summary>
    public bool? UseDegradedMode { get; init; }

    /// <summary>Maximum retries after task failure. Default: 3.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Maximum tool calls per execution round. Default: 10.</summary>
    public int? MaxToolCallsPerRound { get; init; }

    /// <summary>Maximum validation retry loops. Default: 3.</summary>
    public int? MaxValidationRetries { get; init; }
}
