using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Domain.Task;

/// <summary>
/// Repository interface for the <see cref="CrewTask"/> aggregate root.
/// <para>
/// <strong>Scope:</strong> This repository covers <see cref="CrewTask"/> only — the single
/// Task aggregate root that flows through the CQRS/Repository persistence pipeline.
/// </para>
/// <para>
/// The specialized task types (<see cref="CodeGenerationTask"/>, <see cref="AnalysisTask"/>,
/// <see cref="ResearchTask"/>) inherit from <see cref="CrewTaskBase{TContext}"/> and provide
/// type-safe context for domain-specific operations (code generation, analysis, research).
/// They are <strong>not</strong> separate aggregate roots requiring their own repositories;
/// they are domain models used within the execution layer for typed context and behaviour.
/// Task routing is handled via <c>TaskClassificationService</c> using heuristic matching
/// on type name, description, and expected output — not through the repository layer.
/// </para>
/// <para>
/// Design decision (R36): one repository per aggregate root. <see cref="CrewTask"/> is the
/// sole persistable task AR. If a specialized task type ever needs its own persistence
/// lifecycle, a dedicated repository interface should be introduced at that time.
/// </para>
/// </summary>
public interface ITaskRepository : ISpecificationRepository<CrewTask, TaskId>
{
    /// <summary>
    /// Gets multiple tasks by their identifiers.
    /// </summary>
    /// <param name="ids">The task identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of found tasks.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tasks assigned to a specific agent.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tasks assigned to the agent.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tasks by status.
    /// </summary>
    /// <param name="status">The task status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tasks with the specified status.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByStatusAsync(TaskStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tasks that depend on a specific task.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of dependent tasks.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ready-to-execute tasks (no blocking dependencies).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of ready tasks.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tasks by priority.
    /// </summary>
    /// <param name="priority">The task priority.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tasks with the specified priority.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new task and returns it.
    /// </summary>
    /// <param name="task">The task to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The added task.</returns>
    new System.Threading.Tasks.Task<CrewTask> AddAsync(CrewTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a task and reports success.
    /// </summary>
    /// <param name="id">The task identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    new System.Threading.Tasks.Task<bool> DeleteAsync(TaskId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a task is completed.
    /// </summary>
    /// <param name="id">The task identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if completed, false otherwise.</returns>
    System.Threading.Tasks.Task<bool> IsCompletedAsync(TaskId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tasks created within a date range.
    /// </summary>
    /// <param name="startDate">Start date.</param>
    /// <param name="endDate">End date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tasks created within the range.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<CrewTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
}
