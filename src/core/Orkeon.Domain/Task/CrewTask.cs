using Orkeon.Domain.Common;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Task;

/// <summary>
/// Task aggregate root representing a unit of work to be executed by an agent.
/// Uses a dictionary-based context for flexible key-value storage.
/// </summary>
public sealed class CrewTask : CrewTaskBase<DefaultTaskContext>
{
    /// <summary>
    /// Gets the task context as a read-only dictionary.
    /// </summary>
    public new IReadOnlyDictionary<string, object> Context =>
        TypedContext.Values.AsReadOnly();

    /// <summary>
    /// Private constructor for the Task.
    /// </summary>
    private CrewTask(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        TaskPriority priority,
        TaskOutputOptions? outputOptions)
        : base(id, description, expectedOutput, priority ?? TaskPriority.Normal, outputOptions)
    {
    }

    /// <summary>
    /// Creates a new task with the specified parameters.
    /// </summary>
    public static CrewTask Create(
        TaskDescription description,
        ExpectedOutput expectedOutput,
        TaskPriority? priority = null,
        TaskOutputOptions? outputOptions = null)
    {
        ArgumentNullException.ThrowIfNull(expectedOutput);

        return new CrewTask(TaskId.Create(), description, expectedOutput, priority ?? TaskPriority.Normal, outputOptions);
    }

    /// <summary>
    /// Adds context data to the task.
    /// </summary>
    public void AddContext(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        UpdateContext(ctx => ctx.Values[key] = value);
    }

    /// <inheritdoc />
    public override string GetContextSummary()
    {
        var dict = TypedContext.Values;

        if (dict.Count == 0)
            return "No context";

        var items = dict.Select(kvp => $"{kvp.Key}: {kvp.Value}");
        return string.Join(", ", items);
    }
}
