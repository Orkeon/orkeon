using Orkeon.Domain.Crew.Planning;

namespace Orkeon.Application.Agent;

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

