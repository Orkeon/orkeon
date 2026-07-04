using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Represents the output from crew execution.
/// </summary>
public sealed record CrewOutput
{
    /// <summary>
    /// Gets the final output text.
    /// </summary>
    public string Output { get; init; }

    /// <summary>
    /// Gets the structured output data.
    /// </summary>
    public object? StructuredOutput { get; init; }

    /// <summary>
    /// Gets the task outputs.
    /// </summary>
    public IReadOnlyList<TaskOutput> TaskOutputs { get; init; }

    /// <summary>
    /// Gets whether the execution was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets any error message if execution failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the total execution time.
    /// </summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Gets the timestamp when execution completed.
    /// </summary>
    public DateTime CompletedAt { get; init; }

    /// <summary>
    /// Gets execution metadata.
    /// </summary>
    public CrewMetadata Metadata { get; init; }

    /// <summary>Initializes a new instance of <see cref="CrewOutput"/>.</summary>
    /// <param name="output">The final output text.</param>
    /// <param name="structuredOutput">The optional structured output object.</param>
    /// <param name="taskOutputs">The outputs from each task.</param>
    /// <param name="success">Whether the execution was successful.</param>
    /// <param name="executionTime">The total execution time.</param>
    /// <param name="error">The error message, if any.</param>
    /// <param name="metadata">Optional execution metadata.</param>
    internal CrewOutput(
        string output,
        object? structuredOutput,
        IEnumerable<TaskOutput> taskOutputs,
        bool success,
        TimeSpan executionTime,
        string? error = null,
        CrewMetadata? metadata = null)
    {
        Output = output ?? string.Empty;
        StructuredOutput = structuredOutput;
        TaskOutputs = taskOutputs?.ToList().AsReadOnly() ?? new List<TaskOutput>().AsReadOnly();
        Success = success;
        ExecutionTime = executionTime;
        Error = error;
        CompletedAt = DateTime.UtcNow;
        Metadata = metadata ?? CrewMetadata.Empty;
    }

    /// <summary>
    /// Creates a successful crew output.
    /// </summary>
    public static CrewOutput CreateSuccess(
        string output,
        object? structuredOutput,
        IEnumerable<TaskOutput> taskOutputs,
        TimeSpan executionTime,
        CrewMetadata? metadata = null)
    {
        return new CrewOutput(
            output,
            structuredOutput,
            taskOutputs,
            success: true,
            executionTime,
            error: null,
            metadata);
    }

    /// <summary>
    /// Creates a failed crew output.
    /// </summary>
    public static CrewOutput CreateFailure(
        string error,
        IEnumerable<TaskOutput> taskOutputs,
        TimeSpan executionTime,
        CrewMetadata? metadata = null)
    {
        return new CrewOutput(
            output: string.Empty,
            structuredOutput: null,
            taskOutputs,
            success: false,
            executionTime,
            error,
            metadata);
    }

    /// <summary>
    /// Gets a summary of all task outputs.
    /// </summary>
    public string GetTaskSummary()
    {
        if (TaskOutputs.Count == 0)
            return "No tasks executed.";

        var summary = new List<string>();
        foreach (var task in TaskOutputs)
        {
            summary.Add($"- Task {task.TaskId}: {(task.Success ? "Success" : "Failed")} - {task.Output}");
        }

        return string.Join("\n", summary);
    }

    /// <summary>
    /// Gets statistics about the execution.
    /// </summary>
    public ExecutionStatistics GetStatistics()
    {
        return new ExecutionStatistics(
            TotalTasks: TaskOutputs.Count,
            SuccessfulTasks: TaskOutputs.Count(t => t.Success),
            FailedTasks: TaskOutputs.Count(t => !t.Success),
            TotalExecutionTime: ExecutionTime,
            AverageTaskTime: TaskOutputs.Count > 0
                ? TimeSpan.FromMilliseconds(TaskOutputs.Average(t => t.ExecutionTime.TotalMilliseconds))
                : TimeSpan.Zero);
    }
}

/// <summary>
/// Represents execution statistics.
/// </summary>
/// <param name="TotalTasks">The total number of tasks executed.</param>
/// <param name="SuccessfulTasks">The number of tasks that completed successfully.</param>
/// <param name="FailedTasks">The number of tasks that failed.</param>
/// <param name="TotalExecutionTime">The total time taken to execute all tasks.</param>
/// <param name="AverageTaskTime">The average time per task.</param>
public record ExecutionStatistics(
    int TotalTasks,
    int SuccessfulTasks,
    int FailedTasks,
    TimeSpan TotalExecutionTime,
    TimeSpan AverageTaskTime)
{
    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalTasks > 0 ? (double)SuccessfulTasks / TotalTasks * 100 : 0;
}
