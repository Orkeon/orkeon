using System.Text.Json;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Tests for AnthropicLlmProvider.SeparateSystemMessages — ensures correct
/// conversion from LlmMessage[] to Anthropic API message format, especially
/// around orphan tool_results and system message handling.
/// </summary>
public class SeparateSystemMessagesTests
{
    private static readonly LlmConfig DefaultConfig = LlmConfig.Create("claude-3-opus", "test-key");

    [Fact]
    public void ShouldFilterOrphanToolResults_WhenNoMatchingToolUse()
    {
        // Arrange — a tool_result whose tool_use was trimmed away
        var messages = new[]
        {
            LlmMessage.System("System prompt"),
            LlmMessage.User("Task"),
            new LlmMessage { Role = "tool", Content = "orphan result", ToolCallId = "orphan_id_1" },
        };

        // Act
        var (conversation, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // Assert — only the user message should be present, orphan tool_result dropped
        Assert.Single(conversation);
        var json = JsonSerializer.Serialize(conversation[0]);
        Assert.Contains("Task", json);
        Assert.DoesNotContain("orphan_id_1", json);
    }

    [Fact]
    public void ShouldKeepMatchingToolResults_WhenToolUseExists()
    {
        // Arrange — assistant has a tool_use, followed by matching tool_result
        var rawToolCalls = JsonSerializer.Serialize(new[]
        {
            new { id = "call_1", function = new { name = "file_read", arguments = "{}" } }
        });

        var messages = new[]
        {
            LlmMessage.System("System prompt"),
            LlmMessage.User("Task"),
            new LlmMessage { Role = "assistant", Content = "Let me read.", RawToolCalls = rawToolCalls },
            new LlmMessage { Role = "tool", Content = "file contents", ToolCallId = "call_1" },
        };

        // Act
        var (conversation, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // Assert — 3 messages: user + assistant + user(tool_result)
        Assert.Equal(3, conversation.Count);
        var lastJson = JsonSerializer.Serialize(conversation[2]);
        Assert.Contains("call_1", lastJson);
        Assert.Contains("file contents", lastJson);
    }

    [Fact]
    public void ShouldFilterOrphanFromMixedBatch_WhenSomeToolResultsMatch()
    {
        // Arrange — batch of tool_results: one matches, one is orphan
        var rawToolCalls = JsonSerializer.Serialize(new[]
        {
            new { id = "call_valid", function = new { name = "dir_read", arguments = "{}" } }
        });

        var messages = new[]
        {
            LlmMessage.System("System prompt"),
            LlmMessage.User("Task"),
            new LlmMessage { Role = "tool", Content = "orphan result", ToolCallId = "call_orphan" },
            new LlmMessage { Role = "assistant", Content = "", RawToolCalls = rawToolCalls },
            new LlmMessage { Role = "tool", Content = "valid result", ToolCallId = "call_valid" },
        };

        // Act
        var (conversation, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // Assert — orphan dropped, valid kept
        var fullJson = JsonSerializer.Serialize(conversation);
        Assert.DoesNotContain("call_orphan", fullJson);
        Assert.Contains("call_valid", fullJson);
    }

    [Fact]
    public void ShouldPreserveFirstSystemMessage_WhenMultipleSystemMessages()
    {
        // Arrange — system prompt + trim notice (also system role)
        var messages = new[]
        {
            LlmMessage.System("You are a helpful assistant."),
            LlmMessage.User("Task"),
            LlmMessage.System("[Context trimmed: 10 earlier messages removed.]"),
        };

        // Act
        var (conversation, systemMessage) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // Assert — first system message preserved as system, second converted to user
        Assert.Equal("You are a helpful assistant.", systemMessage);
        // The trim notice should be in conversation as a user message
        Assert.Equal(2, conversation.Count);
        var trimNoticeJson = JsonSerializer.Serialize(conversation[1]);
        Assert.Contains("Context trimmed", trimNoticeJson);
    }

    [Fact]
    public void ShouldReplayToolUseBlocks_WhenRawToolCallsHoldTheAnthropicShape()
    {
        // Arrange — RawToolCalls as NativeToolCallTranscript stores an Anthropic body (#245)
        const string rawToolCalls =
            """[{"type":"tool_use","id":"toolu_1","name":"file_read","input":{"path":"/workspace/a.cs","max_lines":20}}]""";
        var messages = new[]
        {
            LlmMessage.System("System prompt"),
            LlmMessage.User("Task"),
            new LlmMessage { Role = "assistant", Content = "Let me read.", RawToolCalls = rawToolCalls },
            new LlmMessage { Role = "tool", Content = "file contents", ToolCallId = "toolu_1" },
        };

        // Act
        var (conversation, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // Assert — the assistant turn holds a text block then the tool_use block with its input as is
        Assert.Equal(3, conversation.Count);
        using var assistant = JsonDocument.Parse(JsonSerializer.Serialize(conversation[1]));
        var blocks = assistant.RootElement.GetProperty("content");
        Assert.Equal(2, blocks.GetArrayLength());
        Assert.Equal("text", blocks[0].GetProperty("type").GetString());
        var toolUse = blocks[1];
        Assert.Equal("tool_use", toolUse.GetProperty("type").GetString());
        Assert.Equal("toolu_1", toolUse.GetProperty("id").GetString());
        Assert.Equal("file_read", toolUse.GetProperty("name").GetString());
        Assert.Equal("/workspace/a.cs", toolUse.GetProperty("input").GetProperty("path").GetString());
        Assert.Equal(20, toolUse.GetProperty("input").GetProperty("max_lines").GetInt32());
        Assert.Contains("toolu_1", JsonSerializer.Serialize(conversation[2]));
    }

    [Fact]
    public void ShouldReplayToolUseBlocks_WhenRawToolCallsHoldTheOpenAiShape()
    {
        // Arrange — RawToolCalls as LlmProviderToChatClientAdapter writes them
        var rawToolCalls = JsonSerializer.Serialize(new[]
        {
            new { id = "call_1", function = new { name = "grep", arguments = "{\"pattern\":\"Task\"}" } }
        });
        var messages = new[]
        {
            LlmMessage.User("Task"),
            new LlmMessage { Role = "assistant", Content = "", RawToolCalls = rawToolCalls },
            new LlmMessage { Role = "tool", Content = "3 matches", ToolCallId = "call_1" },
        };

        // Act
        var (conversation, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // Assert — the arguments string becomes the tool_use input object
        using var assistant = JsonDocument.Parse(JsonSerializer.Serialize(conversation[1]));
        var toolUse = Assert.Single(assistant.RootElement.GetProperty("content").EnumerateArray());
        Assert.Equal("tool_use", toolUse.GetProperty("type").GetString());
        Assert.Equal("call_1", toolUse.GetProperty("id").GetString());
        Assert.Equal("grep", toolUse.GetProperty("name").GetString());
        Assert.Equal("Task", toolUse.GetProperty("input").GetProperty("pattern").GetString());
    }

    [Fact]
    public void ShouldReplayTheTranscriptTurn_WhenTheAnthropicBodyCalledATool()
    {
        // Arrange — the act loop's path: an Anthropic body with a tool_use block, through
        // NativeToolCallTranscript, replayed on the second call
        const string body =
            """{"content":[{"type":"text","text":"Reading."},{"type":"tool_use","id":"toolu_9","name":"dir_read","input":{"path":"/workspace"}}],"stop_reason":"tool_use"}""";
        using var bodyDoc = JsonDocument.Parse(body);
        var response = new LlmResponse { Content = "Reading.", Model = "claude-3-opus" };
        var assistantTurn = NativeToolCallTranscript.AssistantTurn(response, bodyDoc.RootElement);
        var messages = new[]
        {
            LlmMessage.User("Task"),
            assistantTurn,
            new LlmMessage { Role = "tool", Content = "a.cs b.cs", ToolCallId = "toolu_9" },
        };

        // Act
        var (conversation, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // Assert
        Assert.Equal(3, conversation.Count);
        var json = JsonSerializer.Serialize(conversation);
        Assert.Contains("\"name\":\"dir_read\"", json);
        Assert.Contains("\"tool_use_id\":\"toolu_9\"", json);
    }
}
