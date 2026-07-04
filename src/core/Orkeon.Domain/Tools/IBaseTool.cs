using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Domain.Tools;

/// <summary>
/// Base interface for all tools that can be used by agents.
/// </summary>
public interface IBaseTool
{
    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description of what the tool does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the JSON schema defining the tool's parameters and return type.
    /// </summary>
    ToolSchema Schema { get; }

    /// <summary>
    /// Declared access classification consumed by permission gates. Defaults to
    /// <see cref="ToolAccess.Unspecified"/> so existing implementations keep compiling;
    /// gates then fall back to their own fail-closed classification.
    /// </summary>
    ToolAccess Access => ToolAccess.Unspecified;

    /// <summary>
    /// Executes the tool with the given request.
    /// </summary>
    /// <param name="request">The tool call request with parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the tool execution.</returns>
    Task<ToolCallResponse> CallAsync(Protocol.ToolCallRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the tool with the given input (legacy method for compatibility).
    /// </summary>
    /// <param name="input">The input for the tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the tool execution.</returns>
    Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if the input is valid for this tool.
    /// </summary>
    /// <param name="input">The input to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool ValidateInput(string input);
}

/// <summary>
/// Result of a tool execution.
/// </summary>
public class ToolResult
{
    /// <summary>Gets whether the tool executed successfully.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the output text, or <see langword="null"/> on failure.</summary>
    public string? Output { get; init; }
    /// <summary>Gets the error message, or <see langword="null"/> on success.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the tool usage metrics.</summary>
    public ToolUsageMetrics? Usage { get; init; }

    /// <summary>Creates a successful tool result.</summary>
    /// <param name="output">The output text.</param>
    /// <param name="usage">Optional usage metrics.</param>
    /// <returns>A successful <see cref="ToolResult"/>.</returns>
    public static ToolResult CreateSuccess(string output, ToolUsageMetrics? usage = null)
    {
        return new ToolResult
        {
            Success = true,
            Output = output,
            Usage = usage
        };
    }

    /// <summary>Creates a failed tool result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed <see cref="ToolResult"/>.</returns>
    public static ToolResult CreateError(string error)
    {
        return new ToolResult
        {
            Success = false,
            Error = error
        };
    }
}

/// <summary>
/// Tool usage metrics.
/// </summary>
public class ToolUsageMetrics
{
    /// <summary>Gets the number of tokens used.</summary>
    public int TokensUsed { get; init; }
    /// <summary>Gets the execution time in milliseconds.</summary>
    public double ExecutionTimeMs { get; init; }
    /// <summary>Gets the number of API calls made.</summary>
    public int ApiCalls { get; init; }
    /// <summary>Gets the estimated cost.</summary>
    public double Cost { get; init; }
}
