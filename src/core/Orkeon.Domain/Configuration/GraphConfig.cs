namespace Orkeon.Domain.Configuration;

/// <summary>
/// Configuration for graph-based (LangGraph-style) process execution.
/// Only relevant when <see cref="CrewConfiguration.Process"/> is <c>Graph</c>.
/// </summary>
public sealed record GraphConfig
{
    /// <summary>
    /// Maximum retry cycles for failed tasks before giving up.
    /// Each cycle re-enqueues all failed tasks for another attempt.
    /// Default: 2.
    /// </summary>
    public int MaxRetryCycles { get; init; } = 2;

    /// <summary>
    /// Circuit breaker preset name ("strict", "permissive", or "default").
    /// Overridden by explicit field values below when specified.
    /// Default: "strict".
    /// </summary>
    public string CircuitBreakerPreset { get; init; } = "strict";

    /// <summary>
    /// Maximum number of graph node transitions before the circuit breaker trips.
    /// Null = use preset default.
    /// </summary>
    public int? MaxTransitions { get; init; }

    /// <summary>
    /// Maximum number of times the same node can be visited before cycle detection trips.
    /// Null = use preset default.
    /// </summary>
    public int? MaxStateVisits { get; init; }

    /// <summary>
    /// Maximum total execution duration in seconds before the circuit breaker trips.
    /// Null = use preset default.
    /// </summary>
    public int? MaxTotalDurationSeconds { get; init; }
}
