using Microsoft.Extensions.AI;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Stateless policy for the tool-free "produce the final deliverable now" retry:
/// nudge wording, DeepSeek DSML tool-call mimicry detection, and the tool-free
/// <see cref="ChatOptions"/> clone. Extracted verbatim from
/// <see cref="ExecutionOrchestrator"/> (R4.1).
/// </summary>
internal static class FinalAnswerPolicy
{
    /// <summary>
    /// Builds the "produce the final deliverable now" nudge sent on the no-tools retry.
    /// Positive framing + explicit syntactic anchor — prior wording ("Do not call any tool")
    /// was too weak for DeepSeek thinking-mode models that had spent dozens of turns calling
    /// tools and continued the pattern in their native DSML markup as plain text content.
    /// </summary>
    internal static string BuildFinalAnswerNudge(bool escalated)
    {
        if (!escalated)
        {
            return
                "You have reached the iteration ceiling for this task. No further tool calls " +
                "are accepted on this turn — the API has stripped the tools array from the " +
                "request. Produce the task's final deliverable now as plain Markdown text. " +
                "Your response must be the complete deliverable in one turn (no tool calls, " +
                "no special tokens, no XML invoke tags, no DSML markers). " +
                "Begin your response with a Markdown heading (`# `, `## `, …) or directly with " +
                "the deliverable's first sentence.";
        }

        return
            "Your previous turn echoed tool-call markup (DSML / `<invoke>` / `<｜｜DSML｜｜>`). " +
            "That markup will be rejected and the task will fail with an empty deliverable. " +
            "Re-emit ONLY the deliverable's body as plain Markdown. Do not paraphrase a tool " +
            "call. Do not name any tool. Do not include angle brackets at the start of your " +
            "response. The first non-whitespace character of your response must be `#` (a " +
            "Markdown heading), a letter, or a digit.";
    }

    /// <summary>
    /// Detects DeepSeek-style tool-call mimicry in plain-text responses. Thinking-mode models
    /// occasionally emit their native special tokens — <c>&lt;｜｜DSML｜｜tool_calls&gt;</c>,
    /// <c>&lt;invoke name=…&gt;</c>, etc. — when forbidden from calling tools, instead of
    /// producing the requested final answer.
    /// </summary>
    internal static bool IsToolCallMimicry(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.TrimStart();
        // The literal DSML special token emitted by DeepSeek (e.g. "<｜｜DSML｜｜tool_calls>").
        if (trimmed.Contains("DSML", StringComparison.Ordinal)) return true;
        // Generic tool-call markup ("<tool_calls>", "<invoke name=…>") — still not a deliverable.
        if (trimmed.StartsWith("<tool_calls", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("<invoke", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Produces a copy of <paramref name="source"/> with tool invocation disabled: empty
    /// <see cref="ChatOptions.Tools"/> and the Orkeon-specific <c>orkeon:tool_mode</c> set to
    /// <see cref="Domain.Tools.Protocol.ToolCallMode.None"/> so the HTTP adapters omit
    /// <c>tool_choice</c> on the wire.
    /// </summary>
    internal static ChatOptions CloneForToolFreeRetry(ChatOptions source)
    {
        var clone = new ChatOptions
        {
            Temperature = source.Temperature,
            MaxOutputTokens = source.MaxOutputTokens,
            TopP = source.TopP,
            TopK = source.TopK,
            FrequencyPenalty = source.FrequencyPenalty,
            PresencePenalty = source.PresencePenalty,
            ResponseFormat = source.ResponseFormat,
            Tools = [],
            ToolMode = ChatToolMode.None,
        };

        if (source.AdditionalProperties is not null)
        {
            clone.AdditionalProperties = new AdditionalPropertiesDictionary(source.AdditionalProperties);
            clone.AdditionalProperties.Remove("orkeon:tool_schemas");
            clone.AdditionalProperties["orkeon:tool_mode"] = Domain.Tools.Protocol.ToolCallMode.None;
        }

        return clone;
    }
}
