using Orkeon.Domain.Memory.ValueObjects;

namespace Orkeon.Domain.Tools;

/// <summary>Groups the identity fields for a tool invocation (who called what for which task).</summary>
/// <param name="ToolId">The identifier of the tool.</param>
/// <param name="ToolName">The name of the tool.</param>
/// <param name="AgentId">The identifier of the calling agent.</param>
/// <param name="TaskId">The identifier of the associated task.</param>
public sealed record ToolCallIdentity(
    string ToolId,
    string ToolName,
    string AgentId,
    string TaskId)
{
    /// <summary>Validates that all identity fields are non-null.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(ToolId);
        ArgumentNullException.ThrowIfNull(ToolName);
        ArgumentNullException.ThrowIfNull(AgentId);
        ArgumentNullException.ThrowIfNull(TaskId);
    }
}

/// <summary>Represents usage statistics and metadata for a tool.</summary>
public class ToolUsage
{
    /// <summary>Gets the identifier of the tool.</summary>
    public string ToolId { get; }
    /// <summary>Gets the name of the tool.</summary>
    public string ToolName { get; }
    /// <summary>Gets the timestamp when the tool was used.</summary>
    public DateTime UsedAt { get; }
    /// <summary>Gets the identifier of the agent that used the tool.</summary>
    public string AgentId { get; }
    /// <summary>Gets the identifier of the task for which the tool was used.</summary>
    public string TaskId { get; }
    /// <summary>Gets the duration of the tool execution.</summary>
    public TimeSpan Duration { get; }
    /// <summary>Gets a value indicating whether the tool call succeeded.</summary>
    public bool Success { get; }
    /// <summary>Gets the error message if the tool call failed, otherwise null.</summary>
    public string? Error { get; }
    /// <summary>Gets the metadata associated with this tool usage.</summary>
    public ToolUsageMetadata Metadata { get; }

    /// <summary>Initializes a new instance of <see cref="ToolUsage"/>.</summary>
    /// <param name="identity">The identity information for this tool call.</param>
    /// <param name="duration">The duration of the execution.</param>
    /// <param name="success">Whether the call succeeded.</param>
    /// <param name="error">The error message if the call failed.</param>
    /// <param name="metadata">Additional metadata.</param>
    public ToolUsage(
        ToolCallIdentity identity,
        TimeSpan duration,
        bool success,
        string? error = null,
        ToolUsageMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        ToolId = identity.ToolId;
        ToolName = identity.ToolName;
        AgentId = identity.AgentId;
        TaskId = identity.TaskId;
        UsedAt = DateTime.UtcNow;
        Duration = duration;
        Success = success;
        Error = error;
        Metadata = metadata ?? ToolUsageMetadata.Empty;
    }

    /// <summary>Creates a successful tool usage record.</summary>
    /// <param name="identity">The identity information for this tool call.</param>
    /// <param name="duration">The duration of the execution.</param>
    /// <param name="metadata">Additional metadata.</param>
    /// <returns>A successful <see cref="ToolUsage"/>.</returns>
    public static ToolUsage CreateSuccess(
        ToolCallIdentity identity,
        TimeSpan duration,
        ToolUsageMetadata? metadata = null)
    {
        return new ToolUsage(identity, duration, true, null, metadata);
    }

    /// <summary>Creates a failed tool usage record.</summary>
    /// <param name="identity">The identity information for this tool call.</param>
    /// <param name="duration">The duration of the execution.</param>
    /// <param name="error">The error message.</param>
    /// <param name="metadata">Additional metadata.</param>
    /// <returns>A failed <see cref="ToolUsage"/>.</returns>
    public static ToolUsage CreateFailure(
        ToolCallIdentity identity,
        TimeSpan duration,
        string error,
        ToolUsageMetadata? metadata = null)
    {
        return new ToolUsage(identity, duration, false, error, metadata);
    }
}
