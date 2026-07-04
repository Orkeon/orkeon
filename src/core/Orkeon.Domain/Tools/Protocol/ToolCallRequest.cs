namespace Orkeon.Domain.Tools.Protocol;

/// <summary>Represents a request to invoke a tool with specified parameters.</summary>
/// <param name="ToolName">The name of the tool to invoke.</param>
/// <param name="Parameters">The input parameters for the tool.</param>
/// <param name="Context">Optional contextual information for the call.</param>
public record ToolCallRequest(
    string ToolName,
    Dictionary<string, object?> Parameters,
    string? Context = null
);
