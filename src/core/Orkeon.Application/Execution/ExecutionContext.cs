using Orkeon.Domain.Common;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Execution;

/// <summary>
/// The four classic agent-memory types.
/// </summary>
public enum MemoryType
{
    /// <summary>Short Term.</summary>
    ShortTerm,
    /// <summary>Long Term.</summary>
    LongTerm,
    /// <summary>Entity.</summary>
    Entity,
    /// <summary>Contextual.</summary>
    Contextual
}

/// <summary>
/// Non-generic interface for execution contexts.
/// Allows execution infrastructure to operate without requiring knowledge of the specific data type.
/// </summary>
public interface IExecutionContext
{
    /// <summary>Gets or sets the crew id.</summary>
    CrewId CrewId { get; }
    /// <summary>Gets or sets the memory.</summary>
    IMemoryScope Memory { get; }
    /// <summary>Gets or sets the previous outputs.</summary>
    IReadOnlyList<TaskOutput> PreviousOutputs { get; }
    /// <summary>Gets or sets the cancellation token.</summary>
    CancellationToken CancellationToken { get; }
    /// <summary>Gets or sets a value indicating whether is cancelled.</summary>
    bool IsCancelled { get; }
    /// <summary>Gets or sets the last output.</summary>
    TaskOutput? LastOutput { get; }
    /// <summary>Gets or sets the untyped data.</summary>
    object UntypedData { get; }
}

/// <summary>
/// Generic execution context for type-safe task execution.
/// Provides strongly-typed data access and memory management.
/// Phase 3.1.1: Complete type safety with generics.
/// </summary>
public class ExecutionContext<TData> : IExecutionContext where TData : class, new()
{
    /// <summary>Gets or sets the crew id.</summary>
    public CrewId CrewId { get; }
    /// <summary>Gets or sets the data.</summary>
    public TData Data { get; private set; }
    /// <summary>Gets or sets the memory.</summary>
    public IMemoryScope Memory { get; }
    /// <summary>Gets or sets the previous outputs.</summary>
    public IReadOnlyList<TaskOutput> PreviousOutputs { get; }
    /// <summary>Gets or sets the cancellation token.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the data as a non-generic object for use via IExecutionContext.
    /// </summary>
    public object UntypedData => Data;

    private readonly object _dataLock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionContext"/>.
    /// </summary>
    public ExecutionContext(
        CrewId crewId,
        TData data,
        IMemoryScope memory,
        IReadOnlyList<TaskOutput> previousOutputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        CrewId = crewId;
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        ArgumentNullException.ThrowIfNull(memory);
        Memory = memory;
        PreviousOutputs = previousOutputs ?? Array.Empty<TaskOutput>();
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Updates the data in a thread-safe manner.
    /// </summary>
    public void UpdateData(Action<TData> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        lock (_dataLock)
        {
            updateAction(Data);
        }
    }

    /// <summary>
    /// Transforms the context to a new data type.
    /// </summary>
    public ExecutionContext<TNewData> Transform<TNewData>(
        Func<TData, TNewData> transformer)
        where TNewData : class, new()
    {
        ArgumentNullException.ThrowIfNull(transformer);

        lock (_dataLock)
        {
            var newData = transformer(Data);
            return new ExecutionContext<TNewData>(
                CrewId,
                newData,
                Memory,
                PreviousOutputs,
                CancellationToken);
        }
    }

    /// <summary>
    /// Creates a new context with an additional task output.
    /// </summary>
    public ExecutionContext<TData> WithOutput(TaskOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var newOutputs = PreviousOutputs.Append(output).ToList();
        return new ExecutionContext<TData>(
            CrewId,
            Data,
            Memory,
            newOutputs,
            CancellationToken);
    }

    /// <summary>
    /// Gets a read-only copy of the data.
    /// </summary>
    public TData DataSnapshot
    {
        get
        {
            lock (_dataLock)
            {
                return Data;
            }
        }
    }

    /// <summary>
    /// Checks if the context has been cancelled.
    /// </summary>
    public bool IsCancelled => CancellationToken.IsCancellationRequested;

    /// <summary>
    /// Gets the last task output if available.
    /// </summary>
    public TaskOutput? LastOutput => PreviousOutputs.Count > 0 ? PreviousOutputs[^1] : default;

    /// <summary>
    /// Gets task outputs from a specific agent.
    /// </summary>
    public IEnumerable<TaskOutput> GetOutputsFromAgent(string agentId) =>
        PreviousOutputs.Where(output => output.AgentId == agentId);

    /// <summary>
    /// Gets all successful outputs.
    /// </summary>
    public IEnumerable<TaskOutput> GetSuccessfulOutputs() =>
        PreviousOutputs.Where(output => output.Success);
}

/// <summary>
/// Non-generic base for ExecutionContext factory methods.
/// </summary>
public static class ExecutionContext
{
    /// <summary>
    /// Creates a generic execution context with the specified data type.
    /// </summary>
    public static ExecutionContext<TData> Create<TData>(
        CrewId crewId,
        TData data,
        IMemoryScope memory,
        IReadOnlyList<TaskOutput>? previousOutputs = null,
        CancellationToken cancellationToken = default)
        where TData : class, new()
    {
        return new ExecutionContext<TData>(
            crewId,
            data,
            memory,
            previousOutputs ?? Array.Empty<TaskOutput>(),
            cancellationToken);
    }

    /// <summary>
    /// Creates an execution context with empty data.
    /// </summary>
    public static ExecutionContext<TData> CreateEmpty<TData>(
        CrewId crewId,
        IMemoryScope memory,
        CancellationToken cancellationToken = default)
        where TData : class, new()
    {
        return Create(crewId, new TData(), memory, null, cancellationToken);
    }
}

/// <summary>
/// Task output record for execution context.
/// </summary>
public record TaskOutput(
    string TaskId,
    string? AgentId,
    string Content,
    DateTime CompletedAt,
    bool Success,
    TimeSpan ExecutionTime,
    IReadOnlyList<Orkeon.Domain.Tools.ToolUsage>? ToolsUsed = null)
{
    /// <summary>Tools Used.</summary>
    public IReadOnlyList<Orkeon.Domain.Tools.ToolUsage> ToolsUsed { get; init; } = ToolsUsed ?? Array.Empty<Orkeon.Domain.Tools.ToolUsage>();

    /// <summary>
    /// Alias for Content to maintain compatibility with existing code.
    /// </summary>
    public string RawOutput => Content;
}

