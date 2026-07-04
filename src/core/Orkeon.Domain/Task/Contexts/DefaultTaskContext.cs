namespace Orkeon.Domain.Task.Contexts;

/// <summary>
/// Default task context using a dictionary for flexible key-value storage.
/// Used by <see cref="CrewTask"/> when no typed context is needed.
/// </summary>
public sealed class DefaultTaskContext
{
    /// <summary>
    /// Gets the context values dictionary.
    /// </summary>
    public Dictionary<string, object> Values { get; } = [];
}
