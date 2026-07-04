using System.Text.Json;

namespace Orkeon.Application.Interfaces.LLM;

/// <summary>
/// Extracts tool calls from a raw LLM response and formats results
/// for multi-turn conversation history.
/// </summary>
public interface IToolCallParser
{
    /// <summary>
    /// Parses tool calls from the LLM response body.
    /// Returns an empty list when no tool calls are detected.
    /// </summary>
    IReadOnlyList<ParsedToolCall> ParseToolCalls(JsonElement responseBody);

    /// <summary>
    /// Formats the result of a tool execution as a message object
    /// ready to be appended to the conversation history.
    /// </summary>
    object FormatToolResult(ParsedToolCall toolCall, string result, bool success);

    /// <summary>
    /// Formats the original assistant message (containing tool calls)
    /// for re-injection into the conversation history (required for multi-turn).
    /// </summary>
    object FormatAssistantToolCallMessage(JsonElement responseBody);
}

/// <summary>
/// Represents a single tool call parsed from an LLM response.
/// </summary>
/// <param name="Id">Provider-assigned call identifier (generated if absent).</param>
/// <param name="ToolName">Name of the tool to invoke.</param>
/// <param name="Arguments">Parsed arguments for the tool.</param>
public record ParsedToolCall(
    string Id,
    string ToolName,
    Dictionary<string, object?> Arguments);
