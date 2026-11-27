using System.Text.Json.Nodes;
using Orkeon.Interop.AgentCli.Engine;

namespace Orkeon.Interop.AgentCli.Tests.Engine;

public sealed class ClaudeCodeEngineTests
{
    private static readonly ClaudeCodeEngine Engine = new(new AgentCliOptions());

    [Fact]
    public void ShouldIsolateTheCli_WhenCommandLineBuilt()
    {
        var line = Engine.BuildCommandLine(new AgentCliLaunch("/s/system-prompt.md", "{\"mcpServers\":{}}", "haiku", "/s/work"));

        Assert.Equal("claude", line.FileName);
        Assert.DoesNotContain("--bare", line.Arguments);
        AssertFlag(line, "--tools", "");
        AssertFlag(line, "--allowedTools", "mcp__orkeon__*");
        AssertFlag(line, "--setting-sources", "");
        AssertFlag(line, "--system-prompt-file", "/s/system-prompt.md");
        AssertFlag(line, "--model", "haiku");
        AssertFlag(line, "--input-format", "stream-json");
        Assert.Contains("--strict-mcp-config", line.Arguments);
        Assert.Contains("--no-session-persistence", line.Arguments);
        Assert.Contains("ANTHROPIC_API_KEY", line.RemovedEnvironment);
        Assert.Contains("ANTHROPIC_AUTH_TOKEN", line.RemovedEnvironment);
        Assert.Equal("1", line.Environment["CLAUDE_CODE_DISABLE_CLAUDE_MDS"]);
        Assert.Equal("0", line.Environment["CLAUDE_CODE_MCP_AUTO_BACKGROUND_MS"]);
    }

    [Fact]
    public void ShouldRemoveCloudSwitches_WhenCommandLineBuilt()
    {
        var line = Engine.BuildCommandLine(new AgentCliLaunch("/p", "{}", null, "/w"));

        Assert.Contains("CLAUDE_CODE_USE_BEDROCK", line.RemovedEnvironment);
        Assert.Contains("CLAUDE_CODE_USE_VERTEX", line.RemovedEnvironment);
        Assert.Contains("CLAUDE_CODE_USE_FOUNDRY", line.RemovedEnvironment);
        Assert.Equal(["ANTHROPIC_"], line.RemovedEnvironmentPrefixes);
    }

    [Theory]
    [InlineData(100_000_000, "100000000")]
    [InlineData(5_000, "100000")]
    public void ShouldPushCompactionAway_WhenCommandLineBuilt(long configured, string sent)
    {
        var engine = new ClaudeCodeEngine(new AgentCliOptions { AutocompactTokens = configured });

        var line = engine.BuildCommandLine(new AgentCliLaunch("/p", "{}", null, "/w"));

        AssertFlag(line, "--autocompact", sent);
    }

    [Fact]
    public void ShouldOmitModel_WhenNoneGiven()
    {
        var line = Engine.BuildCommandLine(new AgentCliLaunch("/p", "{}", null, "/w"));

        Assert.DoesNotContain("--model", line.Arguments);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public void ShouldPassEffort_WhenLaunchCarriesOne(string effort)
    {
        var line = Engine.BuildCommandLine(new AgentCliLaunch("/p", "{}", "haiku", "/w", effort));

        AssertFlag(line, "--effort", effort);
    }

    [Fact]
    public void ShouldOmitEffort_WhenNoneGiven()
    {
        var line = Engine.BuildCommandLine(new AgentCliLaunch("/p", "{}", "haiku", "/w"));

        Assert.DoesNotContain("--effort", line.Arguments);
    }

    [Fact]
    public void ShouldWriteStreamJsonInput_WhenUserAndInterruptLines()
    {
        var user = JsonNode.Parse(Engine.UserLine("hello"))!;
        var interrupt = JsonNode.Parse(Engine.InterruptLine("r1"))!;

        Assert.Equal("user", user["type"]!.GetValue<string>());
        Assert.Equal("hello", user["message"]!["content"]!.GetValue<string>());
        Assert.Equal("interrupt", interrupt["request"]!["subtype"]!.GetValue<string>());
        Assert.Equal("r1", interrupt["request_id"]!.GetValue<string>());
    }

    [Fact]
    public void ShouldReadEveryEventKind_WhenAppendixCLines()
    {
        var init = Engine.ReadEvent(StreamLines.Init("ANTHROPIC_API_KEY").ToJsonString())!;
        var block = Engine.ReadEvent(StreamLines.ToolUse("m1", "toolu_1", "read").ToJsonString())!;
        var delta = Engine.ReadEvent(StreamLines.Delta("tool_use").ToJsonString())!;
        var stop = Engine.ReadEvent(StreamLines.Stop().ToJsonString())!;
        var result = Engine.ReadEvent(StreamLines.Result("boom", isError: true, status: 500).ToJsonString())!;
        var limit = Engine.ReadEvent(StreamLines.RateLimit("rejected", 1_900_000_000).ToJsonString())!;
        var compact = Engine.ReadEvent(StreamLines.CompactBoundary().ToJsonString())!;

        Assert.Equal(AgentCliEventKind.Init, init.Kind);
        Assert.Equal("ANTHROPIC_API_KEY", init.ApiKeySource);
        Assert.Equal(AgentCliEventKind.AssistantBlock, block.Kind);
        Assert.Equal("m1", block.MessageId);
        Assert.Single(block.Blocks!);
        Assert.Equal("tool_use", delta.StopReason);
        Assert.Equal(20, delta.Usage!["output_tokens"]!.GetValue<int>());
        Assert.Equal(AgentCliEventKind.MessageStop, stop.Kind);
        Assert.True(result.IsError);
        Assert.Equal(500, result.ApiErrorStatus);
        Assert.Equal(0.0123m, result.NotionalCostUsd);
        Assert.Equal("rejected", limit.RateLimitStatus);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_900_000_000), limit.ResetsAt);
        Assert.Equal(AgentCliEventKind.CompactBoundary, compact.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"user\"}")]
    [InlineData("{\"type\":\"assistant\",\"parent_tool_use_id\":\"toolu_9\",\"message\":{}}")]
    public void ShouldIgnoreLine_WhenNotAnEventOfTheConversation(string line)
    {
        Assert.Null(Engine.ReadEvent(line));
    }

    private static void AssertFlag(AgentCliCommandLine line, string flag, string value)
    {
        var index = line.Arguments.ToList().IndexOf(flag);
        Assert.True(index >= 0, $"{flag} missing");
        Assert.Equal(value, line.Arguments[index + 1]);
    }
}
