using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput.ValueObjects;
using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.HumanInput.Events;

/// <summary>
/// Event raised when human input is requested during task execution.
/// </summary>
public sealed record HumanInputRequestedEvent : DomainEvent
{
    /// <summary>Gets the agent identifier requesting input.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the task identifier for which input is needed.</summary>
    public required TaskId TaskId { get; init; }
    /// <summary>Gets the prompt shown to the human.</summary>
    public required string Prompt { get; init; }
    /// <summary>Gets the expected input type (e.g. "text").</summary>
    public string InputType { get; init; } = "text";
    /// <summary>Gets the optional timeout for the human response.</summary>
    public TimeSpan? Timeout { get; init; }
    /// <summary>Gets the structured context for the human input request.</summary>
    public HumanInputEventContext Context { get; init; } = HumanInputEventContext.Empty;
}
