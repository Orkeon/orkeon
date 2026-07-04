using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Task;

/// <summary>
/// Groups optional output and callback parameters for task creation.
/// Reduces parameter count in Task constructors and factory methods.
/// </summary>
public sealed record TaskOutputOptions
{
    /// <summary>
    /// Gets whether asynchronous execution is enabled.
    /// </summary>
    public bool AsyncExecution { get; init; }

    /// <summary>
    /// Gets the JSON schema for output validation.
    /// </summary>
    public JsonSchema? OutputJson { get; init; }

    /// <summary>
    /// Gets the output type for structured data.
    /// </summary>
    public Type? OutputPydantic { get; init; }

    /// <summary>
    /// Gets the output file path.
    /// </summary>
    public string? OutputFile { get; init; }

    /// <summary>
    /// Gets the task callback.
    /// </summary>
    public ITaskCallback? Callback { get; init; }

    /// <summary>
    /// Gets whether human input is required.
    /// </summary>
    public bool HumanInput { get; init; }

    /// <summary>
    /// Declarative deliverable contract (path + production mode).
    /// When null, the task has no framework-managed deliverable and falls back to legacy tool_call behavior.
    /// </summary>
    public TaskDeliverable? Deliverable { get; init; }

    /// <summary>
    /// Default options with no output configuration.
    /// </summary>
    public static readonly TaskOutputOptions Default = new();
}
