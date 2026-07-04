using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Represents a structured tool call with strongly typed arguments.
/// </summary>
public sealed record ToolCall(
    string ToolName,
    string CallId,
    ToolArguments Arguments) : ValueObjectRecord
{
    /// <summary>
    /// Creates a new tool call with auto-generated ID.
    /// </summary>
    public static ToolCall Create(string toolName, ToolArguments args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        return new ToolCall(toolName, Guid.NewGuid().ToString(), args ?? ToolArguments.Empty);
    }


}
