namespace Orkeon.Application.Telemetry;

/// <summary>How a task span ended.</summary>
public enum TaskSpanStatus
{
    /// <summary>The task completed.</summary>
    Completed,

    /// <summary>The task failed.</summary>
    Failed,

    /// <summary>The task was cancelled or skipped.</summary>
    Cancelled,
}
