using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SONAR-14 T2: pins the shared conversation policy — circuit-breaker evaluation and
/// result building, last-assistant-text extraction, history trimming (both message
/// shapes, including the orphan-tool-result guard) and tool-result truncation.
/// </summary>
public class ConversationPolicyTests
{
    // ── EvaluateCircuitBreaker ────────────────────────────────────────────

    [Fact]
    public void Trips_AfterTheConfiguredNumberOfIdenticalErrors()
    {
        string? lastError = null;
        var count = 0;

        for (var i = 0; i < AgentDefaults.MaxConsecutiveIdenticalErrors - 1; i++)
            Assert.False(ConversationPolicy.EvaluateCircuitBreaker("Error: same", ref lastError, ref count));

        Assert.True(ConversationPolicy.EvaluateCircuitBreaker("Error: same", ref lastError, ref count));
        Assert.Equal(AgentDefaults.MaxConsecutiveIdenticalErrors, count);
    }

    [Fact]
    public void ADifferentError_RestartsTheCount()
    {
        string? lastError = null;
        var count = 0;

        ConversationPolicy.EvaluateCircuitBreaker("Error: one", ref lastError, ref count);
        ConversationPolicy.EvaluateCircuitBreaker("Error: one", ref lastError, ref count);
        Assert.False(ConversationPolicy.EvaluateCircuitBreaker("Error: two", ref lastError, ref count));

        Assert.Equal(1, count);
        Assert.Equal("Error: two", lastError);
    }

    [Fact]
    public void ASuccess_ResetsTheBreaker()
    {
        string? lastError = "Error: pending";
        var count = 2;

        Assert.False(ConversationPolicy.EvaluateCircuitBreaker(null, ref lastError, ref count));

        Assert.Null(lastError);
        Assert.Equal(0, count);
    }

    // ── BuildCircuitBreakerResult ─────────────────────────────────────────

    [Fact]
    public void BuildsTheResult_WithoutPartialOutput()
    {
        var result = ConversationPolicy.BuildCircuitBreakerResult(3, "Error: stuck", totalTokensUsed: 42, iteration: 4);

        Assert.Equal(AgentExitReason.CircuitBreakerTripped, result.ExitReason);
        Assert.Equal(5, result.IterationsUsed);
        Assert.Equal(42, result.TokensUsed);
        Assert.Equal("Error: stuck", result.LastError);
        Assert.DoesNotContain("Partial work", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendsThePartialWork_WhenTheAgentProducedSome()
    {
        var result = ConversationPolicy.BuildCircuitBreakerResult(
            3, "Error: stuck", 0, 1, partialOutput: "half a report");

        Assert.Contains("--- Partial work before failure ---", result.Output, StringComparison.Ordinal);
        Assert.Contains("half a report", result.Output, StringComparison.Ordinal);
    }

    // ── ExtractLastAssistantText ──────────────────────────────────────────

    [Fact]
    public void FindsTheMostRecentNonEmptyAssistantText_InChatMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "older text"),
            new(ChatRole.Assistant, ""),
            new(ChatRole.Tool, "Error: x"),
        };

        Assert.Equal("older text", ConversationPolicy.ExtractLastAssistantText(messages));
    }

    [Fact]
    public void ReturnsNull_WhenNoAssistantSpoke_InChatMessages()
    {
        Assert.Null(ConversationPolicy.ExtractLastAssistantText(
            (IReadOnlyList<ChatMessage>)[new ChatMessage(ChatRole.User, "hello")]));
    }

    [Fact]
    public void FindsTheMostRecentNonEmptyAssistantText_InLlmMessages()
    {
        var messages = new List<LlmMessage>
        {
            new() { Role = "assistant", Content = "first draft" },
            new() { Role = "assistant", Content = " " },
            new() { Role = "tool", Content = "Error: x" },
        };

        Assert.Equal("first draft", ConversationPolicy.ExtractLastAssistantText(messages));
    }

    // ── TrimConversationHistory (ChatMessage) ─────────────────────────────

    [Fact]
    public void KeepsShortHistories_Untouched()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "sys"),
            new(ChatRole.User, "task"),
            new(ChatRole.Assistant, "a1"),
        };

        ConversationPolicy.TrimConversationHistory(messages, maxMessages: 10);

        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public void PreservesSystemAndTask_AndInjectsTheTrimNotice()
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, "sys"), new(ChatRole.User, "task") };
        for (var i = 0; i < 20; i++)
            messages.Add(new ChatMessage(ChatRole.Assistant, $"turn {i}"));

        ConversationPolicy.TrimConversationHistory(messages, maxMessages: 10);

        Assert.Equal("sys", messages[0].Text);
        Assert.Equal("task", messages[1].Text);
        Assert.Contains("[Context trimmed:", messages[2].Text, StringComparison.Ordinal);
        Assert.True(messages.Count <= 10);
        Assert.Equal("turn 19", messages[^1].Text);
    }

    [Fact]
    public void DropsLeadingOrphanToolResults_AfterTheCut()
    {
        // Arrange so the first kept message would be a tool result whose assistant
        // (tool_use) message fell on the wrong side of the cut.
        var messages = new List<ChatMessage> { new(ChatRole.System, "sys"), new(ChatRole.User, "task") };
        for (var i = 0; i < 10; i++)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant, $"call {i}"));
            messages.Add(new ChatMessage(ChatRole.Tool, $"result {i}"));
        }

        ConversationPolicy.TrimConversationHistory(messages, maxMessages: 10);

        // The message right after the notice must never be an orphan tool result.
        Assert.NotEqual(ChatRole.Tool, messages[3].Role);
    }

    // ── TrimConversationHistory (LlmMessage) ──────────────────────────────

    [Fact]
    public void TrimsTheLlmMessageShape_TheSameWay()
    {
        var messages = new List<LlmMessage>
        {
            LlmMessage.System("sys"),
            LlmMessage.User("task"),
        };
        for (var i = 0; i < 10; i++)
        {
            messages.Add(new LlmMessage { Role = "assistant", Content = $"call {i}" });
            messages.Add(new LlmMessage { Role = "tool", Content = $"result {i}" });
        }

        ConversationPolicy.TrimConversationHistory(messages, maxMessages: 10);

        Assert.Equal("sys", messages[0].Content);
        Assert.Equal("task", messages[1].Content);
        Assert.Contains("[Context trimmed:", messages[2].Content, StringComparison.Ordinal);
        Assert.NotEqual("tool", messages[3].Role);
        Assert.True(messages.Count <= 10);
    }

    // ── TruncateToolResult ────────────────────────────────────────────────

    [Fact]
    public void LeavesShortResults_Untouched()
    {
        Assert.Equal("short", ConversationPolicy.TruncateToolResult("short", 100));
    }

    [Fact]
    public void TruncatesLongResults_AndSaysHowMuchWasOmitted()
    {
        var result = ConversationPolicy.TruncateToolResult(new string('a', 150), 100);

        Assert.StartsWith(new string('a', 100), result, StringComparison.Ordinal);
        Assert.Contains("[... truncated, 50 chars omitted", result, StringComparison.Ordinal);
    }
}
