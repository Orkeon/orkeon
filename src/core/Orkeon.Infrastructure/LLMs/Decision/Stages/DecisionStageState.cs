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
}
