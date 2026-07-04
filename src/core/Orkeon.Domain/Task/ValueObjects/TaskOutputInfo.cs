using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Groups all parameters for creating a <see cref="TaskOutput"/>.
/// </summary>
public sealed class TaskOutputInfo
{
    /// <summary>
    /// The raw output value (required).
    /// </summary>
    public string RawOutput { get; init; } = null!;

    /// <summary>
    /// The output format type.
    /// </summary>
    public string Format { get; init; } = "text";

    /// <summary>
    /// The formatted output if available.
    /// </summary>
    public string? FormattedOutput { get; init; }

    /// <summary>
    /// The task identifier.
    /// </summary>
    public TaskId? TaskId { get; init; }

    /// <summary>
    /// The identifier of the agent that produced this output, if known.
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// Whether the task execution was successful.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// The execution time for the task.
    /// </summary>
    public TimeSpan? ExecutionTime { get; init; }

    /// <summary>
    /// Any structured output data.
    /// </summary>
    public object? StructuredOutput { get; init; }

    /// <summary>
    /// The timestamp when the output was generated (defaults to current UTC time).
    /// </summary>
    public DateTime? GeneratedAt { get; init; }

    /// <summary>
    /// Validates the current state and throws if any invariant is violated.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when RawOutput or Format is null or whitespace, or when ExecutionTime is negative.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RawOutput))
            throw new InvalidOperationException($"{nameof(RawOutput)} cannot be null or empty.");

        if (string.IsNullOrWhiteSpace(Format))
            throw new InvalidOperationException($"{nameof(Format)} cannot be null or empty.");

        if (ExecutionTime.HasValue && ExecutionTime.Value < TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(ExecutionTime)} cannot be negative.");
    }

    /// <summary>
    /// Creates a validated <see cref="TaskOutputInfo"/> instance.
    /// </summary>
    /// <param name="rawOutput">The raw output value (required).</param>
    /// <param name="format">The output format type.</param>
    /// <param name="formattedOutput">The formatted output if available.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="success">Whether the task execution was successful.</param>
    /// <param name="executionTime">The execution time for the task.</param>
    /// <param name="structuredOutput">Any structured output data.</param>
    /// <param name="generatedAt">The timestamp when the output was generated.</param>
    /// <param name="agentId">The identifier of the agent that produced the output, if known.</param>
    /// <returns>A validated <see cref="TaskOutputInfo"/>.</returns>
#pragma warning disable S107 // Methods should not have too many parameters — all fields are optional with defaults; use object initializer pattern for callers
    public static TaskOutputInfo Create(
        string rawOutput,
        string format = "text",
        string? formattedOutput = null,
        TaskId? taskId = null,
        bool success = true,
        TimeSpan? executionTime = null,
        object? structuredOutput = null,
        DateTime? generatedAt = null,
        string? agentId = null)
#pragma warning restore S107
    {
        var info = new TaskOutputInfo
        {
            RawOutput = rawOutput,
            Format = format,
            FormattedOutput = formattedOutput,
            TaskId = taskId,
            Success = success,
            ExecutionTime = executionTime,
            StructuredOutput = structuredOutput,
            GeneratedAt = generatedAt,
            AgentId = agentId
        };
        info.Validate();
        return info;
    }
}
