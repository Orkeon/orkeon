using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Execution;
using System.Text;

namespace Orkeon.Application.Crew;

/// <summary>
/// Internal static pass-throughs kept for source compatibility after the R4.1
/// decomposition. The logic now lives in the <c>Crew/Execution</c> collaborators
/// (<see cref="ConversationPolicy"/>, <see cref="AgentPromptComposer"/>,
/// <see cref="ToolCallTextParser"/>, <see cref="FinalAnswerPolicy"/>,
/// <see cref="ChatToolDispatcher"/>); existing callers — notably the
/// <c>Orkeon.Application.Tests</c> suite via <c>InternalsVisibleTo</c> — keep the
/// historical <c>ExecutionOrchestrator.*</c> entry points with identical signatures
/// and behavior.
/// </summary>
public partial class ExecutionOrchestrator
{
    /// <inheritdoc cref="ConversationPolicy.ExtractLastToolError(IReadOnlyList{ChatMessage})"/>
    internal static string? ExtractLastToolError(IReadOnlyList<ChatMessage> messages)
        => ConversationPolicy.ExtractLastToolError(messages);

    /// <inheritdoc cref="ConversationPolicy.ExtractLastToolError(IReadOnlyList{Domain.SharedKernel.ValueObjects.LlmMessage})"/>
    internal static string? ExtractLastToolError(IReadOnlyList<Domain.SharedKernel.ValueObjects.LlmMessage> messages)
        => ConversationPolicy.ExtractLastToolError(messages);

    /// <inheritdoc cref="ConversationPolicy.ExtractLastToolErrorFromText(string)"/>
    internal static string? ExtractLastToolErrorFromText(string conversationText)
        => ConversationPolicy.ExtractLastToolErrorFromText(conversationText);

    /// <inheritdoc cref="ConversationPolicy.ExtractLastAssistantText(IReadOnlyList{ChatMessage})"/>
    internal static string? ExtractLastAssistantText(IReadOnlyList<ChatMessage> messages)
        => ConversationPolicy.ExtractLastAssistantText(messages);

    /// <inheritdoc cref="ConversationPolicy.ExtractLastAssistantText(IReadOnlyList{Domain.SharedKernel.ValueObjects.LlmMessage})"/>
    internal static string? ExtractLastAssistantText(IReadOnlyList<Domain.SharedKernel.ValueObjects.LlmMessage> messages)
        => ConversationPolicy.ExtractLastAssistantText(messages);

    /// <inheritdoc cref="ConversationPolicy.TrimConversationHistory(List{ChatMessage}, int)"/>
    internal static void TrimConversationHistory(List<ChatMessage> messages, int maxMessages)
        => ConversationPolicy.TrimConversationHistory(messages, maxMessages);

    /// <inheritdoc cref="ConversationPolicy.TrimConversationHistory(List{Domain.SharedKernel.ValueObjects.LlmMessage}, int)"/>
    internal static void TrimConversationHistory(List<Domain.SharedKernel.ValueObjects.LlmMessage> messages, int maxMessages)
        => ConversationPolicy.TrimConversationHistory(messages, maxMessages);

    /// <inheritdoc cref="ConversationPolicy.TruncateToolResult(string, int)"/>
    internal static string TruncateToolResult(string result, int maxLength)
        => ConversationPolicy.TruncateToolResult(result, maxLength);

    /// <inheritdoc cref="AgentPromptComposer.AppendToolsSection(StringBuilder, DomainAgent, bool)"/>
    internal static void AppendToolsSection(StringBuilder prompt, DomainAgent agent, bool supportsNativeToolCalling)
        => AgentPromptComposer.AppendToolsSection(prompt, agent, supportsNativeToolCalling);

    /// <inheritdoc cref="ToolCallTextParser.UnescapeLlmText(string)"/>
    internal static string UnescapeLlmText(string text)
        => ToolCallTextParser.UnescapeLlmText(text);

    /// <inheritdoc cref="FinalAnswerPolicy.BuildFinalAnswerNudge(bool)"/>
    internal static string BuildFinalAnswerNudge(bool escalated)
        => FinalAnswerPolicy.BuildFinalAnswerNudge(escalated);

    /// <inheritdoc cref="FinalAnswerPolicy.IsToolCallMimicry(string)"/>
    internal static bool IsToolCallMimicry(string text)
        => FinalAnswerPolicy.IsToolCallMimicry(text);

    /// <inheritdoc cref="FinalAnswerPolicy.CloneForToolFreeRetry(ChatOptions)"/>
    internal static ChatOptions CloneForToolFreeRetry(ChatOptions source)
        => FinalAnswerPolicy.CloneForToolFreeRetry(source);

    /// <inheritdoc cref="ChatToolDispatcher.BuildMissingToolMessage(string, string)"/>
    internal static ChatMessage BuildMissingToolMessage(string callId, string toolName)
        => ChatToolDispatcher.BuildMissingToolMessage(callId, toolName);
}
