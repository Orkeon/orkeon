using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Unit tests for <see cref="AnthropicLlmProvider.SeparateSystemMessages"/>,
/// covering each message routing branch independently.
/// </summary>
public class AnthropicSeparateSystemMessagesTests
{
    private static LlmConfig DefaultConfig => LlmConfig.Create(ModelClaude3Opus, "test-key");

    // ── System messages ────────────────────────────────────────────────────

    [Fact]
    public void ShouldExtractFirstSystemMessage_AsSystemPrompt()
    {
        var messages = new[]
        {
            LlmMessage.System("You are a helpful assistant."),
            LlmMessage.User("Hello")
        };

        var (conv, system) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        Assert.Equal("You are a helpful assistant.", system);
        Assert.Single(conv);
    }

    [Fact]
    public void ShouldKeepSubsequentSystemMessages_AsUserMessages()
    {
        var messages = new[]
        {
            LlmMessage.System("First system."),
            LlmMessage.System("Trim notice."),
            LlmMessage.User("Hello")
        };

        var (conv, system) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        Assert.Equal("First system.", system);
        Assert.Equal(2, conv.Count);
        var second = (dynamic)conv[0];
        Assert.Equal("user", (string)second.role);
        Assert.Equal("Trim notice.", (string)second.content);
    }

    // ── Standard messages ──────────────────────────────────────────────────

    [Fact]
    public void ShouldPassThrough_StandardUserAndAssistantMessages()
    {
        var messages = new[]
        {
            LlmMessage.User("Hello"),
            LlmMessage.Assistant("Hi there!")
        };

        var (conv, system) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        Assert.Null(system);
        Assert.Equal(2, conv.Count);
    }

    // ── Assistant messages with tool calls ─────────────────────────────────

    [Fact]
    public void ShouldConvertToolCallsToContentBlocks_WhenAssistantHasRawToolCalls()
    {
        var toolCalls = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "toolu_01",
                function = new { name = "search", arguments = "{\"query\":\"cats\"}" }
            }
        });

        var messages = new[]
        {
            new LlmMessage { Role = "assistant", Content = "Let me search.", RawToolCalls = toolCalls }
        };

        var (conv, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        Assert.Single(conv);
        var msg = conv[0];
        var json = JsonSerializer.Serialize(msg);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("assistant", doc.RootElement.GetProperty("role").GetString());

        var blocks = doc.RootElement.GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, blocks.Count);
        Assert.Equal("text", blocks[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", blocks[1].GetProperty("type").GetString());
        Assert.Equal("toolu_01", blocks[1].GetProperty("id").GetString());
        Assert.Equal("search", blocks[1].GetProperty("name").GetString());
    }

    [Fact]
    public void ShouldIncludeTextBlock_WhenAssistantHasContentAndToolCalls()
    {
        var toolCalls = JsonSerializer.Serialize(new[]
        {
            new { id = "t1", function = new { name = "tool", arguments = "{}" } }
        });

        var messages = new[]
        {
            new LlmMessage { Role = "assistant", Content = "Thinking...", RawToolCalls = toolCalls }
        };

        var (conv, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        var json = JsonSerializer.Serialize(conv[0]);
        using var doc = JsonDocument.Parse(json);
        var blocks = doc.RootElement.GetProperty("content").EnumerateArray().ToList();
        Assert.Equal("text", blocks[0].GetProperty("type").GetString());
        Assert.Equal("Thinking...", blocks[0].GetProperty("text").GetString());
    }

    [Fact]
    public void ShouldOmitTextBlock_WhenAssistantContentIsEmpty()
    {
        var toolCalls = JsonSerializer.Serialize(new[]
        {
            new { id = "t1", function = new { name = "tool", arguments = "{}" } }
        });

        var messages = new[]
        {
            new LlmMessage { Role = "assistant", Content = "", RawToolCalls = toolCalls }
        };

        var (conv, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        var json = JsonSerializer.Serialize(conv[0]);
        using var doc = JsonDocument.Parse(json);
        var blocks = doc.RootElement.GetProperty("content").EnumerateArray().ToList();
        Assert.Single(blocks);
        Assert.Equal("tool_use", blocks[0].GetProperty("type").GetString());
    }

    // ── Tool result messages ───────────────────────────────────────────────

    [Fact]
    public void ShouldGroupConsecutiveToolResults_IntoSingleUserMessage()
    {
        var toolCalls = JsonSerializer.Serialize(new[]
        {
            new { id = "tc1", function = new { name = "a", arguments = "{}" } },
            new { id = "tc2", function = new { name = "b", arguments = "{}" } }
        });

        var messages = new[]
        {
            new LlmMessage { Role = "assistant", Content = "", RawToolCalls = toolCalls },
            new LlmMessage { Role = "tool", ToolCallId = "tc1", Content = "result1" },
            new LlmMessage { Role = "tool", ToolCallId = "tc2", Content = "result2" }
        };

        var (conv, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // assistant block + one grouped user message
        Assert.Equal(2, conv.Count);
        var json = JsonSerializer.Serialize(conv[1]);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
        var results = doc.RootElement.GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        Assert.Equal("tool_result", results[0].GetProperty("type").GetString());
        Assert.Equal("tc1", results[0].GetProperty("tool_use_id").GetString());
        Assert.Equal("tool_result", results[1].GetProperty("type").GetString());
        Assert.Equal("tc2", results[1].GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public void ShouldDropOrphanToolResults_WhenAssistantWasTrimmed()
    {
        // No assistant message carrying "orphan_id" — simulates trimmed history.
        var messages = new[]
        {
            new LlmMessage { Role = "tool", ToolCallId = "orphan_id", Content = "stale result" },
            LlmMessage.User("What happened?")
        };

        var (conv, _) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        // orphan dropped; only the user message remains
        Assert.Single(conv);
        var json = JsonSerializer.Serialize(conv[0]);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("What happened?", doc.RootElement.GetProperty("content").GetString());
    }

    // ── System message fallback ────────────────────────────────────────────

    [Fact]
    public void ShouldFallBackToConfig_WhenNoSystemMessageInMessages()
    {
        var config = LlmConfig.Create(ModelClaude3Opus, "key") with
        {
            SystemMessage = "Fallback system."
        };

        var messages = new[] { LlmMessage.User("Hi") };
        var (_, system) = AnthropicLlmProvider.SeparateSystemMessages(messages, config);

        Assert.Equal("Fallback system.", system);
    }

    [Fact]
    public void ShouldFallBackToCustomParameters_WhenNeitherMessageNorConfigSystemMessage()
    {
        var config = LlmConfig.Create(ModelClaude3Opus, "key") with
        {
            CustomParameters = new Dictionary<string, object> { ["system_message"] = "Custom param system." }
        };

        var messages = new[] { LlmMessage.User("Hi") };
        var (_, system) = AnthropicLlmProvider.SeparateSystemMessages(messages, config);

        Assert.Equal("Custom param system.", system);
    }

    // ── Empty message list ─────────────────────────────────────────────────

    [Fact]
    public void ShouldAddEmptyUserMessage_WhenAllMessagesAreSystem()
    {
        var messages = new[] { LlmMessage.System("Only system.") };

        var (conv, system) = AnthropicLlmProvider.SeparateSystemMessages(messages, DefaultConfig);

        Assert.Equal("Only system.", system);
        Assert.Single(conv);
        var json = JsonSerializer.Serialize(conv[0]);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("", doc.RootElement.GetProperty("content").GetString());
    }
}
