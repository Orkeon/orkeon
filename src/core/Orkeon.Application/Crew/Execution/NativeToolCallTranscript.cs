using System.Text.Json;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The conversation turns of native tool calling, shared by the C# agent loop and the scripting
/// <c>ctx.llm.act</c> loop: the assistant turn that carries a response's tool calls, which the
/// provider replays in its own protocol.
/// </summary>
public static class NativeToolCallTranscript
{
    /// <summary>
    /// The assistant turn of a response whose body holds tool calls: its text, the raw calls
    /// (the Anthropic <c>tool_use</c> blocks, or the OpenAI <c>choices[0].message.tool_calls</c>
    /// array) and the reasoning trace a thinking provider needs replayed on the next turn.
    /// </summary>
    /// <param name="response">The provider's response.</param>
    /// <param name="responseBody">The parsed <see cref="LlmResponse.RawResponseBody"/>.</param>
    /// <returns>The message to append before the tool turns that answer the calls.</returns>
    public static LlmMessage AssistantTurn(LlmResponse response, JsonElement responseBody)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new LlmMessage
        {
            Role = "assistant",
            Content = response.Content ?? "",
            RawToolCalls = RawToolCalls(responseBody),
            ReasoningContent = ReasoningContent(response),
        };
    }

    private static string RawToolCalls(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return "";

        // Anthropic format: content[].type == "tool_use"
        if (root.TryGetProperty("content", out var contentArray) &&
            contentArray.ValueKind == JsonValueKind.Array)
        {
            var toolUseBlocks = new List<string>();
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object &&
                    block.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String &&
                    t.GetString() == "tool_use")
                    toolUseBlocks.Add(block.GetRawText());
            }
            if (toolUseBlocks.Count > 0)
                return "[" + string.Join(",", toolUseBlocks) + "]";
        }

        // OpenAI format: choices[0].message.tool_calls
        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].ValueKind == JsonValueKind.Object &&
            choices[0].TryGetProperty("message", out var msg) &&
            msg.ValueKind == JsonValueKind.Object &&
            msg.TryGetProperty("tool_calls", out var tc))
        {
            return tc.GetRawText();
        }

        return "";
    }

    /// <summary>
    /// The optional <c>reasoning_content</c> trace of the response. Thinking-mode providers
    /// (DeepSeek) require it to be replayed on the next turn — dropping it triggers an HTTP 400
    /// on the very next request.
    /// </summary>
    private static string? ReasoningContent(LlmResponse response)
    {
        if (response.Metadata is null) return null;
        if (!response.Metadata.TryGetValue("reasoning_content", out var raw)) return null;
        return raw as string;
    }
}
