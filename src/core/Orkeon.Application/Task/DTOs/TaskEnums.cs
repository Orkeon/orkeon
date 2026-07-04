namespace Orkeon.Application.Task.DTOs;

/// <summary>
/// Task status enumeration.
/// </summary>
public enum TaskStatus
{
    /// <summary>Task is pending execution.</summary>
    Pending,

    /// <summary>Task is currently being executed.</summary>
    InProgress,

    /// <summary>Task has been completed successfully.</summary>
    Completed,

    /// <summary>Task execution failed.</summary>
    Failed,

    /// <summary>Task was cancelled.</summary>
    Cancelled,

    /// <summary>Task has been delegated to another agent.</summary>
    Delegated,

    /// <summary>Task is waiting for dependencies.</summary>
    WaitingForDependencies,

    /// <summary>Task is blocked by dependencies.</summary>
    Blocked
}

/// <summary>
/// Task priority enumeration.
/// </summary>
public enum TaskPriority
{
    /// <summary>Low priority task.</summary>
    Low,

    /// <summary>Normal priority task.</summary>
    Normal,

    /// <summary>High priority task.</summary>
    High,

    /// <summary>Critical priority task.</summary>
    Critical
}
