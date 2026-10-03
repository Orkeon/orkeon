using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Represents an execution instance of a crew.
/// </summary>
public sealed class CrewExecution
{
    /// <summary>
    /// Gets the process identifier for this execution.
    /// </summary>
    public ProcessId ProcessId { get; }

    /// <summary>
    /// Gets when the execution started.
    /// </summary>
    public DateTime StartedAt { get; }

    /// <summary>
    /// Gets when the execution completed.
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Gets the execution status.
    /// </summary>
    public ExecutionStatus Status { get; private set; }

    /// <summary>
    /// Gets the number of tasks a successful execution completed — each counted once, by its final
    /// outcome (GAP-32): a task a graph retried and that then succeeded is one completed task. A
    /// failed execution keeps 0; its <see cref="FailureReason"/> names every task that did not succeed.
    /// </summary>
    public int CompletedTasks { get; private set; }

    /// <summary>
    /// Gets the failure reason if execution failed.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Gets the execution duration.
    /// </summary>
    public TimeSpan? Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : null;

    /// <summary>
    /// Initializes a new instance of CrewExecution.
    /// </summary>
    internal CrewExecution(ProcessId processId, DateTime startedAt)
    {
        ArgumentNullException.ThrowIfNull(processId);
        ProcessId = processId;
        StartedAt = startedAt;
        Status = ExecutionStatus.Running;
        CompletedTasks = 0;
    }

    /// <summary>
    /// Marks the execution as completed: the run succeeded, every task it ran succeeded (GAP-32). A run
    /// with a task that did not succeed is a failed run (<see cref="Fail"/>), never a partial success.
    /// </summary>
    /// <param name="completedTasks">The tasks the run completed, each counted once.</param>
    internal void Complete(int completedTasks)
    {
        if (Status != ExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot complete execution in {Status} status.");

        if (completedTasks < 0)
            throw new ArgumentException("Completed tasks cannot be negative.", nameof(completedTasks));

        CompletedTasks = completedTasks;
        CompletedAt = DateTime.UtcNow;
        Status = ExecutionStatus.Succeeded;
    }

    /// <summary>
    /// Marks the execution as failed.
    /// </summary>
    internal void Fail(string reason)
    {
        if (Status != ExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot fail execution in {Status} status.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        FailureReason = reason;
        CompletedAt = DateTime.UtcNow;
        Status = ExecutionStatus.Failed;
    }
}

/// <summary>
/// Represents the status of a crew execution.
/// </summary>
public enum ExecutionStatus
{
    /// <summary>
    /// Execution is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// Execution completed successfully: every task it ran succeeded.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Execution failed: a task did not succeed, or the run stopped on an exception or a
    /// cancellation — a cancelled run is a failed one, its reason saying so (GAP-32).
    /// </summary>
    Failed
}
