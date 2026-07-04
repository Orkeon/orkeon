using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Interfaces.Services
{
    /// <summary>
    /// Core execution orchestration interface focused solely on task execution flow.
    /// </summary>
    public interface IExecutionOrchestrator
    {
        /// <summary>
        /// Executes a task for an agent without side effects.
        /// </summary>
        System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreAsync(
            DomainAgent agent,
            CrewTask task,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Plans task execution steps without executing them.
        /// </summary>
        System.Threading.Tasks.Task<TaskExecutionPlan> PlanExecutionAsync(
            DomainAgent agent,
            CrewTask task,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates if an agent can execute a specific task.
        /// </summary>
        System.Threading.Tasks.Task<ValidationResult> ValidateExecutionAsync(
            DomainAgent agent,
            CrewTask task,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Maps execution contexts between layers.
        /// </summary>
        Domain.Task.ValueObjects.SimpleTaskExecutionContext MapExecutionContext(
            Context.SimpleExecutionContext applicationContext,
            DomainAgent agent);
    }

    /// <summary>
    /// Validation result for task execution.
    /// </summary>
    public record ValidationResult(
        bool CanExecute,
        string? Reason = null,
        IReadOnlyList<string>? MissingCapabilities = null);
}
