using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SML-009 / R12.5: conversation roles are matched case-insensitively everywhere. Before the
/// fix, <see cref="ConversationPolicy.TrimConversationHistory(System.Collections.Generic.List{LlmMessage}, int)"/>
/// compared the leading role with a case-sensitive <c>== "tool"</c>, so a mixed-case
/// <c>"Tool"</c> orphan (whose tool_use was trimmed away) survived and produced the very
/// orphan-tool_result that the trim exists to prevent.
/// </summary>
public class ConversationPolicyRoleCasingTests
{
    [Fact]
    public void TrimConversationHistory_ShouldDropLeadingToolMessage_RegardlessOfCase()
    {
        // Arrange — 8 messages, maxMessages = 5 → the kept "recent" window is the last 2,
        // whose head is a mixed-case "Tool" orphan. It must be trimmed.
        var orphanTool = new LlmMessage { Role = "Tool", Content = "orphan-tool-result", ToolCallId = "call-1" };
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("system"),
            LlmMessage.User("task"),
            LlmMessage.Assistant("a2"),
            LlmMessage.User("u3"),
            LlmMessage.Assistant("a4"),
            LlmMessage.User("u5"),
            orphanTool,
            LlmMessage.User("final-user"),
        };

        // Act
        ConversationPolicy.TrimConversationHistory(messages, maxMessages: 5);

        // Assert — the mixed-case tool orphan is gone (it survived on e91ef936's case-sensitive check).
        Assert.DoesNotContain(messages, m => m.Content == "orphan-tool-result");
        // system + user + summary notice + the single non-tool recent message
        Assert.Equal(4, messages.Count);
        Assert.Equal("final-user", messages[^1].Content);
    }
}
