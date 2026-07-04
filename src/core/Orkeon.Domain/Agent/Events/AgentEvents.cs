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
/// Event raised when an agent collaborates with another agent.
/// </summary>
public sealed record AgentCollaborationStartedEvent : DomainEvent
{
    /// <summary>Gets the initiator agent identifier.</summary>
    public required AgentId InitiatorId { get; init; }
    /// <summary>Gets the collaborator agent identifier.</summary>
    public required AgentId CollaboratorId { get; init; }
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the collaboration identifier.</summary>
    public required CollaborationId CollaborationId { get; init; }
}

/// <summary>
/// Event raised when an agent updates its memory.
/// </summary>
public sealed record AgentMemoryUpdatedEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the memory identifier.</summary>
    public required MemoryId MemoryId { get; init; }
    /// <summary>Gets the memory type.</summary>
    public required string MemoryType { get; init; }
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

/// <summary>
/// Event raised when an agent is spawned dynamically at runtime.
/// </summary>
public sealed record AgentSpawnedEvent : DomainEvent
{
    /// <summary>Gets the spawned agent identifier.</summary>
    public required AgentId SpawnedAgentId { get; init; }
    /// <summary>Gets the parent crew identifier.</summary>
    public required CrewId ParentCrewId { get; init; }
    /// <summary>Gets the requesting agent identifier (if applicable).</summary>
    public AgentId? RequestingAgentId { get; init; }
    /// <summary>Gets the spawned agent role.</summary>
    public required AgentRole Role { get; init; }
    /// <summary>Gets the spawned agent goal.</summary>
    public required AgentGoal Goal { get; init; }
    /// <summary>Gets the timestamp when the agent was spawned.</summary>
    public required DateTime SpawnTime { get; init; }
}
