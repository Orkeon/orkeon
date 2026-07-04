using Microsoft.Extensions.AI;
using Orkeon.Application.Crew;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Tests for context window management in ExecutionOrchestrator:
/// sliding window trimming and tool result truncation.
/// </summary>
public class ExecutionOrchestratorContextManagementTests
{
    // ── TrimConversationHistory (ChatMessage overload) ──────

    [Fact]
    public void TrimConversationHistory_UnderLimit_NoChange()
    {
        // Arrange
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "User task"),
            new(ChatRole.Assistant, "Response 1"),
            new(ChatRole.Tool, "Tool result 1"),
        };
        var originalCount = messages.Count;

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert
        Assert.Equal(originalCount, messages.Count);
    }

    [Fact]
    public void TrimConversationHistory_ExactlyAtLimit_NoChange()
    {
        // Arrange
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "User task"),
            new(ChatRole.Assistant, "Response 1"),
            new(ChatRole.Tool, "Tool result 1"),
            new(ChatRole.Assistant, "Response 2"),
            new(ChatRole.Tool, "Tool result 2"),
        };

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 6);

        // Assert
        Assert.Equal(6, messages.Count);
    }

    [Fact]
    public void TrimConversationHistory_OverLimit_PreservesSystemAndUser()
    {
        // Arrange
        var systemContent = "You are a helpful assistant.";
        var userContent = "Analyze this data set.";
        var messages = BuildLargeConversation(systemContent, userContent, 30);

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert
        Assert.Equal(systemContent, messages[0].Text);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(userContent, messages[1].Text);
        Assert.Equal(ChatRole.User, messages[1].Role);
    }

    [Fact]
    public void TrimConversationHistory_OverLimit_KeepsRecentMessages()
    {
        // Arrange
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System"),
            new(ChatRole.User, "User"),
        };
        // Add 20 pairs = 40 messages + 2 header = 42 total
        for (int i = 0; i < 20; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, $"Assistant-{i}"));
            messages.Add(new ChatMessage(ChatRole.Tool, $"Tool-{i}"));
        }

        // Act — trim to 10 messages: system + user + summary + 7 recent
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert — the last message should be the most recent tool result
        var lastMsg = messages[^1];
        Assert.Equal("Tool-19", lastMsg.Text);

        // And the second-to-last should be the most recent assistant message
        var secondToLast = messages[^2];
        Assert.Equal("Assistant-19", secondToLast.Text);
    }

    [Fact]
    public void TrimConversationHistory_OverLimit_InjectsTrimNotice()
    {
        // Arrange
        var messages = BuildLargeConversation("System", "User", 30);

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert — third message (index 2) should be the trim notice as user role
        Assert.Equal(ChatRole.User, messages[2].Role);
        Assert.Contains("Context trimmed", messages[2].Text);
        Assert.Contains("earlier messages removed", messages[2].Text);
    }

    [Fact]
    public void TrimConversationHistory_OverLimit_RespectsBound()
    {
        // Arrange
        var messages = BuildLargeConversation("System", "User", 50);

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, AgentDefaults.MaxContextMessages);

        // Assert
        Assert.True(messages.Count <= AgentDefaults.MaxContextMessages);
    }

    [Fact]
    public void TrimConversationHistory_OverLimit_TrimNoticeShowsCorrectCount()
    {
        // Arrange — 2 header + 40 conversation = 42 total messages
        var messages = BuildLargeConversation("System", "User", 20);
        var originalCount = messages.Count; // 42

        // Act — trim to 10
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert — trimmed count should be original - maxMessages = 32
        var expectedTrimmedCount = originalCount - 10;
        Assert.Contains(expectedTrimmedCount.ToString(), messages[2].Text);
    }

    // ── TrimConversationHistory (LlmMessage overload) ──────

    [Fact]
    public void TrimConversationHistory_LlmMessage_UnderLimit_NoChange()
    {
        // Arrange
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("System prompt"),
            LlmMessage.User("User task"),
            LlmMessage.Assistant("Response 1"),
        };
        var originalCount = messages.Count;

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert
        Assert.Equal(originalCount, messages.Count);
    }

    [Fact]
    public void TrimConversationHistory_LlmMessage_OverLimit_PreservesSystemAndUser()
    {
        // Arrange
        var messages = BuildLargeLlmConversation("System prompt", "User task", 30);

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("System prompt", messages[0].Content);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("User task", messages[1].Content);
    }

    [Fact]
    public void TrimConversationHistory_LlmMessage_OverLimit_InjectsTrimNotice()
    {
        // Arrange
        var messages = BuildLargeLlmConversation("System", "User", 30);

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert — trim notice uses user role to avoid overwriting system prompt
        Assert.Equal("user", messages[2].Role);
        Assert.Contains("Context trimmed", messages[2].Content);
    }

    [Fact]
    public void TrimConversationHistory_LlmMessage_OverLimit_RespectsBound()
    {
        // Arrange
        var messages = BuildLargeLlmConversation("System", "User", 50);

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, AgentDefaults.MaxContextMessages);

        // Assert
        Assert.True(messages.Count <= AgentDefaults.MaxContextMessages);
    }

    // ── TrimConversationHistory — orphan tool messages ────────────

    [Fact]
    public void TrimConversationHistory_OverLimit_DropsOrphanLeadingToolMessages()
    {
        // Arrange — build a conversation where trimming would start on a tool message.
        // Structure: system + user + (assistant with 10 tool results) x 3 = 2 + 33 = 35
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System"),
            new(ChatRole.User, "User"),
        };
        for (int batch = 0; batch < 3; batch++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, $"Batch-{batch}"));
            for (int t = 0; t < 10; t++)
                messages.Add(new ChatMessage(ChatRole.Tool, $"Result-{batch}-{t}"));
        }
        // Total: 2 + 3*(1+10) = 35 messages

        // Act — trim to 10: keepCount = 7, skip 28 -> starts at index 28
        // Index 28 is a Tool message from batch-2 -> must be dropped
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert — no tool message should appear before the first assistant message (after header)
        var afterHeader = messages.Skip(3).ToList(); // skip system + user + trim notice
        if (afterHeader.Count > 0)
        {
            Assert.NotEqual(ChatRole.Tool, afterHeader[0].Role);
        }
    }

    [Fact]
    public void TrimConversationHistory_LlmMessage_OverLimit_DropsOrphanLeadingToolMessages()
    {
        // Arrange — same structure for LlmMessage overload
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("System"),
            LlmMessage.User("User"),
        };
        for (int batch = 0; batch < 3; batch++)
        {
            messages.Add(LlmMessage.Assistant($"Batch-{batch}"));
            for (int t = 0; t < 10; t++)
                messages.Add(new LlmMessage { Role = "tool", Content = $"Result-{batch}-{t}", ToolCallId = $"call_{batch}_{t}" });
        }

        // Act
        ExecutionOrchestrator.TrimConversationHistory(messages, 10);

        // Assert — no orphan tool message at start of recent messages
        var afterHeader = messages.Skip(3).ToList();
        if (afterHeader.Count > 0)
        {
            Assert.NotEqual("tool", afterHeader[0].Role);
        }
    }

    // ── TruncateToolResult ─────────────────────────────────────

    [Fact]
    public void TruncateToolResult_ShortResult_Unchanged()
    {
        // Arrange
        var shortResult = "File content: hello world";

        // Act
        var output = ExecutionOrchestrator.TruncateToolResult(shortResult, AgentDefaults.MaxToolResultLength);

        // Assert
        Assert.Equal(shortResult, output);
    }

    [Fact]
    public void TruncateToolResult_ExactlyAtLimit_Unchanged()
    {
        // Arrange
        var exactResult = new string('x', AgentDefaults.MaxToolResultLength);

        // Act
        var output = ExecutionOrchestrator.TruncateToolResult(exactResult, AgentDefaults.MaxToolResultLength);

        // Assert
        Assert.Equal(exactResult, output);
    }

    [Fact]
    public void TruncateToolResult_LongResult_Truncates()
    {
        // Arrange
        var longResult = new string('A', 10_000);

        // Act
        var output = ExecutionOrchestrator.TruncateToolResult(longResult, AgentDefaults.MaxToolResultLength);

        // Assert
        Assert.True(output.Length < longResult.Length);
        Assert.StartsWith(new string('A', AgentDefaults.MaxToolResultLength), output);
        Assert.Contains("truncated", output);
    }

    [Fact]
    public void TruncateToolResult_LongResult_ShowsOmittedCount()
    {
        // Arrange
        var totalLength = 10_000;
        var longResult = new string('B', totalLength);
        var expectedOmitted = totalLength - AgentDefaults.MaxToolResultLength;

        // Act
        var output = ExecutionOrchestrator.TruncateToolResult(longResult, AgentDefaults.MaxToolResultLength);

        // Assert
        Assert.Contains($"{expectedOmitted} chars omitted", output);
    }

    [Fact]
    public void TruncateToolResult_LongResult_ContainsNarrowingAdvice()
    {
        // Arrange
        var longResult = new string('C', 10_000);

        // Act
        var output = ExecutionOrchestrator.TruncateToolResult(longResult, AgentDefaults.MaxToolResultLength);

        // Assert
        Assert.Contains("Use more specific parameters to narrow results", output);
    }

    [Fact]
    public void ResolveMaxToolResultLength_FileRead_ReturnsOverride()
    {
        // file_read serves trusted Markdown deliverables to synthesizer agents;
        // the default 4000-char cap forced re-read loops on round 30 trpc.
        Assert.Equal(32_000, AgentDefaults.ResolveMaxToolResultLength("file_read"));
    }

    [Fact]
    public void ResolveMaxToolResultLength_FileRead_IsCaseInsensitive()
    {
        Assert.Equal(32_000, AgentDefaults.ResolveMaxToolResultLength("File_Read"));
    }

    [Fact]
    public void ResolveMaxToolResultLength_UnlistedTool_FallsBackToDefault()
    {
        Assert.Equal(AgentDefaults.MaxToolResultLength,
            AgentDefaults.ResolveMaxToolResultLength("directory_read"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveMaxToolResultLength_NullOrEmpty_FallsBackToDefault(string? toolName)
    {
        Assert.Equal(AgentDefaults.MaxToolResultLength,
            AgentDefaults.ResolveMaxToolResultLength(toolName!));
    }

    // ── Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Builds a conversation with system + user header and N pairs of assistant+tool messages.
    /// Total message count = 2 + (pairs * 2).
    /// </summary>
    private static List<ChatMessage> BuildLargeConversation(string systemContent, string userContent, int pairs)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemContent),
            new(ChatRole.User, userContent),
        };

        for (int i = 0; i < pairs; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, $"Assistant response {i}"));
            messages.Add(new ChatMessage(ChatRole.Tool, $"Tool result {i}"));
        }

        return messages;
    }

    /// <summary>
    /// Builds a LlmMessage conversation with system + user header and N pairs of assistant+tool messages.
    /// </summary>
    private static List<LlmMessage> BuildLargeLlmConversation(string systemContent, string userContent, int pairs)
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.System(systemContent),
            LlmMessage.User(userContent),
        };

        for (int i = 0; i < pairs; i++)
        {
            messages.Add(LlmMessage.Assistant($"Assistant response {i}"));
            messages.Add(new LlmMessage { Role = "tool", Content = $"Tool result {i}", ToolCallId = $"call_{i}" });
        }

        return messages;
    }
}
