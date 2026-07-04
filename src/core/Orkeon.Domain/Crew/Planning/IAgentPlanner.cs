using Orkeon.Domain.Common;
using Orkeon.Domain.Task;

namespace Orkeon.Domain.Crew.Planning;

/// <summary>
/// Interface for agent planning capabilities.
/// </summary>
public interface IAgentPlanner
{
    /// <summary>
    /// Creates a plan for executing a task.
    /// </summary>
    /// <param name="task">The task to plan for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The task execution plan.</returns>
    System.Threading.Tasks.Task<TaskPlan> CreatePlanAsync(CrewTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refines an existing plan based on feedback.
    /// </summary>
    /// <param name="plan">The plan to refine.</param>
    /// <param name="feedback">The feedback to incorporate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The refined plan.</returns>
    System.Threading.Tasks.Task<TaskPlan> RefinePlanAsync(TaskPlan plan, PlanFeedback feedback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a plan.
    /// </summary>
    /// <param name="plan">The plan to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    System.Threading.Tasks.Task<PlanValidationResult> ValidatePlanAsync(TaskPlan plan, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a plan for executing a task.
/// </summary>
public sealed record TaskPlan
{
    /// <summary>Gets the plan identifier.</summary>
    public TaskPlanId Id { get; init; } = TaskPlanId.Create();
    /// <summary>Gets the task identifier this plan is for.</summary>
    public TaskId TaskId { get; init; } = TaskId.Create();
    /// <summary>Gets the plan steps.</summary>
    public IReadOnlyList<PlanStep> Steps { get; init; } = [];
    /// <summary>Gets additional context for the plan.</summary>
    public Dictionary<string, object> Context { get; init; } = [];
    /// <summary>Gets the estimated duration to execute the plan.</summary>
    public TimeSpan EstimatedDuration { get; init; }
    /// <summary>Gets the confidence score for this plan (0.0 to 1.0).</summary>
    public double ConfidenceScore { get; init; }
}

/// <summary>
/// Represents a single step in a plan.
/// </summary>
public sealed record PlanStep
{
    /// <summary>Gets the step identifier.</summary>
    public PlanStepId Id { get; init; } = PlanStepId.Create();
    /// <summary>Gets the action to perform.</summary>
    public string Action { get; init; } = string.Empty;
    /// <summary>Gets the step description.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the step parameters.</summary>
    public Dictionary<string, object> Parameters { get; init; } = [];
    /// <summary>Gets the step dependency identifiers.</summary>
    public IReadOnlyList<PlanStepId> Dependencies { get; init; } = [];
    /// <summary>Gets the estimated duration for this step.</summary>
    public TimeSpan EstimatedDuration { get; init; }
}

/// <summary>
/// Feedback on a plan execution.
/// </summary>
public sealed record PlanFeedback
{
    /// <summary>Gets the plan identifier.</summary>
    public TaskPlanId PlanId { get; init; } = TaskPlanId.Create();
    /// <summary>Gets whether the plan succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the completed step identifiers.</summary>
    public IReadOnlyList<PlanStepId> CompletedSteps { get; init; } = [];
    /// <summary>Gets the failed step identifiers.</summary>
    public IReadOnlyList<PlanStepId> FailedSteps { get; init; } = [];
    /// <summary>Gets per-step feedback messages.</summary>
    public IReadOnlyDictionary<string, string> StepFeedback { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Result of plan validation.
/// </summary>
public sealed record PlanValidationResult
{
    /// <summary>Gets whether the plan is valid.</summary>
    public bool IsValid { get; init; }
    /// <summary>Gets the list of validation errors.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
    /// <summary>Gets the list of validation warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
