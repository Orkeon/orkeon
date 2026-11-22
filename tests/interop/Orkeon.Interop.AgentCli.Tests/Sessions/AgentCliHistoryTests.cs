using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Interop.AgentCli.Sessions;

namespace Orkeon.Interop.AgentCli.Tests.Sessions;

public sealed class AgentCliHistoryTests
{
    [Fact]
    public void ShouldJoinLeadingSystemMessages_WhenSplitting()
    {
        LlmMessage[] messages =
        [
            new() { Role = "system", Content = "A" },
            new() { Role = "System", Content = "B" },
            new() { Role = "user", Content = "Q" },
            new() { Role = "system", Content = "late" },
        ];

        var (prompt, history) = AgentCliHistory.Split(messages, "Base");

        Assert.Equal("Base\n\nA\n\nB", prompt);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void ShouldReadToolCallIds_WhenEitherShape()
    {
        var anthropic = new LlmMessage { Role = "assistant", RawToolCalls = "[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"read\",\"input\":{}}]" };
        var openAi = new LlmMessage { Role = "assistant", RawToolCalls = "[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"read\",\"arguments\":\"{}\"}}]" };

        Assert.Equal("toolu_1", AgentCliHistory.KeyOf(anthropic).ToolIds);
        Assert.Equal("call_1", AgentCliHistory.KeyOf(openAi).ToolIds);
        Assert.Equal("read", AgentCliHistory.ToolCallsOf(openAi)[0].Name);
    }

    [Fact]
    public void ShouldSendRequestAlone_WhenHistoryIsOneUserMessage()
    {
        Assert.Equal("Hi", AgentCliHistory.Seed([new LlmMessage { Role = "user", Content = "Hi" }]));
    }

    [Fact]
    public void ShouldRenderHistoryThenAskToContinue_WhenHistoryEndsOnToolResult()
    {
        LlmMessage[] history =
        [
            new() { Role = "user", Content = "Read a.txt" },
            new() { Role = "assistant", Content = "", RawToolCalls = "[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"read\",\"input\":{\"path\":\"a.txt\"}}]" },
            new() { Role = "tool", ToolCallId = "toolu_1", Content = "hello" },
        ];

        var seed = AgentCliHistory.Seed(history);

        Assert.StartsWith("<conversation_history>", seed, StringComparison.Ordinal);
        Assert.Contains("[assistant called tool read (toolu_1) with {\"path\":\"a.txt\"}]", seed, StringComparison.Ordinal);
        Assert.Contains("[result of tool call toolu_1]\nhello", seed, StringComparison.Ordinal);
        Assert.EndsWith("Continue from where the conversation above stops.", seed, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldTellConversationsApart_WhenFirstMessageDiffers()
    {
        var one = AgentCliHistory.Fingerprint("haiku", "P", [new AgentCliMessageKey("user", "A", "")]);
        var same = AgentCliHistory.Fingerprint("haiku", "P", [new AgentCliMessageKey("user", "A", ""), new AgentCliMessageKey("assistant", "x", "")]);
        var other = AgentCliHistory.Fingerprint("haiku", "P", [new AgentCliMessageKey("user", "B", "")]);

        Assert.Equal(one, same);
        Assert.NotEqual(one, other);
    }
}
