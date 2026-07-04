using System.Collections.Immutable;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Represents the output from batch operations.
/// </summary>
public sealed record BatchOutput
{
    /// <summary>Gets the batch execution ID.</summary>
    public string BatchId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Gets the list of results from each execution in the batch.</summary>
    public ImmutableList<BatchExecutionResult> Results { get; init; } = [];

    /// <summary>Gets the total number of executions in the batch.</summary>
    public int TotalExecutions { get; init; }

    /// <summary>Gets the number of successful executions.</summary>
    public int SuccessfulExecutions { get; init; }

    /// <summary>Gets the number of failed executions.</summary>
    public int FailedExecutions { get; init; }

    /// <summary>Gets the total execution time for the batch.</summary>
    public TimeSpan TotalExecutionTime { get; init; }

    /// <summary>Gets the start time of the batch execution.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>Gets the end time of the batch execution.</summary>
    public DateTime EndTime { get; init; }

    /// <summary>Gets metadata about the batch execution.</summary>
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Represents a single execution result within a batch.
/// </summary>
public sealed record BatchExecutionResult
{
    /// <summary>Gets the execution ID.</summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Gets the input provided for this execution.</summary>
    public ImmutableDictionary<string, object>? Input { get; init; }

    /// <summary>Gets the output from this execution.</summary>
    public object? Output { get; init; }

    /// <summary>Gets whether this execution was successful.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the error message if the execution failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets the execution time for this specific execution.</summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>Gets token usage for this execution.</summary>
    public TokenUsage? TokenUsage { get; init; }
}
