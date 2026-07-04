namespace Orkeon.Domain.Common.StateMachine;

/// <summary>
/// Read-only view of a state machine for observation and querying.
/// </summary>
/// <typeparam name="TState">The state type (typically an enum or value object).</typeparam>
/// <typeparam name="TEvent">The event/trigger type.</typeparam>
public interface IStateMachine<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    /// <summary>Gets the current state.</summary>
    TState CurrentState { get; }

    /// <summary>Gets whether the machine is in a terminal (final) state.</summary>
    bool IsTerminal { get; }

    /// <summary>Gets the total number of transitions executed so far.</summary>
    int TransitionCount { get; }

    /// <summary>Gets whether the circuit breaker has tripped.</summary>
    bool IsCircuitBroken { get; }

    /// <summary>Gets the circuit breaker status details.</summary>
    CircuitBreakerStatus CircuitStatus { get; }

    /// <summary>Gets the events that are permissible from the current state.</summary>
    IReadOnlyList<TEvent> GetPermittedEvents();

    /// <summary>Checks whether a specific event can fire from the current state.</summary>
    bool CanFire(TEvent trigger);
}

/// <summary>
/// Mutable state machine that can process events and transition between states.
/// </summary>
public interface IMutableStateMachine<TState, TEvent> : IStateMachine<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    /// <summary>
    /// Fires an event, causing a state transition if the event is permitted.
    /// </summary>
    /// <returns>The transition result describing what happened.</returns>
    /// <exception cref="CircuitBrokenException">Thrown when the circuit breaker has tripped.</exception>
    /// <exception cref="InvalidTransitionException{TState, TEvent}">Thrown when no transition is defined for this state+event.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1030", Justification = "FSM verb: 'Fire' triggers a state transition and returns a result; it is not a CLR event.")]
    TransitionResult<TState, TEvent> Fire(TEvent trigger);

    /// <summary>
    /// Fires an event with a context object available to guards and actions.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1030", Justification = "FSM verb: 'Fire' triggers a state transition and returns a result; it is not a CLR event.")]
    TransitionResult<TState, TEvent> Fire<TContext>(TEvent trigger, TContext context);

    /// <summary>
    /// Attempts to fire an event. Returns false if the transition is not permitted
    /// (does not throw for invalid transitions or broken circuit).
    /// </summary>
    bool TryFire(TEvent trigger, out TransitionResult<TState, TEvent>? result);

    /// <summary>
    /// Attempts to fire an event with context.
    /// </summary>
    bool TryFire<TContext>(TEvent trigger, TContext context, out TransitionResult<TState, TEvent>? result);

    /// <summary>
    /// Resets the circuit breaker, allowing transitions to resume.
    /// Only effective when the circuit is in the broken state.
    /// </summary>
    void ResetCircuitBreaker();

    /// <summary>
    /// Event raised after each successful transition.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003", Justification = "Event payload is a domain value type (TransitionResult); not modeled as EventArgs by design.")]
    event EventHandler<TransitionResult<TState, TEvent>>? OnTransition;

    /// <summary>
    /// Event raised when the circuit breaker trips.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003", Justification = "Event payload is a domain value type (CircuitBreakerStatus); not modeled as EventArgs by design.")]
    event EventHandler<CircuitBreakerStatus>? OnCircuitBroken;
}
