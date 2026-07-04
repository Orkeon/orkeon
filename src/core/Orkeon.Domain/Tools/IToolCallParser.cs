using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tools;

/// <summary>
/// Interface for parsing tool calls from LLM responses.
/// </summary>
public interface IToolCallParser
{
    /// <summary>
    /// Parses a tool call from an LLM response string.
    /// </summary>
    /// <param name="response">The LLM response containing a tool call.</param>
    /// <returns>The parsed tool call, or null if no valid tool call found.</returns>
    ToolCall? ParseFromLlmResponse(string response);

    /// <summary>
    /// Formats a tool call for sending to an LLM.
    /// </summary>
    /// <param name="toolCall">The tool call to format.</param>
    /// <returns>The formatted string representation.</returns>
    string FormatForLlm(ToolCall toolCall);
}
