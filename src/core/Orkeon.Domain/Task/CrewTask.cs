using Orkeon.Domain.Common;
using Orkeon.Domain.Task.Contexts;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

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
    /// Private restore constructor — delegates to the base restore constructor.
    /// </summary>
#pragma warning disable S107 // Restore constructor requires all persisted state; by design
    private CrewTask(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        TaskStatus status,
        AgentId? assignedAgent,
        TaskOutput? output,
        TaskPriority priority,
        DateTime createdAt,
        DateTime? startedAt,
        DateTime? completedAt,
        bool asyncExecution,
        JsonSchema? outputJson,
        Type? outputPydantic,
        string? outputFile,
        ITaskCallback? callback,
        bool humanInput,
        IEnumerable<TaskId>? dependencies,
        IEnumerable<ToolId>? requiredTools,
        DefaultTaskContext? contextData)
        : base(id, description, expectedOutput, status, assignedAgent, output, priority,
               createdAt, startedAt, completedAt, asyncExecution, outputJson, outputPydantic,
               outputFile, callback, humanInput, dependencies, requiredTools, contextData)
    {
    }
#pragma warning restore S107

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
    /// Rehydrates a <see cref="CrewTask"/> from persistence without raising domain events.
    /// Use this factory when loading an existing task from a database or external store.
    /// </summary>
#pragma warning disable S107 // Restore factory requires all persisted state; by design
    internal static CrewTask Restore(
        TaskId id,
        TaskDescription description,
        ExpectedOutput expectedOutput,
        TaskStatus status,
        AgentId? assignedAgent,
        TaskOutput? output,
        TaskPriority priority,
        DateTime createdAt,
        DateTime? startedAt,
        DateTime? completedAt,
        bool asyncExecution,
        JsonSchema? outputJson,
        Type? outputPydantic,
        string? outputFile,
        ITaskCallback? callback,
        bool humanInput,
        IEnumerable<TaskId>? dependencies = null,
        IEnumerable<ToolId>? requiredTools = null,
        Dictionary<string, object>? contextValues = null)
        => Restore(new CrewTaskSnapshot
        {
            Id = id,
            Description = description,
            ExpectedOutput = expectedOutput,
            Status = status,
            AssignedAgent = assignedAgent,
            Output = output,
            Priority = priority,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AsyncExecution = asyncExecution,
            OutputJson = outputJson,
            OutputPydantic = outputPydantic,
            OutputFile = outputFile,
            Callback = callback,
            HumanInput = humanInput,
            Dependencies = dependencies,
            RequiredTools = requiredTools,
            ContextValues = contextValues
        });
#pragma warning restore S107

    /// <summary>
    /// Rehydrates a <see cref="CrewTask"/> from a <see cref="CrewTaskSnapshot"/> without
    /// raising domain events. This is the preferred reconstruction entry point: named
    /// snapshot members avoid the positional-argument fragility of the flat overload.
    /// </summary>
    public static CrewTask Restore(CrewTaskSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Id);

        DefaultTaskContext? ctx = null;
        if (snapshot.ContextValues != null)
        {
            ctx = new DefaultTaskContext();
            foreach (var kvp in snapshot.ContextValues)
                ctx.Values[kvp.Key] = kvp.Value;
        }

        return new CrewTask(
            snapshot.Id,
            snapshot.Description,
            snapshot.ExpectedOutput,
            snapshot.Status,
            snapshot.AssignedAgent,
            snapshot.Output,
            snapshot.Priority,
            snapshot.CreatedAt,
            snapshot.StartedAt,
            snapshot.CompletedAt,
            snapshot.AsyncExecution,
            snapshot.OutputJson,
            snapshot.OutputPydantic,
            snapshot.OutputFile,
            snapshot.Callback,
            snapshot.HumanInput,
            snapshot.Dependencies,
            snapshot.RequiredTools,
            ctx);
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
