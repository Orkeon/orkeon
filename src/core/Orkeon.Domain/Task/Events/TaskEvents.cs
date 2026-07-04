using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Domain.Task.Events;

/// <summary>
/// Event raised when a new task is created.
/// </summary>
public sealed record TaskCreatedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the task description.</summary>
    public required TaskDescription Description { get; init; }
}

/// <summary>
/// Event raised when a task is assigned to an agent.
/// </summary>
public sealed record TaskAssignedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
}

/// <summary>
/// Event raised when a task status changes.
/// </summary>
public sealed record TaskStatusChangedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the old status.</summary>
    public required TaskStatus OldStatus { get; init; }
    /// <summary>Gets the new status.</summary>
    public required TaskStatus NewStatus { get; init; }
    /// <summary>Gets the optional reason for the status change.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Event raised when a task is started.
/// </summary>
public sealed record TaskStartedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
}

/// <summary>
/// Event raised when a task is completed.
/// </summary>
public sealed record TaskCompletedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the task output.</summary>
    public required TaskOutput Output { get; init; }
    /// <summary>Gets the task execution duration.</summary>
    public required TimeSpan Duration { get; init; }
}

/// <summary>
/// Event raised when a task fails.
/// </summary>
public sealed record TaskFailedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the agent identifier if assigned.</summary>
    public AgentId? AgentId { get; init; }
    /// <summary>Gets the error message.</summary>
    public required string ErrorMessage { get; init; }
    /// <summary>Gets the exception if available.</summary>
    public Exception? Exception { get; init; }
}

/// <summary>
/// Event raised when a task is cancelled.
/// </summary>
public sealed record TaskCancelledEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the cancellation reason.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Event raised when task dependencies are updated.
/// </summary>
public sealed record TaskDependenciesUpdatedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the added dependencies.</summary>
    public required IReadOnlyList<TaskId> AddedDependencies { get; init; }
    /// <summary>Gets the removed dependencies.</summary>
    public required IReadOnlyList<TaskId> RemovedDependencies { get; init; }
}

/// <summary>
/// Event raised when a task is blocked by dependencies.
/// </summary>
public sealed record TaskBlockedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the blocking task identifiers.</summary>
    public required IReadOnlyList<TaskId> BlockingTasks { get; init; }
}

/// <summary>
/// Event raised when a task is unblocked.
/// </summary>
public sealed record TaskUnblockedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
}
