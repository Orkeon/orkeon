using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Parses tool calls from Anthropic Messages API responses and formats tool results
/// for multi-turn conversation history.
/// </summary>
/// <remarks>
/// Anthropic returns tool calls as <c>content</c> blocks with <c>type == "tool_use"</c>.
/// Unlike OpenAI, the <c>input</c> field is already a JSON object (not a JSON string).
/// Tool results must be wrapped in a <c>user</c> message with <c>tool_result</c> content blocks.
/// </remarks>
public sealed class AnthropicToolCallParser : IToolCallParser
{
    /// <inheritdoc />
    public IReadOnlyList<ParsedToolCall> ParseToolCalls(JsonElement responseBody)
    {
        var calls = new List<ParsedToolCall>();

        if (!responseBody.TryGetProperty("content", out var contentArray)
            || contentArray.ValueKind != JsonValueKind.Array)
        {
            return calls;
        }

        foreach (var block in contentArray.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeProp)
                && typeProp.GetString() == "tool_use")
            {
                var id = block.GetProperty("id").GetString() ?? string.Empty;
                var name = block.GetProperty("name").GetString() ?? string.Empty;

                // Anthropic provides input as a JSON object directly (not a string)
                var arguments = ParseJsonObjectToDictionary(block.GetProperty("input"));

                calls.Add(new ParsedToolCall(id, name, arguments));
            }
        }

        return calls;
    }

    /// <inheritdoc />
    public object FormatToolResult(ParsedToolCall toolCall, string result, bool success)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        return new Dictionary<string, object>
        {
            ["role"] = "user",
            ["content"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = toolCall.Id,
                    ["content"] = success ? result : $"Error: {result}"
                }
            }
        };
    }

    /// <inheritdoc />
    public object FormatAssistantToolCallMessage(JsonElement responseBody)
    {
        return new Dictionary<string, object>
        {
            ["role"] = "assistant",
            ["content"] = JsonSerializer.Deserialize<List<object>>(
                responseBody.GetProperty("content").GetRawText())!
        };
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> object into a <see cref="Dictionary{TKey,TValue}"/>.
    /// Handles nested objects and arrays recursively.
    /// </summary>
    private static Dictionary<string, object?> ParseJsonObjectToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();

        if (element.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = ConvertJsonElement(property.Value);
        }

        return dict;
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> to the appropriate CLR type.
    /// </summary>
    private static object ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
        JsonValueKind.Object => ParseJsonObjectToDictionary(element),
        _ => element.GetRawText()
    };
}
