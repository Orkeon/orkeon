namespace Orkeon.Domain.Common.StateMachine;

/// <summary>
/// Defines a single transition in the state machine: from a source state,
/// on a given event, to a target state, with optional guard and actions.
/// </summary>
internal sealed class TransitionDefinition<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    /// <summary>Source state.</summary>
    public required TState From { get; init; }

    /// <summary>Triggering event.</summary>
    public required TEvent Trigger { get; init; }

    /// <summary>Target state.</summary>
    public required TState To { get; init; }

    /// <summary>
    /// Optional guard predicate. Transition only fires if this returns true.
    /// The object parameter is the optional context passed to Fire{TContext}.
    /// </summary>
    public Func<object?, bool>? Guard { get; init; }

    /// <summary>
    /// Optional action executed after the transition succeeds.
    /// Parameters: (fromState, toState, context).
    /// </summary>
    public Action<TState, TState, object?>? OnTransition { get; init; }

    /// <summary>
    /// Human-readable description of the guard for diagnostics.
    /// </summary>
    public string? GuardDescription { get; init; }
}
