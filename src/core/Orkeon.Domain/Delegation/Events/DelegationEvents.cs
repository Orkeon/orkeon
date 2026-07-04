using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.Delegation.Events;

/// <summary>
/// Event raised when a task is delegated from one agent to another.
/// </summary>
public sealed record TaskDelegatedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the delegating agent identifier.</summary>
    public required AgentId FromAgentId { get; init; }
    /// <summary>Gets the target agent identifier.</summary>
    public required AgentId ToAgentId { get; init; }
    /// <summary>Gets the delegation context.</summary>
    public required string Context { get; init; }
}

/// <summary>
/// Groups the outcome details of a delegation.
/// </summary>
public sealed record DelegationOutcome(
    bool Success,
    string Output,
    TimeSpan ExecutionTime,
    string? Error = null);

/// <summary>
/// Event raised when a delegation is completed.
/// </summary>
public sealed record DelegationCompletedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the delegating agent identifier.</summary>
    public required AgentId FromAgentId { get; init; }
    /// <summary>Gets the target agent identifier.</summary>
    public required AgentId ToAgentId { get; init; }
    /// <summary>Gets the delegation outcome.</summary>
    public required DelegationOutcome Outcome { get; init; }

    // Backward-compatible accessors
    /// <summary>Whether the delegation succeeded.</summary>
    public bool Success => Outcome.Success;
    /// <summary>The delegation output.</summary>
    public string Output => Outcome.Output;
    /// <summary>The delegation execution time.</summary>
    public TimeSpan ExecutionTime => Outcome.ExecutionTime;
    /// <summary>The error message if delegation failed.</summary>
    public string? Error => Outcome.Error;
}

/// <summary>
/// Event raised when a delegation request is queued.
/// </summary>
public sealed record DelegationQueuedEvent : DomainEvent
{
    /// <summary>Gets the task identifier.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the delegating agent identifier.</summary>
    public required AgentId FromAgentId { get; init; }
    /// <summary>Gets the target agent identifier.</summary>
    public required AgentId ToAgentId { get; init; }
    /// <summary>Gets when the delegation was queued.</summary>
    public required DateTime QueuedAt { get; init; }
}

/// <summary>
/// Event raised when an agent is registered for delegation.
/// </summary>
public sealed record AgentRegisteredForDelegationEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the agent's role.</summary>
    public required string Role { get; init; }
}

/// <summary>
/// Event raised when an agent is unregistered from delegation.
/// </summary>
public sealed record AgentUnregisteredFromDelegationEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
}
