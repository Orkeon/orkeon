namespace Orkeon.Domain.Common.StateMachine;

/// <summary>
/// Describes the outcome of a state transition.
/// </summary>
public sealed record TransitionResult<TState, TEvent>
    where TState : notnull
    where TEvent : notnull
{
    /// <summary>The state before the transition.</summary>
    public required TState FromState { get; init; }

    /// <summary>The state after the transition.</summary>
    public required TState ToState { get; init; }

    /// <summary>The event that triggered the transition.</summary>
    public required TEvent Trigger { get; init; }

    /// <summary>Whether the state actually changed.</summary>
    public bool StateChanged => !FromState.Equals(ToState);

    /// <summary>The ordinal number of this transition in the machine's lifetime.</summary>
    public required int TransitionOrdinal { get; init; }

    /// <summary>Timestamp of the transition.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
