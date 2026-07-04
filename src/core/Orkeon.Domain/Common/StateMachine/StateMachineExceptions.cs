namespace Orkeon.Domain.Common.StateMachine;

#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required

/// <summary>
/// Thrown when the circuit breaker has tripped and the machine cannot process events.
/// </summary>
public sealed class CircuitBrokenException : InvalidOperationException
{
    /// <summary>The circuit breaker status at the time of the exception.</summary>
    public CircuitBreakerStatus Status { get; }

    /// <summary>Creates a new circuit broken exception.</summary>
    public CircuitBrokenException(CircuitBreakerStatus status)
        : base($"Circuit breaker tripped: {status?.BrokenReason}")
    {
        ArgumentNullException.ThrowIfNull(status);
        Status = status;
    }

    /// <summary>Initializes a new instance of <see cref="CircuitBrokenException"/>.</summary>
    public CircuitBrokenException() { Status = null!; }

    /// <summary>Initializes a new instance of <see cref="CircuitBrokenException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public CircuitBrokenException(string message) : base(message) { Status = null!; }

    /// <summary>Initializes a new instance of <see cref="CircuitBrokenException"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CircuitBrokenException(string message, Exception innerException) : base(message, innerException) { Status = null!; }
}

/// <summary>
/// Thrown when an event is fired that has no transition defined from the current state.
/// </summary>
public sealed class InvalidTransitionException<TState, TEvent> : InvalidOperationException
    where TState : notnull
    where TEvent : notnull
{
    /// <summary>The state when the invalid transition was attempted.</summary>
    public TState CurrentState { get; }

    /// <summary>The event that was fired.</summary>
    public TEvent Trigger { get; }

    /// <summary>Creates a new invalid transition exception.</summary>
    public InvalidTransitionException(TState currentState, TEvent trigger)
        : base($"No transition defined from state '{currentState}' for event '{trigger}'")
    {
        CurrentState = currentState;
        Trigger = trigger;
    }

    /// <summary>Initializes a new instance of <see cref="InvalidTransitionException{TState, TEvent}"/>.</summary>
    public InvalidTransitionException() { CurrentState = default!; Trigger = default!; }

    /// <summary>Initializes a new instance of <see cref="InvalidTransitionException{TState, TEvent}"/>.</summary>
    /// <param name="message">The exception message.</param>
    public InvalidTransitionException(string message) : base(message) { CurrentState = default!; Trigger = default!; }

    /// <summary>Initializes a new instance of <see cref="InvalidTransitionException{TState, TEvent}"/> with an inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public InvalidTransitionException(string message, Exception innerException) : base(message, innerException) { CurrentState = default!; Trigger = default!; }
}

#pragma warning restore S3925
