using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.Task.Events;

namespace Orkeon.Domain.Task.Contexts;

/// <summary>
/// Generic interface for task contexts.
/// </summary>
public interface ITaskContext<T> where T : class, new()
{
    /// <summary>
    /// Gets the typed data.
    /// </summary>
    T Data { get; }

    /// <summary>
    /// Updates the data using an action.
    /// </summary>
    void Update(Action<T> updateAction);

    /// <summary>
    /// Gets the context metadata.
    /// </summary>
    TaskContextMetadata Metadata { get; }

    /// <summary>
    /// Transforms this context to another type.
    /// </summary>
    ITaskContext<TNew> Transform<TNew>(Func<T, TNew> transformer)
        where TNew : class, new();
}

/// <summary>
/// Thread-safe implementation of task context.
/// </summary>
public class TypedTaskContext<T> : ITaskContext<T> where T : class, new()
{
    private readonly T _data;
    private readonly object _lock = new();
    private readonly List<DomainEvent> _events = [];

    /// <inheritdoc />
    public T Data
    {
        get
        {
            lock (_lock)
                return _data;
        }
    }

    /// <inheritdoc />
    public TaskContextMetadata Metadata { get; }

    /// <summary>Initializes a new instance of <see cref="TypedTaskContext{T}"/>.</summary>
    /// <param name="initialData">The initial context data.</param>
    /// <param name="metadata">The metadata for this context.</param>
    public TypedTaskContext(T initialData, TaskContextMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(initialData);
        _data = initialData;
        ArgumentNullException.ThrowIfNull(metadata);
        Metadata = metadata;
    }

    /// <inheritdoc />
    public void Update(Action<T> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        lock (_lock)
        {
            var before = JsonSerializer.Serialize(_data);
            updateAction(_data);
            var after = JsonSerializer.Serialize(_data);

            if (before != after)
            {
                _events.Add(new TaskContextUpdatedEvent
                {
                    TaskId = Metadata.TaskId,
                    AgentId = Metadata.AgentId,
                    ContextType = typeof(T).Name,
                    BeforeState = before,
                    AfterState = after
                });
            }
        }
    }

    /// <inheritdoc />
    public ITaskContext<TNew> Transform<TNew>(Func<T, TNew> transformer)
        where TNew : class, new()
    {
        ArgumentNullException.ThrowIfNull(transformer);
        lock (_lock)
        {
            var newData = transformer(_data);
            return new TypedTaskContext<TNew>(newData, Metadata);
        }
    }

    /// <summary>
    /// Gets the events raised by this context.
    /// </summary>
    public IReadOnlyList<DomainEvent> GetEvents()
    {
        lock (_lock)
        {
            return _events.AsReadOnly();
        }
    }

    /// <summary>
    /// Clears the events.
    /// </summary>
    public void ClearEvents()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }
}

/// <summary>
/// Metadata for task contexts.
/// </summary>
public sealed record TaskContextMetadata
{
    /// <summary>Gets the task identifier.</summary>
    public TaskId TaskId { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public AgentId AgentId { get; init; }
    /// <summary>Gets when the context was created.</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>Gets the context type name.</summary>
    public string ContextType { get; init; }

    /// <summary>Initializes a new instance of <see cref="TaskContextMetadata"/>.</summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="contextType">The context type name.</param>
    public TaskContextMetadata(
        TaskId taskId,
        AgentId agentId,
        string contextType)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        TaskId = taskId;
        ArgumentNullException.ThrowIfNull(agentId);
        AgentId = agentId;
        ArgumentNullException.ThrowIfNull(contextType);
        ContextType = contextType;
        CreatedAt = DateTime.UtcNow;
    }
}
