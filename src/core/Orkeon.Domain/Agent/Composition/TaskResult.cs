using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent.Composition;

/// <summary>Result of a task execution.</summary>
public sealed record TaskResult
{
    /// <summary>Gets the identifier of the task.</summary>
    public TaskId TaskId { get; init; } = TaskId.Create();
    /// <summary>Gets the identifier of the agent that executed the task.</summary>
    public AgentId AgentId { get; init; } = AgentId.Create();
    /// <summary>Gets a value indicating whether the task succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the text output of the task.</summary>
    public string Output { get; init; } = string.Empty;
    /// <summary>Gets the structured output, or null if not available.</summary>
    public object? StructuredOutput { get; init; }
    /// <summary>Gets the execution duration.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Gets the timestamp when the task started.</summary>
    public DateTime StartedAt { get; init; }
    /// <summary>Gets the timestamp when the task completed.</summary>
    public DateTime CompletedAt { get; init; }
    /// <summary>Gets the tools used during task execution.</summary>
    public IReadOnlyList<string> ToolsUsed { get; init; } = [];
    /// <summary>Gets additional metadata for this result.</summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
    /// <summary>Gets the error message if the task failed, otherwise null.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the number of retries performed.</summary>
    public int RetryCount { get; init; }
    /// <summary>Gets the confidence score for the output, or null if not applicable.</summary>
    public double? ConfidenceScore { get; init; }

    /// <summary>Creates a successful task result.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="output">The task output.</param>
    /// <param name="duration">The execution duration.</param>
    /// <param name="structuredOutput">Optional structured output.</param>
    /// <returns>A successful <see cref="TaskResult"/>.</returns>
    public static TaskResult CreateSuccess(
        TaskId taskId,
        AgentId agentId,
        string output,
        TimeSpan duration,
        object? structuredOutput = null)
    {
        // One clock read, not two: with a read per property, StartedAt + Duration lands a
        // variable distance from CompletedAt -- the gap between the two calls, which a
        // loaded machine can stretch past 10 ms. Readers take that invariant seriously.
        var completedAt = DateTime.UtcNow;
        return new TaskResult
        {
            TaskId = taskId,
            AgentId = agentId,
            Success = true,
            Output = output,
            StructuredOutput = structuredOutput,
            Duration = duration,
            CompletedAt = completedAt,
            StartedAt = completedAt.Subtract(duration)
        };
    }

    /// <summary>Creates a failed task result.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="error">The error message.</param>
    /// <param name="duration">The execution duration.</param>
    /// <returns>A failed <see cref="TaskResult"/>.</returns>
    public static TaskResult CreateFailure(
        TaskId taskId,
        AgentId agentId,
        string error,
        TimeSpan duration)
    {
        var completedAt = DateTime.UtcNow;
        return new TaskResult
        {
            TaskId = taskId,
            AgentId = agentId,
            Success = false,
            Error = error,
            Duration = duration,
            CompletedAt = completedAt,
            StartedAt = completedAt.Subtract(duration)
        };
    }
}
