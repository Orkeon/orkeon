using Orkeon.Domain.Common;

namespace Orkeon.Domain.HumanInput.ValueObjects;

/// <summary>
/// Value object carrying structured context for human input request events.
/// Replaces <c>Dictionary&lt;string, object&gt;</c> in event payloads.
/// </summary>
public sealed record HumanInputEventContext : ValueObjectRecord
{
    /// <summary>Gets the analysis or task type context.</summary>
    public string? AnalysisType { get; init; }

    /// <summary>Gets the priority level.</summary>
    public string? Priority { get; init; }

    /// <summary>Gets the task description providing context for the request.</summary>
    public string? TaskDescription { get; init; }

    /// <summary>Gets the agent role providing context for the request.</summary>
    public string? AgentRole { get; init; }

    /// <summary>Gets any additional notes.</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Creates a new <see cref="HumanInputEventContext"/>.
    /// </summary>
    public static HumanInputEventContext Create(
        string? analysisType = null,
        string? priority = null,
        string? taskDescription = null,
        string? agentRole = null,
        string? notes = null)
    {
        if (analysisType is not null && string.IsNullOrWhiteSpace(analysisType))
            throw new ArgumentException("AnalysisType, when provided, cannot be empty or whitespace.", nameof(analysisType));

        if (priority is not null && string.IsNullOrWhiteSpace(priority))
            throw new ArgumentException("Priority, when provided, cannot be empty or whitespace.", nameof(priority));

        if (taskDescription is not null && string.IsNullOrWhiteSpace(taskDescription))
            throw new ArgumentException("TaskDescription, when provided, cannot be empty or whitespace.", nameof(taskDescription));

        if (agentRole is not null && string.IsNullOrWhiteSpace(agentRole))
            throw new ArgumentException("AgentRole, when provided, cannot be empty or whitespace.", nameof(agentRole));

        return new()
        {
            AnalysisType = analysisType,
            Priority = priority,
            TaskDescription = taskDescription,
            AgentRole = agentRole,
            Notes = notes
        };
    }

    /// <summary>Gets an empty context.</summary>
    public static HumanInputEventContext Empty { get; } = new();
}
