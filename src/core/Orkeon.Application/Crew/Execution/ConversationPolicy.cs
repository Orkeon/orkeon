using Microsoft.Extensions.AI;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Stateless conversation-history policy shared by the three execution loops:
/// context-window trimming, tool-result truncation, circuit-breaker evaluation and
/// last-error / last-assistant-text extraction. Extracted verbatim from
/// <see cref="ExecutionOrchestrator"/> (R4.1).
/// </summary>
internal static class ConversationPolicy
{
    // ── Circuit breaker helpers ──────────────────────────────

    /// <summary>
    /// Extracts the last tool error from a ChatMessage list (IChatClient path).
    /// Returns the error text if the last tool result starts with "Error:", null otherwise.
    /// </summary>
    internal static string? ExtractLastToolError(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return null;

        // Walk backwards to find the last Tool message
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.Tool)
                continue;

            // Check FunctionResultContent items first (native function calling path)
            var funcResult = msg.Contents?.OfType<FunctionResultContent>().LastOrDefault();
            if (funcResult?.Result is string resultStr && resultStr.StartsWith("Error:", StringComparison.Ordinal))
                return resultStr;

            // Fallback to plain text content
            var text = msg.Text;
            if (!string.IsNullOrEmpty(text) && text.StartsWith("Error:", StringComparison.Ordinal))
                return text;

            // Found a tool message but it's not an error
            return null;
        }

        return null;
    }

    /// <summary>
    /// Extracts the last tool error from an LlmMessage list (native tool calling path).
    /// Returns the error text if the last tool result starts with "Error:", null otherwise.
    /// </summary>
    internal static string? ExtractLastToolError(IReadOnlyList<Domain.SharedKernel.ValueObjects.LlmMessage> messages)
    {
        if (messages.Count == 0)
            return null;

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (!LlmRoles.IsTool(msg.Role))
                continue;

            if (!string.IsNullOrEmpty(msg.Content) && msg.Content.StartsWith("Error:", StringComparison.Ordinal))
                return msg.Content;

            return null;
        }

        return null;
    }

    /// <summary>
    /// Extracts the last tool error from a StringBuilder conversation (legacy path).
    /// Returns the error text if the last tool result line contains an error, null otherwise.
    /// </summary>
    internal static string? ExtractLastToolErrorFromText(string conversationText)
    {
        if (string.IsNullOrEmpty(conversationText))
            return null;

        // Look for the last "[Tool ... error]:" or "[Tool ... result]: Error:" pattern
        var lines = conversationText.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Contains("[Tool ", StringComparison.Ordinal) && line.Contains(" error]:", StringComparison.Ordinal))
            {
                return line;
            }
            if (line.Contains("[Tool ", StringComparison.Ordinal) && line.Contains(" result]: Error:", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }

    /// <summary>
    /// Evaluates the circuit breaker state after a tool execution.
    /// Returns true if the circuit breaker has tripped (too many identical errors).
    /// </summary>
    internal static bool EvaluateCircuitBreaker(
        string? newError,
        ref string? lastErrorMessage,
        ref int consecutiveIdenticalErrors)
    {
        if (newError != null)
        {
            if (newError == lastErrorMessage)
            {
                consecutiveIdenticalErrors++;
            }
            else
            {
                lastErrorMessage = newError;
                consecutiveIdenticalErrors = 1;
            }

            return consecutiveIdenticalErrors >= AgentDefaults.MaxConsecutiveIdenticalErrors;
        }

        // Success — reset
        lastErrorMessage = null;
        consecutiveIdenticalErrors = 0;
        return false;
    }

    /// <summary>
    /// Builds the standard circuit breaker tripped result.
    /// When a partial output (typically the last assistant text) is supplied,
    /// it is appended to the result so dependent tasks can salvage what the
    /// agent produced before the breaker tripped.
    /// </summary>
    internal static AgentLoopResult BuildCircuitBreakerResult(
        int consecutiveErrors, string lastError, int totalTokensUsed, int iteration,
        string? partialOutput = null)
    {
        var header = $"Agent stopped after {consecutiveErrors} identical tool call failures. Error: {lastError}";
        var output = string.IsNullOrWhiteSpace(partialOutput)
            ? header
            : $"{header}\n\n--- Partial work before failure ---\n{partialOutput}";
        return new AgentLoopResult(output, totalTokensUsed, AgentExitReason.CircuitBreakerTripped,
            IterationsUsed: iteration + 1, LastError: lastError);
    }

    /// <summary>
    /// Extracts the most recent non-empty assistant text from a ChatMessage list.
    /// Used to surface partial work in circuit-breaker results so the next task
    /// in a sequential pipeline can build on whatever the agent already produced.
    /// </summary>
    internal static string? ExtractLastAssistantText(IReadOnlyList<ChatMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.Assistant)
                continue;

            var text = msg.Text;
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        return null;
    }

    /// <summary>
    /// Extracts the most recent non-empty assistant text from an LlmMessage list (legacy path).
    /// </summary>
    internal static string? ExtractLastAssistantText(IReadOnlyList<Domain.SharedKernel.ValueObjects.LlmMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (!LlmRoles.IsAssistant(msg.Role))
                continue;

            if (!string.IsNullOrWhiteSpace(msg.Content))
                return msg.Content;
        }
        return null;
    }

    // ── Context window management ──────────────────────────

    /// <summary>
    /// Trims conversation history to keep it bounded while preserving system and user messages.
    /// Preserves the first two messages (system + user task), injects a summary notice,
    /// and keeps the most recent messages within the budget.
    /// </summary>
    internal static void TrimConversationHistory(List<ChatMessage> messages, int maxMessages)
    {
        if (messages.Count <= maxMessages) return;

        // Preserve: [0] system, [1] user task, then the N most recent messages
        var systemMsg = messages[0];
        var userMsg = messages[1];
        var trimmedCount = messages.Count - maxMessages;
        var keepCount = maxMessages - 3; // 3 = system + user + summary notice
        var recentMessages = messages.Skip(messages.Count - keepCount).ToList();

        // Skip leading tool-role messages whose assistant (tool_use) was trimmed away.
        // Keeping them would produce orphan tool_results rejected by Anthropic/MiniMax APIs.
        while (recentMessages.Count > 0 && recentMessages[0].Role == ChatRole.Tool)
            recentMessages.RemoveAt(0);

        // Use "user" role so the notice stays in the conversation without overwriting the system prompt.
        var summaryMsg = new ChatMessage(ChatRole.User,
            $"[Context trimmed: {trimmedCount} earlier messages removed. Focus on the most recent tool results and the original task.]");

        messages.Clear();
        messages.Add(systemMsg);
        messages.Add(userMsg);
        messages.Add(summaryMsg);
        messages.AddRange(recentMessages);
    }

    /// <summary>
    /// Trims conversation history (LlmMessage variant) to keep it bounded while preserving system and user messages.
    /// Same logic as the ChatMessage overload, adapted for the native tool calling path.
    /// </summary>
    internal static void TrimConversationHistory(List<Domain.SharedKernel.ValueObjects.LlmMessage> messages, int maxMessages)
    {
        if (messages.Count <= maxMessages) return;

        var systemMsg = messages[0];
        var userMsg = messages[1];
        var trimmedCount = messages.Count - maxMessages;
        var keepCount = maxMessages - 3;
        var recentMessages = messages.Skip(messages.Count - keepCount).ToList();

        // Skip leading tool-role messages whose assistant (tool_use) was trimmed away.
        // Keeping them would produce orphan tool_results rejected by Anthropic/MiniMax APIs.
        while (recentMessages.Count > 0 && LlmRoles.IsTool(recentMessages[0].Role))
            recentMessages.RemoveAt(0);

        // Use "user" role so the notice stays in the conversation without overwriting the system prompt.
        var summaryMsg = Domain.SharedKernel.ValueObjects.LlmMessage.User(
            $"[Context trimmed: {trimmedCount} earlier messages removed. Focus on the most recent tool results and the original task.]");

        messages.Clear();
        messages.Add(systemMsg);
        messages.Add(userMsg);
        messages.Add(summaryMsg);
        messages.AddRange(recentMessages);
    }

    /// <summary>
    /// Truncates a tool result string to the specified maximum length.
    /// When truncation occurs, appends a notice with the count of omitted characters.
    /// </summary>
    internal static string TruncateToolResult(string result, int maxLength)
    {
        if (result.Length <= maxLength) return result;
        return result[..maxLength] +
            $"\n\n[... truncated, {result.Length - maxLength} chars omitted. Use more specific parameters to narrow results.]";
    }
}
