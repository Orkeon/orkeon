using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Represents the output of a completed task.
/// </summary>
public sealed record TaskOutput : ValueObjectRecord
{
    /// <summary>
    /// Gets the task identifier.
    /// </summary>
    public TaskId? TaskId { get; }

    /// <summary>
    /// Gets the identifier of the agent that produced this output, if known.
    /// </summary>
    public string? AgentId { get; }

    /// <summary>
    /// Gets the raw output value.
    /// </summary>
    public string RawOutput { get; }

    /// <summary>
    /// Gets the formatted output if available.
    /// </summary>
    public string? FormattedOutput { get; }

    /// <summary>
    /// Gets the output format type.
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Gets the timestamp when the output was generated.
    /// </summary>
    public DateTime GeneratedAt { get; }

    /// <summary>
    /// Gets whether the task execution was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the execution time for the task.
    /// </summary>
    public TimeSpan ExecutionTime { get; }

    /// <summary>
    /// Gets any structured output data.
    /// </summary>
    public object? StructuredOutput { get; }

    /// <summary>
    /// Gets the output string (alias for RawOutput).
    /// </summary>
    public string Output => RawOutput;

    /// <summary>
    /// Initializes a new instance of the TaskOutput from a <see cref="TaskOutputInfo"/>.
    /// </summary>
    private TaskOutput(TaskOutputInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (string.IsNullOrWhiteSpace(info.RawOutput))
            throw new ArgumentException("info.RawOutput cannot be empty.", nameof(info));

        if (string.IsNullOrWhiteSpace(info.Format))
            throw new ArgumentException("info.Format cannot be empty.", nameof(info));

        RawOutput = info.RawOutput;
#pragma warning disable CA1308 // lowercase is the stored/normalized form of the output format, not a comparison normalization
        Format = info.Format.ToLowerInvariant();
#pragma warning restore CA1308
        FormattedOutput = info.FormattedOutput;
        GeneratedAt = info.GeneratedAt ?? DateTime.UtcNow;
        TaskId = info.TaskId;
        AgentId = info.AgentId;
        Success = info.Success;
        ExecutionTime = info.ExecutionTime ?? TimeSpan.Zero;
        StructuredOutput = info.StructuredOutput;
    }

    /// <summary>
    /// Initializes a new instance of the TaskOutput.
    /// Convenience constructor that delegates to <see cref="TaskOutput(TaskOutputInfo)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when rawOutput is null or empty.</exception>
#pragma warning disable S107 // Backward-compatible constructor; use TaskOutput(TaskOutputInfo) instead
    private TaskOutput(
        string rawOutput,
        string format = "text",
        string? formattedOutput = null,
        TaskId? taskId = null,
        bool success = true,
        TimeSpan? executionTime = null,
        object? structuredOutput = null,
        DateTime? generatedAt = null,
        string? agentId = null)
        : this(new TaskOutputInfo
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
        })
    {
    }
#pragma warning restore S107

    /// <summary>
    /// Creates a new TaskOutput from a <see cref="TaskOutputInfo"/>.
    /// </summary>
    public static TaskOutput From(TaskOutputInfo info) => new(info);

    /// <summary>
    /// Creates a new TaskOutput with the specified parameters.
    /// </summary>
#pragma warning disable S107 // Factory method wraps backward-compatible constructor
    public static TaskOutput Create(
        string rawOutput,
        string format = "text",
        string? formattedOutput = null,
        TaskId? taskId = null,
        bool success = true,
        TimeSpan? executionTime = null,
        object? structuredOutput = null,
        DateTime? generatedAt = null,
        string? agentId = null)
        => new(rawOutput, format, formattedOutput, taskId, success, executionTime, structuredOutput, generatedAt, agentId);
#pragma warning restore S107

    /// <summary>
    /// Creates a simple text output.
    /// </summary>
    public static TaskOutput Text(string output) => new(output, "text");

    /// <summary>
    /// Creates a JSON formatted output.
    /// </summary>
    public static TaskOutput Json(string rawOutput, string? formatted = null) =>
        new(rawOutput, "json", formatted);

    /// <summary>
    /// Creates a markdown formatted output.
    /// </summary>
    public static TaskOutput Markdown(string output) => new(output, "markdown");
}
