using Orkeon.Domain.Task;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Represents the context for task execution by an agent.
/// </summary>
public sealed class TaskExecutionContext
{
    /// <summary>
    /// Gets the agent executing the task.
    /// </summary>
    public Agent Agent { get; }

    /// <summary>
    /// Gets the task being executed.
    /// </summary>
    public ICrewTask Task { get; }

    /// <summary>
    /// Gets the typed execution context.
    /// </summary>
    private TypedTaskExecutionContext TypedContext { get; set; }


    /// <summary>
    /// Gets the typed variables.
    /// </summary>
    public TaskVariables TypedVariables => TypedContext.Variables;

    /// <summary>
    /// Gets the typed metadata.
    /// </summary>
    public ExecutionMetadata TypedMetadata => TypedContext.Metadata;

    /// <summary>
    /// Gets the timestamp when the context was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the TaskExecutionContext class.
    /// </summary>
    private TaskExecutionContext(Agent agent, ICrewTask task)
    {
        ArgumentNullException.ThrowIfNull(agent);
        Agent = agent;
        ArgumentNullException.ThrowIfNull(task);
        Task = task;
        CreatedAt = DateTime.UtcNow;

        // Initialize typed context
        var taskId = task.TaskId;
        var agentId = agent.Id;
        TypedContext = TypedTaskExecutionContext.Create(taskId, agentId);
    }

    /// <summary>
    /// Creates a new <see cref="TaskExecutionContext"/> for the given agent and task.
    /// </summary>
    /// <param name="agent">The agent executing the task.</param>
    /// <param name="task">The task being executed.</param>
    /// <returns>A new <see cref="TaskExecutionContext"/> instance.</returns>
    public static TaskExecutionContext For(Agent agent, ICrewTask task)
        => new(agent, task);

    /// <summary>
    /// Adds or updates a variable in the context.
    /// </summary>
    public void SetVariable(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ArgumentNullException.ThrowIfNull(value);

        TypedContext = TypedContext.WithVariable(key, value);
    }

    /// <summary>
    /// Gets a variable from the context.
    /// </summary>
    public T? GetVariable<T>(string key) where T : class
    {
        return TypedVariables.GetObject<T>(key);
    }

    /// <summary>
    /// Gets a string variable from the context.
    /// </summary>
    public string? GetStringVariable(string key)
    {
        return TypedVariables.GetString(key);
    }

    /// <summary>
    /// Gets an integer variable from the context.
    /// </summary>
    public int? GetIntVariable(string key)
    {
        return TypedVariables.GetInt(key);
    }

    /// <summary>
    /// Adds metadata to the context.
    /// </summary>
    public void AddMetadata(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        ArgumentNullException.ThrowIfNull(value);

        TypedContext = TypedContext with
        {
            Metadata = TypedContext.Metadata.WithCustomProperty(key, value)
        };
    }
}
