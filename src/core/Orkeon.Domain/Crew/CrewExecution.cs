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
    /// Gets the number of completed tasks.
    /// </summary>
    public int CompletedTasks { get; private set; }

    /// <summary>
    /// Gets the number of failed tasks.
    /// </summary>
    public int FailedTasks { get; private set; }

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
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var totalTasks = CompletedTasks + FailedTasks;
            return totalTasks > 0 ? (double)CompletedTasks / totalTasks * 100 : 0;
        }
    }

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
        FailedTasks = 0;
    }

    /// <summary>
    /// Marks the execution as completed.
    /// </summary>
    internal void Complete(int completedTasks, int failedTasks)
    {
        if (Status != ExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot complete execution in {Status} status.");

        if (completedTasks < 0)
            throw new ArgumentException("Completed tasks cannot be negative.", nameof(completedTasks));

        if (failedTasks < 0)
            throw new ArgumentException("Failed tasks cannot be negative.", nameof(failedTasks));

        CompletedTasks = completedTasks;
        FailedTasks = failedTasks;
        CompletedAt = DateTime.UtcNow;
        Status = failedTasks == 0 ? ExecutionStatus.Succeeded : ExecutionStatus.PartialSuccess;
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

    /// <summary>
    /// Updates the task progress.
    /// </summary>
    internal void UpdateProgress(int completedTasks, int failedTasks)
    {
        if (Status != ExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot update progress for execution in {Status} status.");

        if (completedTasks < 0)
            throw new ArgumentException("Completed tasks cannot be negative.", nameof(completedTasks));

        if (failedTasks < 0)
            throw new ArgumentException("Failed tasks cannot be negative.", nameof(failedTasks));

        CompletedTasks = completedTasks;
        FailedTasks = failedTasks;
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
    /// Execution completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Execution completed with some failures.
    /// </summary>
    PartialSuccess,

    /// <summary>
    /// Execution failed completely.
    /// </summary>
    Failed,

    /// <summary>
    /// Execution was cancelled.
    /// </summary>
    Cancelled
}
