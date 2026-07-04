using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Groups identity and input fields for an agent step.
/// </summary>
public sealed record AgentStepInfo(
    AgentId? AgentId = null,
    TaskId? TaskId = null,
    string Action = "",
    string Input = "",
    IReadOnlyList<string>? ToolsUsed = null,
    AgentStepContext? Context = null);

/// <summary>
/// Represents a step in an agent's execution.
/// </summary>
public sealed record AgentStep
{
    /// <summary>Gets the unique identifier of this step.</summary>
    public AgentStepId Id { get; init; }
    /// <summary>Gets the identifier of the agent that executed this step.</summary>
    public AgentId? AgentId { get; init; }
    /// <summary>Gets the identifier of the task associated with this step.</summary>
    public TaskId? TaskId { get; init; }
    /// <summary>Gets the action performed in this step.</summary>
    public string Action { get; init; }
    /// <summary>Gets the input provided to this step.</summary>
    public string Input { get; init; }
    /// <summary>Gets the text output of this step, or null if not completed.</summary>
    public string? Output { get; init; }
    /// <summary>Gets the structured output of this step, or null if not available.</summary>
    public object? StructuredOutput { get; init; }
    /// <summary>Gets a value indicating whether this step succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the error message if this step failed, otherwise null.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the timestamp when this step started.</summary>
    public DateTime StartedAt { get; init; }
    /// <summary>Gets the timestamp when this step completed, or null if not yet completed.</summary>
    public DateTime? CompletedAt { get; init; }
    /// <summary>Gets the duration of this step.</summary>
    public TimeSpan Duration => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;
    /// <summary>Gets the execution context for this step.</summary>
    public AgentStepContext Context { get; init; }
    /// <summary>Gets the list of tools used during this step.</summary>
    public IReadOnlyList<string> ToolsUsed { get; init; }

#pragma warning disable S107 // Private constructor groups identity via AgentStepInfo
    private AgentStep(
        AgentStepId id,
        AgentStepInfo info,
        string? output,
        object? structuredOutput,
        bool success,
        string? error,
        DateTime startedAt,
        DateTime? completedAt)
    {
        Id = id;
        AgentId = info.AgentId;
        TaskId = info.TaskId;
        Action = info.Action;
        Input = info.Input;
        Output = output;
        StructuredOutput = structuredOutput;
        Success = success;
        Error = error;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Context = info.Context ?? AgentStepContext.Empty;
        ToolsUsed = info.ToolsUsed switch
        {
            null => new System.Collections.ObjectModel.ReadOnlyCollection<string>([]),
            System.Collections.Generic.IList<string> list =>
                new System.Collections.ObjectModel.ReadOnlyCollection<string>(list),
            var other => new System.Collections.ObjectModel.ReadOnlyCollection<string>([.. other]),
        };
    }
#pragma warning restore S107

    /// <summary>
    /// Creates a successful agent step using grouped step info.
    /// </summary>
    public static AgentStep CreateSuccess(
        string output,
        object? structuredOutput,
        AgentStepInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var startedAt = DateTime.UtcNow;
        return new AgentStep(
            id: AgentStepId.Create(),
            info: info,
            output: output,
            structuredOutput: structuredOutput,
            success: true,
            error: null,
            startedAt: startedAt,
            completedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a successful agent step.
    /// Convenience overload that delegates to <see cref="CreateSuccess(string, object?, AgentStepInfo)"/>.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use CreateSuccess(string, object?, AgentStepInfo) instead
    public static AgentStep CreateSuccess(
        string output,
        object? structuredOutput,
        AgentId? agentId = null,
        TaskId? taskId = null,
        string action = "",
        string input = "",
        IReadOnlyList<string>? toolsUsed = null,
        AgentStepContext? context = null)
#pragma warning restore S107
    {
        return CreateSuccess(output, structuredOutput,
            new AgentStepInfo(agentId, taskId, action, input, toolsUsed, context));
    }

    /// <summary>
    /// Creates a failed agent step using grouped step info.
    /// </summary>
    public static AgentStep Failed(string error, AgentStepInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var startedAt = DateTime.UtcNow;
        return new AgentStep(
            id: AgentStepId.Create(),
            info: info,
            output: null,
            structuredOutput: null,
            success: false,
            error: error,
            startedAt: startedAt,
            completedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a failed agent step.
    /// Convenience overload that delegates to <see cref="Failed(string, AgentStepInfo)"/>.
    /// </summary>
    public static AgentStep Failed(
        string error,
        AgentId? agentId = null,
        TaskId? taskId = null,
        string action = "",
        string input = "",
        IReadOnlyList<string>? toolsUsed = null,
        AgentStepContext? context = null)
    {
        return Failed(error,
            new AgentStepInfo(agentId, taskId, action, input, toolsUsed, context));
    }
}
