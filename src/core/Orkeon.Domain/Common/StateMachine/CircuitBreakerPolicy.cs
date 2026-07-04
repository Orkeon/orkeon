namespace Orkeon.Domain.Common.StateMachine;

/// <summary>
/// Configuration for the state machine circuit breaker.
/// Prevents runaway loops, repeated cycles, and stale executions.
/// </summary>
public sealed record CircuitBreakerPolicy
{
    /// <summary>
    /// Maximum number of transitions allowed before tripping.
    /// Default: 100.
    /// </summary>
    public int MaxTransitions { get; init; } = 100;

    /// <summary>
    /// Maximum time the machine can spend in a single state before tripping.
    /// Default: 5 minutes. Use <see cref="TimeSpan.Zero"/> to disable.
    /// </summary>
    public TimeSpan StateTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of times the same state can be visited before tripping.
    /// This detects cycles (e.g., Executing → Failed → Retry → Executing loop).
    /// Default: 10. Use 0 to disable.
    /// </summary>
    public int MaxStateVisits { get; init; } = 10;

    /// <summary>
    /// Maximum total lifetime of the state machine from first transition.
    /// Default: 30 minutes. Use <see cref="TimeSpan.Zero"/> to disable.
    /// </summary>
    public TimeSpan MaxTotalDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// When true, the machine enters a degraded state instead of throwing.
    /// The degraded state must be configured via <see cref="StateMachineBuilder{TState,TEvent}.WithDegradedState"/>.
    /// When false (default), a <see cref="CircuitBrokenException"/> is thrown.
    /// </summary>
    public bool UseDegradedMode { get; init; }

    /// <summary>A permissive policy with high limits, suitable for development.</summary>
    public static CircuitBreakerPolicy Permissive => new()
    {
        MaxTransitions = 1000,
        StateTimeout = TimeSpan.FromMinutes(30),
        MaxStateVisits = 50,
        MaxTotalDuration = TimeSpan.FromHours(2)
    };

    /// <summary>A strict policy for production LLM orchestration.</summary>
    public static CircuitBreakerPolicy Strict => new()
    {
        MaxTransitions = 50,
        StateTimeout = TimeSpan.FromMinutes(2),
        MaxStateVisits = 5,
        MaxTotalDuration = TimeSpan.FromMinutes(10),
        UseDegradedMode = true
    };

    /// <summary>Default policy.</summary>
    public static CircuitBreakerPolicy Default => new();
}

/// <summary>
/// Snapshot of the circuit breaker's current status.
/// </summary>
public sealed record CircuitBreakerStatus
{
    /// <summary>Whether the circuit is currently broken.</summary>
    public required bool IsBroken { get; init; }

    /// <summary>The reason the circuit broke (null if healthy).</summary>
    public string? BrokenReason { get; init; }

    /// <summary>Total transitions so far.</summary>
    public required int TotalTransitions { get; init; }

    /// <summary>Time spent in the current state.</summary>
    public required TimeSpan TimeInCurrentState { get; init; }

    /// <summary>How many times the current state has been visited.</summary>
    public required int CurrentStateVisitCount { get; init; }

    /// <summary>Total elapsed time since the machine started.</summary>
    public required TimeSpan TotalElapsed { get; init; }

    /// <summary>The state visit histogram (state → visit count).</summary>
    public IReadOnlyDictionary<string, int>? StateVisitHistogram { get; init; }

    /// <summary>Healthy status factory.</summary>
    public static CircuitBreakerStatus Healthy(int transitions, TimeSpan timeInState, int stateVisits, TimeSpan totalElapsed) => new()
    {
        IsBroken = false,
        TotalTransitions = transitions,
        TimeInCurrentState = timeInState,
        CurrentStateVisitCount = stateVisits,
        TotalElapsed = totalElapsed
    };
}
