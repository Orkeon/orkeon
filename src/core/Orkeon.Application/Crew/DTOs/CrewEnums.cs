namespace Orkeon.Application.Crew.DTOs;

/// <summary>
/// Process types for crew execution.
/// </summary>
public enum ProcessType
{
    /// <summary>Tasks are executed sequentially.</summary>
    Sequential,

    /// <summary>Tasks are executed in parallel where possible.</summary>
    Parallel,

    /// <summary>Tasks are managed by a hierarchical manager agent.</summary>
    Hierarchical,

    /// <summary>Tasks require consensus between agents.</summary>
    Consensual,

    /// <summary>Tasks run as a state graph with conditional edges and controlled cycles.</summary>
    Graph,

    /// <summary>Agents self-organize under an execution budget, delegating and spawning.</summary>
    Autonomous
}

/// <summary>
/// Crew execution status.
/// </summary>
public enum CrewStatus
{
    /// <summary>Crew is idle and ready for execution.</summary>
    Idle,

    /// <summary>Crew is currently executing tasks.</summary>
    Executing,

    /// <summary>Crew execution completed successfully.</summary>
    Completed,

    /// <summary>Crew execution failed.</summary>
    Failed,

    /// <summary>Crew execution was paused.</summary>
    Paused,

    /// <summary>Crew execution was cancelled.</summary>
    Cancelled
}
