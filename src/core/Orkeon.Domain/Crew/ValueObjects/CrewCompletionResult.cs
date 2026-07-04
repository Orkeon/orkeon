using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>
/// Value object carrying essential crew completion data for domain events.
/// Replaces the <c>CrewOutput</c> sealed class in event payloads to ensure
/// domain events only carry Identity, ValueObject, or primitive types.
/// </summary>
public sealed record CrewCompletionResult : ValueObjectRecord
{
    /// <summary>Gets the final output text.</summary>
    public string Output { get; init; }

    /// <summary>Gets whether the execution was successful.</summary>
    public bool Success { get; init; }

    /// <summary>Gets any error message if execution failed.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the total execution time.</summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>Gets the number of tasks that were executed.</summary>
    public int TaskCount { get; init; }

    /// <summary>Gets the number of tasks that completed successfully.</summary>
    public int SuccessfulTaskCount { get; init; }

    private CrewCompletionResult(
        string output,
        bool success,
        string? error,
        TimeSpan executionTime,
        int taskCount,
        int successfulTaskCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taskCount);
        ArgumentOutOfRangeException.ThrowIfNegative(successfulTaskCount);
        if (successfulTaskCount > taskCount)
            throw new ArgumentOutOfRangeException(nameof(successfulTaskCount),
                $"SuccessfulTaskCount ({successfulTaskCount}) cannot exceed TaskCount ({taskCount}).");
        if (executionTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(executionTime),
                "ExecutionTime cannot be negative.");

        Output = output ?? string.Empty;
        Success = success;
        Error = error;
        ExecutionTime = executionTime;
        TaskCount = taskCount;
        SuccessfulTaskCount = successfulTaskCount;
    }

    /// <summary>
    /// Creates a <see cref="CrewCompletionResult"/> from a <see cref="Orkeon.Domain.Crew.CrewOutput"/>.
    /// </summary>
    public static CrewCompletionResult FromCrewOutput(Orkeon.Domain.Crew.CrewOutput crewOutput)
    {
        ArgumentNullException.ThrowIfNull(crewOutput);

        return new CrewCompletionResult(
            crewOutput.Output,
            crewOutput.Success,
            crewOutput.Error,
            crewOutput.ExecutionTime,
            crewOutput.TaskOutputs.Count,
            crewOutput.TaskOutputs.Count(t => t.Success));
    }

    /// <summary>
    /// Creates a <see cref="CrewCompletionResult"/> directly from primitives.
    /// </summary>
    public static CrewCompletionResult Create(
        string output,
        bool success,
        TimeSpan executionTime,
        int taskCount = 0,
        int successfulTaskCount = 0,
        string? error = null)
    {
        return new CrewCompletionResult(output, success, error, executionTime, taskCount, successfulTaskCount);
    }
}
