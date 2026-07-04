using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.Task.Events;

/// <summary>
/// Event raised when a task context is updated.
/// </summary>
public sealed record TaskContextUpdatedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the context type name.</summary>
    public required string ContextType { get; init; }
    /// <summary>Gets the serialized state before the update.</summary>
    public required string BeforeState { get; init; }
    /// <summary>Gets the serialized state after the update.</summary>
    public required string AfterState { get; init; }
}
