using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Crew.Events;

/// <summary>
/// Event raised when a new crew is created.
/// </summary>
public sealed record CrewCreatedEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the crew goal.</summary>
    public required string Goal { get; init; }
    /// <summary>Gets the process type.</summary>
    public required ProcessType ProcessType { get; init; }
}

/// <summary>
/// Event raised when an agent joins a crew.
/// </summary>
public sealed record AgentJoinedCrewEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
}

/// <summary>
/// Event raised when an agent leaves a crew.
/// </summary>
public sealed record AgentLeftCrewEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the reason for leaving.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Event raised when a task is added to a crew.
/// </summary>
public sealed record TaskAddedToCrewEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
}

/// <summary>
/// Event raised when a task is removed from a crew.
/// </summary>
public sealed record TaskRemovedFromCrewEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the reason for removal.</summary>
    public required string Reason { get; init; }
}

/// <summary>
/// Event raised when crew execution starts.
/// </summary>
public sealed record CrewExecutionStartedEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the process identifier.</summary>
    public required ProcessId ProcessId { get; init; }
}

/// <summary>
/// Event raised when crew execution completes successfully.
/// </summary>
public sealed record CrewExecutionCompletedEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the process identifier.</summary>
    public required ProcessId ProcessId { get; init; }
    /// <summary>Gets the execution duration.</summary>
    public required TimeSpan Duration { get; init; }
    /// <summary>Gets the number of completed tasks.</summary>
    public required int CompletedTasks { get; init; }
    /// <summary>Gets the number of failed tasks.</summary>
    public required int FailedTasks { get; init; }
}

/// <summary>
/// Event raised when crew execution fails.
/// </summary>
public sealed record CrewExecutionFailedEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the process identifier.</summary>
    public required ProcessId ProcessId { get; init; }
    /// <summary>Gets the failure reason.</summary>
    public required string Reason { get; init; }
    /// <summary>Gets the exception if available.</summary>
    public Exception? Exception { get; init; }
}

/// <summary>
/// Event raised when crew process type is changed.
/// </summary>
public sealed record CrewProcessTypeChangedEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the old process type.</summary>
    public required ProcessType OldProcessType { get; init; }
    /// <summary>Gets the new process type.</summary>
    public required ProcessType NewProcessType { get; init; }
}

/// <summary>
/// Event raised when crew goal is updated.
/// </summary>
public sealed record CrewGoalUpdatedEvent : DomainEvent
{
    /// <summary>Gets the crew identifier.</summary>
    public required CrewId CrewId { get; init; }
    /// <summary>Gets the old goal.</summary>
    public required string OldGoal { get; init; }
    /// <summary>Gets the new goal.</summary>
    public required string NewGoal { get; init; }
}
