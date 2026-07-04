using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Domain.Task;

/// <summary>
/// Immutable snapshot of the persisted state of a <see cref="CrewTask"/> aggregate.
/// Used by <see cref="CrewTask.Restore(CrewTaskSnapshot)"/> to rehydrate a task from
/// persistence with named members instead of a long positional parameter list.
/// </summary>
public sealed record CrewTaskSnapshot
{
    /// <summary>The task identifier.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The task description.</summary>
    public required TaskDescription Description { get; init; }

    /// <summary>The expected output.</summary>
    public required ExpectedOutput ExpectedOutput { get; init; }

    /// <summary>The persisted task status.</summary>
    public required TaskStatus Status { get; init; }

    /// <summary>The agent the task is assigned to, if any.</summary>
    public AgentId? AssignedAgent { get; init; }

    /// <summary>The persisted task output, if any.</summary>
    public TaskOutput? Output { get; init; }

    /// <summary>The task priority.</summary>
    public required TaskPriority Priority { get; init; }

    /// <summary>The creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>The execution-start timestamp, if any.</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>The completion timestamp, if any.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Whether the task runs asynchronously.</summary>
    public bool AsyncExecution { get; init; }

    /// <summary>The optional JSON output schema.</summary>
    public JsonSchema? OutputJson { get; init; }

    /// <summary>The optional structured (pydantic-equivalent) output type.</summary>
    public Type? OutputPydantic { get; init; }

    /// <summary>The optional output file path.</summary>
    public string? OutputFile { get; init; }

    /// <summary>The optional task callback.</summary>
    public ITaskCallback? Callback { get; init; }

    /// <summary>Whether human input is required.</summary>
    public bool HumanInput { get; init; }

    /// <summary>The task dependencies.</summary>
    public IEnumerable<TaskId>? Dependencies { get; init; }

    /// <summary>The required tools.</summary>
    public IEnumerable<ToolId>? RequiredTools { get; init; }

    /// <summary>The persisted context key-value pairs.</summary>
    public IReadOnlyDictionary<string, object>? ContextValues { get; init; }
}
