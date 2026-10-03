namespace Orkeon.Application.Services.StateManagement;

/// <summary>
/// Result of a state transition operation.
/// </summary>
public record StateTransitionResult<TState>(
    TState FromState,
    TState ToState,
    Enum Event,
    bool IsValid,
    string? ErrorMessage = null)
    where TState : notnull
{
    /// <summary>
    /// Gets or sets a value indicating whether state changed.
    /// </summary>
    public bool StateChanged => !FromState.Equals(ToState);
}
