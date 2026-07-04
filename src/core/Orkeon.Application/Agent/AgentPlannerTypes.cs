using Orkeon.Domain.Crew.Planning;
using Orkeon.Domain.Constants.Task;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Application.Agent;

/// <summary>
/// Configuration for planning functionality.
/// </summary>
public record PlanningConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether enable planning.
    /// </summary>
    public bool EnablePlanning { get; init; } = true;
    /// <summary>Gets or sets the max retries.</summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;
    /// <summary>Gets or sets the timeout.</summary>
    public TimeSpan Timeout { get; init; } = TaskDefaults.DefaultOperationTimeout;
}


/// <summary>
/// Context for planning operations.
/// </summary>
public record PlanningContext(
    string TaskDescription,
    string ExpectedOutput,
    IReadOnlyList<string> Tools,
    string AgentRole,
    string AgentBackstory,
    string? AdditionalContext);

/// <summary>
/// Represents a planning step.
/// </summary>
public record PlanningStep(
    int StepNumber,
    string Title,
    string Description,
    IReadOnlyList<string> Actions);

/// <summary>
/// Result of planning operations.
/// </summary>
public record PlanningResult(
    IReadOnlyList<TaskPlan> TaskPlans,
    TimeSpan PlanningDuration,
    bool Success);

