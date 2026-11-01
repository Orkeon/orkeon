using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Decision.Stages;

/// <summary>What the built-in stages show the decision model: the request, and the answer once there is one.</summary>
internal static class DecisionStageState
{
    /// <summary>The last user message; the last message of any role when none is the user's.</summary>
    public static string Request(LlmMessage[] messages)
    {
        for (var i = messages.Length - 1; i >= 0; i--)
        {
            if (string.Equals(messages[i].Role, LlmRoles.User, StringComparison.OrdinalIgnoreCase))
                return messages[i].Content;
        }

        return messages.Length == 0 ? string.Empty : messages[^1].Content;
    }

    /// <summary>The request alone, as a string state.</summary>
    public static JsonNode OfRequest(LlmMessage[] messages) => JsonValue.Create(Request(messages));

    /// <summary>The request and the answer, as an object state.</summary>
    public static JsonNode OfExchange(LlmMessage[] messages, LlmResponse response) => new JsonObject
    {
        ["request"] = Request(messages),
        ["response"] = response.Content,
    };

    /// <summary>
    /// Whether <paramref name="response"/> calls tools: an OpenAI chat body with a non-empty
    /// <c>choices[0].message.tool_calls</c>, or an Anthropic Messages body with a <c>tool_use</c>
    /// block in <c>content[]</c> — the two shapes every provider returns or synthesizes. A
    /// response with no body, or a body that cannot be read, calls none.
    /// </summary>
    public static bool CallsTools(LlmResponse response)
    {
        if (string.IsNullOrEmpty(response.RawResponseBody))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(response.RawResponseBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                && choices[0].ValueKind == JsonValueKind.Object
                && choices[0].TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("tool_calls", out var calls)
                && calls.ValueKind == JsonValueKind.Array)
                return calls.GetArrayLength() > 0;
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                return content.EnumerateArray().Any(block =>
                    block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.ValueEquals("tool_use"));
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
