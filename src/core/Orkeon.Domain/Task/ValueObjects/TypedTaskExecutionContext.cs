using System.Collections.Immutable;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Strongly typed task execution context.
/// </summary>
public sealed record TypedTaskExecutionContext : ValueObjectRecord
{
    /// <summary>The task identifier.</summary>
    public TaskId TaskId { get; }
    /// <summary>The agent executing this task.</summary>
    public AgentId ExecutingAgent { get; }
    /// <summary>The task variables.</summary>
    public TaskVariables Variables { get; init; }
    /// <summary>Outputs from previous tasks.</summary>
    public ImmutableArray<TaskOutput> PreviousOutputs { get; init; }
    /// <summary>Execution metadata.</summary>
    public ExecutionMetadata Metadata { get; init; }
    /// <summary>Tool calls made during execution.</summary>
    public ImmutableArray<ToolCall> ToolCalls { get; init; }

    /// <summary>
    /// Initializes a new instance of <see cref="TypedTaskExecutionContext"/> with validation.
    /// </summary>
    public TypedTaskExecutionContext(
        TaskId TaskId,
        AgentId ExecutingAgent,
        TaskVariables Variables,
        ImmutableArray<TaskOutput> PreviousOutputs,
        ExecutionMetadata Metadata,
        ImmutableArray<ToolCall> ToolCalls)
    {
        ArgumentNullException.ThrowIfNull(TaskId);
        ArgumentNullException.ThrowIfNull(ExecutingAgent);
        ArgumentNullException.ThrowIfNull(Variables);
        ArgumentNullException.ThrowIfNull(Metadata);

        this.TaskId = TaskId;
        this.ExecutingAgent = ExecutingAgent;
        this.Variables = Variables;
        this.PreviousOutputs = PreviousOutputs;
        this.Metadata = Metadata;
        this.ToolCalls = ToolCalls;
    }

    /// <summary>
    /// Deconstruct for backward compatibility with positional record syntax.
    /// </summary>
    public void Deconstruct(
        out TaskId taskId,
        out AgentId executingAgent,
        out TaskVariables variables,
        out ImmutableArray<TaskOutput> previousOutputs,
        out ExecutionMetadata metadata,
        out ImmutableArray<ToolCall> toolCalls)
    {
        taskId = TaskId;
        executingAgent = ExecutingAgent;
        variables = Variables;
        previousOutputs = PreviousOutputs;
        metadata = Metadata;
        toolCalls = ToolCalls;
    }

    /// <summary>
    /// Creates a new execution context for a task.
    /// </summary>
    public static TypedTaskExecutionContext Create(
        TaskId taskId,
        AgentId executingAgent,
        TimeSpan? maxExecutionTime = null)
    {
        return new TypedTaskExecutionContext(
            TaskId: taskId,
            ExecutingAgent: executingAgent,
            Variables: TaskVariables.Empty,
            PreviousOutputs: [],
            Metadata: ExecutionMetadata.CreateNew(maxExecutionTime),
            ToolCalls: []);
    }

    /// <summary>
    /// Adds or updates a variable.
    /// </summary>
    public TypedTaskExecutionContext WithVariable<T>(string key, T value)
    {
        return this with { Variables = Variables.With(key, value) };
    }

    /// <summary>
    /// Adds a previous output.
    /// </summary>
    public TypedTaskExecutionContext WithPreviousOutput(TaskOutput output)
    {
        return this with { PreviousOutputs = PreviousOutputs.Add(output) };
    }

    /// <summary>
    /// Records a tool call.
    /// </summary>
    public TypedTaskExecutionContext WithToolCall(ToolCall toolCall)
    {
        return this with { ToolCalls = ToolCalls.Add(toolCall) };
    }

    /// <summary>
    /// Marks the execution as complete.
    /// </summary>
    public TypedTaskExecutionContext Complete(string? error = null)
    {
        return this with { Metadata = Metadata.Complete(error) };
    }

}
