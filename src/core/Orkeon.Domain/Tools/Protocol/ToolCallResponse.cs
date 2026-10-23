namespace Orkeon.Domain.Tools.Protocol;

/// <summary>Represents the result of a tool call execution.</summary>
/// <param name="Success">Indicates whether the tool call succeeded.</param>
/// <param name="Result">The result returned by the tool, or null on failure.</param>
/// <param name="Error">The error message if the call failed, otherwise null.</param>
/// <param name="Metadata">Optional metadata associated with the response.</param>
public record ToolCallResponse(
    bool Success,
    object? Result,
    string? Error,
    Dictionary<string, object?>? Metadata = null
)
{
    /// <summary>True when the tool already bounded its own output; the invocation pipeline then does not truncate it again.</summary>
    public bool AlreadyBounded { get; init; }
}
