using System.Text;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.Crew.Interfaces;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Context for planning operations, avoiding direct Crew AR reference.
/// </summary>
public sealed record PlanningContext(
    CrewId CrewId,
    CrewGoal Goal,
    IReadOnlyList<AgentId> Agents);

/// <summary>
/// Responsible for creating execution plans for crews.
/// Delegates parsing of LLM responses to <see cref="IExecutionPlanParser"/>
/// to keep the Domain layer free from serialization concerns.
/// </summary>
public sealed class CrewPlanner
{
    private readonly ILlmProvider _planningLlm;
    private readonly IExecutionPlanParser _parser;
    private readonly IPlanningStrategy? _strategy;

    private CrewPlanner(ILlmProvider planningLlm, IExecutionPlanParser parser, IPlanningStrategy? strategy = null)
    {
        ArgumentNullException.ThrowIfNull(planningLlm);
        _planningLlm = planningLlm;
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
        _strategy = strategy;
    }

    /// <summary>Creates a new instance of <see cref="CrewPlanner"/>.</summary>
    /// <param name="planningLlm">The LLM provider to use for LLM-based planning.</param>
    /// <param name="parser">The parser used to convert LLM responses into execution plans.</param>
    /// <param name="strategy">
    /// Optional planning strategy. When provided, plan creation is delegated to the strategy
    /// instead of the LLM; the resulting plan still goes through the same validation
    /// (task completeness, circular dependencies).
    /// </param>
    public static CrewPlanner Create(ILlmProvider planningLlm, IExecutionPlanParser parser, IPlanningStrategy? strategy = null)
        => new(planningLlm, parser, strategy);

    /// <summary>
    /// Creates an execution plan for the crew.
    /// </summary>
    public System.Threading.Tasks.Task<ExecutionPlan> CreatePlanAsync(
        PlanningContext context,
        IReadOnlyList<TaskId> tasks,
        CrewInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(input);
        return CreatePlanCoreAsync(context, tasks, input);
    }

    private async System.Threading.Tasks.Task<ExecutionPlan> CreatePlanCoreAsync(
        PlanningContext context,
        IReadOnlyList<TaskId> tasks,
        CrewInput input)
    {
        // A provided strategy takes precedence over the LLM-based flow; both
        // paths produce a plan that goes through the same validation below.
        var plan = _strategy != null
            ? await _strategy.CreatePlanAsync(context, tasks, input).ConfigureAwait(false)
            : await CreatePlanWithLlmAsync(context, tasks, input).ConfigureAwait(false);

        if (plan == null)
            throw new InvalidOperationException("Failed to create execution plan");

        // Validate the plan
        var validation = ValidatePlan(plan, tasks);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Invalid plan: {string.Join(", ", validation.Errors)}");

        return plan;
    }

    private async System.Threading.Tasks.Task<ExecutionPlan?> CreatePlanWithLlmAsync(
        PlanningContext context,
        IReadOnlyList<TaskId> tasks,
        CrewInput input)
    {
        // Build the planning prompt
        var planningPrompt = BuildPlanningPrompt(context, tasks, input);

        // Call the LLM to generate the plan
        var llmResponse = await _planningLlm.GenerateAsync(
            planningPrompt,
            LlmConfig.Default() with { Temperature = 0.3f }).ConfigureAwait(false); // Low temperature for consistency

        // Delegate parsing to the injected abstraction
        return await _parser.ParseAsync(llmResponse.Content, tasks, context.Agents).ConfigureAwait(false);
    }

    private static string BuildPlanningPrompt(PlanningContext context, IReadOnlyList<TaskId> tasks, CrewInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(input);

        var prompt = new StringBuilder();
        prompt.AppendLine(FormattableString.Invariant($"You are planning the execution for crew: {context.Goal}"));
        prompt.AppendLine(FormattableString.Invariant($"Input: {input.InitialContext}"));
        prompt.AppendLine("\nAvailable agents:");

        foreach (var agentId in context.Agents)
        {
            // Note: In production, load agents from the repository
            prompt.AppendLine(FormattableString.Invariant($"- Agent {agentId}"));
        }

        prompt.AppendLine("\nTasks to plan:");
        foreach (var task in tasks)
        {
            // Note: In production, load tasks from the repository
            prompt.AppendLine(FormattableString.Invariant($"- Task {task}"));
        }

        prompt.AppendLine("\nCreate an optimal execution plan considering:");
        prompt.AppendLine("1. Task dependencies");
        prompt.AppendLine("2. Agent capabilities");
        prompt.AppendLine("3. Parallel execution opportunities");
        prompt.AppendLine("4. Resource optimization");

        prompt.AppendLine("\nReturn the plan in the following format:");
        prompt.AppendLine("TASK: <task_id>");
        prompt.AppendLine("ORDER: <execution_order>");
        prompt.AppendLine("AGENT: <agent_id>");
        prompt.AppendLine("DEPENDENCIES: <comma_separated_task_ids>");
        prompt.AppendLine("PARALLEL_GROUP: <group_number>");
        prompt.AppendLine("INSTRUCTIONS: <special_instructions>");
        prompt.AppendLine("---");

        return prompt.ToString();
    }

    private static ValidationResult ValidatePlan(ExecutionPlan plan, IReadOnlyList<TaskId> tasks)
    {
        var result = new ValidationResult();

        // Check all tasks are included
        foreach (var task in tasks)
        {
            if (!plan.Tasks.Any(t => t.TaskId == task))
            {
                result.AddError("Tasks", $"Task {task} is missing from the plan");
            }
        }

        // Check for circular dependencies
        foreach (var plannedTask in plan.Tasks.Where(pt => HasCircularDependency(pt, plan)))
        {
            result.AddError("Dependencies", $"Task {plannedTask.TaskId} has circular dependencies");
        }

        return result;
    }

    private static bool HasCircularDependency(PlannedTask task, ExecutionPlan plan)
    {
        var visited = new HashSet<TaskId>();
        var recursionStack = new HashSet<TaskId>();

        return HasCircularDependencyHelper(task.TaskId, plan, visited, recursionStack);
    }

    private static bool HasCircularDependencyHelper(
        TaskId taskId,
        ExecutionPlan plan,
        HashSet<TaskId> visited,
        HashSet<TaskId> recursionStack)
    {
        visited.Add(taskId);
        recursionStack.Add(taskId);

        var task = plan.Tasks.FirstOrDefault(t => t.TaskId == taskId);
        if (task != null)
        {
            foreach (var dependency in task.Dependencies)
            {
                if (!visited.Contains(dependency))
                {
                    if (HasCircularDependencyHelper(dependency, plan, visited, recursionStack))
                        return true;
                }
                else if (recursionStack.Contains(dependency))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(taskId);
        return false;
    }

}

/// <summary>
/// Interface for planning strategies.
/// </summary>
public interface IPlanningStrategy
{
    /// <summary>Creates an execution plan for the given planning context and tasks.</summary>
    /// <param name="context">The planning context containing crew identity and agents.</param>
    /// <param name="tasks">The list of task identifiers to include in the plan.</param>
    /// <param name="input">The crew input context.</param>
    /// <returns>The resulting <see cref="ExecutionPlan"/>.</returns>
    System.Threading.Tasks.Task<ExecutionPlan> CreatePlanAsync(PlanningContext context, IReadOnlyList<TaskId> tasks, CrewInput input);
}

/// <summary>
/// Default planning strategy implementation.
/// </summary>
public sealed class DefaultPlanningStrategy : IPlanningStrategy
{
    /// <inheritdoc />
    public System.Threading.Tasks.Task<ExecutionPlan> CreatePlanAsync(PlanningContext context, IReadOnlyList<TaskId> tasks, CrewInput input)
    {
        // Simple sequential plan by default
        return System.Threading.Tasks.Task.FromResult(ExecutionPlan.Create(tasks));
    }
}
