using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>Information about an agent's current workload.</summary>
public record WorkloadInfo
{
    /// <summary>Utilization percentage above which an agent is considered overloaded.</summary>
    public const double OverloadThreshold = 80;

    /// <summary>Maximum utilization percentage value (capped at 100).</summary>
    public const double UtilizationMax = 100;

    /// <summary>Gets the identifier of the agent this workload info belongs to.</summary>
    public AgentId AgentId { get; }
    /// <summary>Gets the number of currently active tasks.</summary>
    public int ActiveTasks { get; }
    /// <summary>Gets the number of queued tasks.</summary>
    public int QueuedTasks { get; }
    /// <summary>Gets the utilization percentage (0 to 100).</summary>
    public double UtilizationPercentage { get; }
    /// <summary>Gets the average duration of completed tasks.</summary>
    public TimeSpan AverageTaskDuration { get; }
    /// <summary>Gets the timestamp when the last task was completed.</summary>
    public DateTime LastTaskCompletedAt { get; }
    /// <summary>Gets the identifiers of the currently active tasks.</summary>
    public IReadOnlyList<TaskId> CurrentTaskIds { get; }

    /// <summary>Initializes a new instance of <see cref="WorkloadInfo"/>.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="activeTasks">Number of active tasks.</param>
    /// <param name="queuedTasks">Number of queued tasks.</param>
    /// <param name="utilizationPercentage">Utilization percentage (0 to 100).</param>
    /// <param name="averageTaskDuration">Average task duration.</param>
    /// <param name="lastTaskCompletedAt">Timestamp of last task completion.</param>
    /// <param name="currentTaskIds">Identifiers of current tasks.</param>
    private WorkloadInfo(
        AgentId agentId,
        int activeTasks,
        int queuedTasks,
        double utilizationPercentage,
        TimeSpan averageTaskDuration,
        DateTime lastTaskCompletedAt,
        IReadOnlyList<TaskId>? currentTaskIds = null)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        AgentId = agentId;
        ActiveTasks = activeTasks;
        QueuedTasks = queuedTasks;
        UtilizationPercentage = Math.Max(0, Math.Min(UtilizationMax, utilizationPercentage));
        AverageTaskDuration = averageTaskDuration;
        LastTaskCompletedAt = lastTaskCompletedAt;
        CurrentTaskIds = currentTaskIds ?? Array.Empty<TaskId>();
    }

    /// <summary>Creates a new <see cref="WorkloadInfo"/> instance.</summary>
    public static WorkloadInfo Create(
        AgentId agentId,
        int activeTasks,
        int queuedTasks,
        double utilizationPercentage,
        TimeSpan averageTaskDuration,
        DateTime lastTaskCompletedAt,
        IReadOnlyList<TaskId>? currentTaskIds = null)
        => new(agentId, activeTasks, queuedTasks, utilizationPercentage, averageTaskDuration, lastTaskCompletedAt, currentTaskIds);

    /// <summary>Gets a value indicating whether the agent is overloaded (utilization > 80%).</summary>
    public bool IsOverloaded => UtilizationPercentage > OverloadThreshold;
    /// <summary>Gets a value indicating whether the agent has no active tasks.</summary>
    public bool IsAvailable => ActiveTasks == 0;
    /// <summary>Gets the total number of active and queued tasks.</summary>
    public int TotalTasks => ActiveTasks + QueuedTasks;
}
