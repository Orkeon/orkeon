using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Agent.Events;

/// <summary>
/// Event raised when a new agent is created.
/// </summary>
public sealed record AgentCreatedEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the agent's role.</summary>
    public required AgentRole Role { get; init; }
    /// <summary>Gets the agent's goal.</summary>
    public required AgentGoal Goal { get; init; }
}

/// <summary>
/// Event raised when an agent is assigned to a task.
/// </summary>
public sealed record AgentAssignedToTaskEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
}

/// <summary>
/// Event raised when an agent starts executing a task.
/// </summary>
public sealed record AgentStartedTaskEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
}

/// <summary>
/// Event raised when an agent completes a task.
/// </summary>
public sealed record AgentCompletedTaskEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the task output.</summary>
    public required TaskOutput Output { get; init; }
}

/// <summary>
/// Event raised when an agent fails to complete a task.
/// </summary>
public sealed record AgentFailedTaskEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the failure reason.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Event raised when an agent's capabilities are updated.
/// </summary>
public sealed record AgentCapabilitiesUpdatedEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the tools that were added.</summary>
    public required IReadOnlyList<string> AddedTools { get; init; }
    /// <summary>Gets the tools that were removed.</summary>
    public required IReadOnlyList<string> RemovedTools { get; init; }
}

/// <summary>
/// Event raised when an agent is killed (emergency stop).
/// </summary>
public sealed record AgentKilledEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the kill reason.</summary>
    public required string Reason { get; init; }
}
