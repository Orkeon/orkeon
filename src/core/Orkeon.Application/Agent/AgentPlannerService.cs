using Orkeon.Domain.Crew.Planning;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Task;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Application.Agent;

/// <summary>
/// Deterministic stub planner — emits a fixed 4-step plan (analyze, plan, execute, review)
/// with a hard-coded confidence score of 0.8 for every task; a genuine LLM-backed planner
/// is a planned feature.
/// </summary>
/// <remarks>
/// This is the default <see cref="IAgentPlanner"/> registration so that
/// <c>ExecutionOrchestrator</c> always has a planner available. Plan creation does not
/// inspect the task content; only <see cref="RefinePlanAsync"/> and
/// <see cref="ValidatePlanAsync"/> implement real logic (feedback-driven step retry and
/// structural validation). Each <see cref="CreatePlanAsync"/> call emits a Debug log
/// stating that the stub is in use.
/// </remarks>
public partial class AgentPlannerService : IAgentPlanner
{
    private readonly ILogger<AgentPlannerService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AgentPlannerService"/>.
    /// </summary>
    public AgentPlannerService(ILogger<AgentPlannerService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Creates a plan for executing a task. Stub behavior: returns the same fixed
    /// 4-step plan regardless of the task content (see the class remarks).
    /// </summary>
    public System.Threading.Tasks.Task<TaskPlan> CreatePlanAsync(CrewTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        LogCreatingPlan(task.Id);
        LogStubPlanner(task.Id);

        // Create basic steps for the task
        var steps = new List<PlanStep>
        {
            new PlanStep
            {
                Action = "Analyze Task Requirements",
                Description = "Review and understand the task description and expected output",
                EstimatedDuration = TimeSpan.FromMinutes(5)
            },
            new PlanStep
            {
                Action = "Plan Approach",
                Description = "Determine the best approach to complete the task",
                EstimatedDuration = TimeSpan.FromMinutes(5)
            },
            new PlanStep
            {
                Action = "Execute Task",
                Description = "Perform the actual work required by the task",
                EstimatedDuration = TimeSpan.FromMinutes(15)
            },
            new PlanStep
            {
                Action = "Review and Finalize",
                Description = "Review results and ensure they meet the expected output",
                EstimatedDuration = TimeSpan.FromMinutes(5)
            }
        };

        // Synchronous stub: returns a fixed 4-step plan with a hard-coded
        // confidence. Kept synchronous (no async/await) so the absence of any
        // real asynchronous work is visible — a genuine LLM-backed planner is
        // tracked separately (maturity plan).
        return System.Threading.Tasks.Task.FromResult(new TaskPlan
        {
            TaskId = task.Id,
            Steps = steps,
            EstimatedDuration = CrewDefaults.DefaultExecutionTimeout,
            ConfidenceScore = 0.8
        });
    }

    /// <summary>
    /// Refines an existing plan based on feedback.
    /// </summary>
    public System.Threading.Tasks.Task<TaskPlan> RefinePlanAsync(TaskPlan plan, PlanFeedback feedback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(feedback);
        LogRefiningPlan(plan.Id);

        // If the plan was successful, no refinement needed
        if (feedback.Success)
        {
            return System.Threading.Tasks.Task.FromResult(plan);
        }

        // Create a new plan incorporating feedback
        var refinedSteps = new List<PlanStep>();

        foreach (var step in plan.Steps)
        {
            if (feedback.FailedSteps.Contains(step.Id))
            {
                // Add retry logic for failed steps
                refinedSteps.Add(new PlanStep
                {
                    Action = $"Retry: {step.Action}",
                    Description = $"{step.Description} (Adjusted based on previous failure)",
                    Parameters = step.Parameters,
                    Dependencies = step.Dependencies,
                    EstimatedDuration = step.EstimatedDuration
                });
            }
            else if (!feedback.CompletedSteps.Contains(step.Id))
            {
                // Keep pending steps as is
                refinedSteps.Add(step);
            }
        }

        var degradedConfidence = ConfidenceScore.From(plan.ConfidenceScore).Degrade();

        return System.Threading.Tasks.Task.FromResult(plan with
        {
            Steps = refinedSteps,
            ConfidenceScore = degradedConfidence.Value // Domain VO encapsulates degradation rule
        });
    }

    /// <summary>
    /// Validates a plan.
    /// </summary>
    public System.Threading.Tasks.Task<PlanValidationResult> ValidatePlanAsync(TaskPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        LogValidatingPlan(plan.Id);

        var errors = new List<string>();
        var warnings = new List<string>();

        // Basic validation
        if (string.IsNullOrEmpty(plan.TaskId))
        {
            errors.Add("Plan must be associated with a task");
        }

        if (plan.Steps.Count == 0)
        {
            errors.Add("Plan must contain at least one step");
        }

        // Validate each step
        foreach (var step in plan.Steps)
        {
            if (string.IsNullOrEmpty(step.Action))
            {
                errors.Add($"Step {step.Id} must have an action");
            }

            if (string.IsNullOrEmpty(step.Description))
            {
                warnings.Add($"Step {step.Id} should have a description");
            }
        }

        // Check for circular dependencies
        var stepsList = plan.Steps.ToList();
        var circularSteps = stepsList
            .Where(step => HasCircularDependency(step, stepsList, []))
            .Select(step => step.Id);
        foreach (var stepId in circularSteps)
        {
            errors.Add($"Circular dependency detected for step {stepId}");
        }

        return System.Threading.Tasks.Task.FromResult(new PlanValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        });
    }

    private static bool HasCircularDependency(PlanStep step, List<PlanStep> allSteps, HashSet<string> visited)
    {
        if (visited.Contains(step.Id))
        {
            return true;
        }

        visited.Add(step.Id);

        foreach (var depId in step.Dependencies)
        {
            var depStep = allSteps.FirstOrDefault(s => s.Id == depId);
            if (depStep != null && HasCircularDependency(depStep, allSteps, visited))
            {
                return true;
            }
        }

        visited.Remove(step.Id);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating plan for task {TaskId}")]
    private partial void LogCreatingPlan(string taskId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "AgentPlannerService is a deterministic stub planner — emitting the fixed 4-step plan for task {TaskId}; an LLM-backed planner is a planned feature")]
    private partial void LogStubPlanner(string taskId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refining plan {PlanId} based on feedback")]
    private partial void LogRefiningPlan(string planId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Validating plan {PlanId}")]
    private partial void LogValidatingPlan(string planId);
}
