using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using AppParsedToolCall = Orkeon.Application.Interfaces.LLM.ParsedToolCall;
using IAppToolCallParser = Orkeon.Application.Interfaces.LLM.IToolCallParser;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SONAR-14 T2: exercises the native structured tool-calling loop end to end with a
/// scripted full provider and a hand-written parser — final answers, tool round-trips,
/// unknown tools, tool faults, the circuit breaker, iteration bounds and the
/// tool_calls / reasoning_content replay rules.
/// </summary>
public class NativeToolCallingAgentLoopTests
{
    private static DomainAgent BuildAgent(int maxIterations = 5, params ITool[] tools)
    {
        var builder = new AgentBuilder()
            .Role("Native Agent")
            .Goal("Exercise the native loop")
            .MaxIterations(maxIterations);

        foreach (var tool in tools)
            builder = builder.WithTool(tool);

        return builder.Build();
    }

    private static DomainTask BuildTask() =>
        DomainTask.Create(
            TaskDescription.From("Native loop task"),
            ExpectedOutput.From("An answer"));

    private static (NativeToolCallingAgentLoop loop, ScriptedFullLlmProvider provider, SpyExecutionLogger logger)
        BuildLoop(DomainAgent agent, IEnumerable<IBaseTool>? registeredTools = null)
    {
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider();
        var strategy = new FakeToolCallingStrategy(new OpenAiShapedToolCallParser());
        var gate = new LlmCallGate(logger, new ScriptedBasicLlmProvider(), rateLimiter: null);
        var loop = new NativeToolCallingAgentLoop(logger, provider, strategy, registeredTools, gate);
        _ = agent;
        return (loop, provider, logger);
    }

    private static ExecutionInvocationContext BuildInvocation(DomainAgent agent, DomainTask task) =>
        new(agent, task, "system prompt", "user prompt", Context: null,
            ToolsUsed: [], Stopwatch: System.Diagnostics.Stopwatch.StartNew());

    [Fact]
    public async System.Threading.Tasks.Task ReturnsText_WhenTheResponseHasNoRawBody()
    {
        var agent = BuildAgent();
        var (loop, provider, _) = BuildLoop(agent);
        provider.EnqueueText("plain final answer");

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        Assert.Equal("plain final answer", result.Output);
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(1, result.IterationsUsed);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReturnsText_WhenTheRawBodyCarriesNoToolCalls()
    {
        var agent = BuildAgent();
        var (loop, provider, _) = BuildLoop(agent);
        provider.Enqueue(new LlmResponse
        {
            Content = "answer without calls",
            RawResponseBody = """{"choices":[{"message":{"content":"answer without calls"}}]}""",
        });

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        Assert.Equal("answer without calls", result.Output);
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecutesAToolRoundTrip_AndRecordsTheUsage()
    {
        var tool = new SpyTool("file_read", result: "file contents");
        var agent = BuildAgent(5, tool);
        var (loop, provider, _) = BuildLoop(agent, registeredTools: [tool]);

        provider.EnqueueOpenAiToolCall("call-1", "file_read", """{"path":"a.txt"}""");
        provider.EnqueueText("done reading");

        var invocation = BuildInvocation(agent, BuildTask());
        var result = await loop.ExecuteAsync(invocation, 5, TestContext.Current.CancellationToken);

        Assert.Equal("done reading", result.Output);
        Assert.Equal(2, result.IterationsUsed);

        var call = Assert.Single(tool.Calls);
        Assert.Equal("a.txt", call.Parameters["path"]);

        var usage = Assert.Single(invocation.ToolsUsed);
        Assert.Equal("file_read", usage.ToolName);
        Assert.True(usage.Success);

        // The second turn must replay the assistant tool_calls and the tool result.
        var secondTurn = provider.ReceivedTurns[1];
        var assistant = secondTurn.Single(m => m.Role == "assistant");
        Assert.Contains("call-1", assistant.RawToolCalls, StringComparison.Ordinal);
        var toolMessage = secondTurn.Single(m => m.Role == "tool");
        Assert.Equal("file contents", toolMessage.Content);
        Assert.Equal("call-1", toolMessage.ToolCallId);
    }

    [Fact]
    public async System.Threading.Tasks.Task FeedsAnErrorBack_ForAnUnknownTool()
    {
        var agent = BuildAgent();
        var (loop, provider, _) = BuildLoop(agent);

        provider.EnqueueOpenAiToolCall("call-x", "ghost_tool", "{}");
        provider.EnqueueText("recovered");

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        Assert.Equal("recovered", result.Output);
        var toolMessage = provider.ReceivedTurns[1].Single(m => m.Role == "tool");
        Assert.StartsWith("Error: Unknown tool: ghost_tool", toolMessage.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task RecordsAFailure_WhenTheToolThrows()
    {
        var tool = new SpyTool("bomb", exceptionToThrow: new InvalidOperationException("boom"));
        var agent = BuildAgent(5, tool);
        var (loop, provider, _) = BuildLoop(agent, registeredTools: [tool]);

        provider.EnqueueOpenAiToolCall("call-b", "bomb", "{}");
        provider.EnqueueText("survived");

        var invocation = BuildInvocation(agent, BuildTask());
        var result = await loop.ExecuteAsync(invocation, 5, TestContext.Current.CancellationToken);

        Assert.Equal("survived", result.Output);
        var usage = Assert.Single(invocation.ToolsUsed);
        Assert.False(usage.Success);
        var toolMessage = provider.ReceivedTurns[1].Single(m => m.Role == "tool");
        Assert.Equal("Error: boom", toolMessage.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task TripsTheCircuitBreaker_OnRepeatedIdenticalToolErrors()
    {
        var tool = new SpyTool("flaky", result: "same failure", succeed: false);
        var agent = BuildAgent(10, tool);
        var (loop, provider, _) = BuildLoop(agent, registeredTools: [tool]);

        for (var i = 0; i < 10; i++)
            provider.EnqueueOpenAiToolCall($"call-{i}", "flaky", "{}");

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 10, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.CircuitBreakerTripped, result.ExitReason);
        Assert.NotNull(result.LastError);
        Assert.Contains("same failure", result.LastError, StringComparison.Ordinal);
        // The breaker trips well before the iteration budget is spent.
        Assert.True(provider.ReceivedTurns.Count < 10);
    }

    [Fact]
    public async System.Threading.Tasks.Task StopsAtMaxIterations_WhenToolCallsNeverEnd()
    {
        var tool = new SpyTool("busy", result: "always more work");
        var agent = BuildAgent(maxIterations: 3, tool);
        var (loop, provider, _) = BuildLoop(agent, registeredTools: [tool]);

        for (var i = 0; i < 10; i++)
            provider.EnqueueOpenAiToolCall($"call-{i}", "busy", $$"""{"step":"{{i}}"}""");

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), defaultMaxIterations: 99, TestContext.Current.CancellationToken);

        // The agent's own MaxIterations (3) wins over the orchestrator default (99).
        Assert.Equal(AgentExitReason.MaxIterationsReached, result.ExitReason);
        Assert.Equal(3, result.IterationsUsed);
        Assert.Equal(3, provider.ReceivedTurns.Count);
        Assert.NotNull(result.LastError);
    }

    [Fact]
    public async System.Threading.Tasks.Task ParsesAnthropicToolUseBlocks_ForTheReplayMessage()
    {
        var tool = new SpyTool("anthropic_tool", result: "ok");
        var agent = BuildAgent(5, tool);

        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider();
        // A parser that reads the Anthropic body shape.
        var strategy = new FakeToolCallingStrategy(new AnthropicShapedParser());
        var gate = new LlmCallGate(logger, new ScriptedBasicLlmProvider(), rateLimiter: null);
        var loop = new NativeToolCallingAgentLoop(logger, provider, strategy, [tool], gate);

        provider.Enqueue(new LlmResponse
        {
            Content = "",
            RawResponseBody =
                """{"content":[{"type":"tool_use","id":"toolu-1","name":"anthropic_tool","input":{}}]}""",
        });
        provider.EnqueueText("anthropic done");

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        Assert.Equal("anthropic done", result.Output);
        var assistant = provider.ReceivedTurns[1].Single(m => m.Role == "assistant");
        Assert.Contains("tool_use", assistant.RawToolCalls, StringComparison.Ordinal);
        Assert.Contains("toolu-1", assistant.RawToolCalls, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReplaysReasoningContent_FromTheResponseMetadata()
    {
        var tool = new SpyTool("think_tool", result: "ok");
        var agent = BuildAgent(5, tool);
        var (loop, provider, _) = BuildLoop(agent, registeredTools: [tool]);

        provider.Enqueue(new LlmResponse
        {
            Content = "",
            RawResponseBody =
                """{"choices":[{"message":{"tool_calls":[{"id":"c1","type":"function","function":{"name":"think_tool","arguments":"{}"}}]}}]}""",
            Metadata = new Dictionary<string, object> { ["reasoning_content"] = "chain of thought" },
        });
        provider.EnqueueText("thought through");

        await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        var assistant = provider.ReceivedTurns[1].Single(m => m.Role == "assistant");
        Assert.Equal("chain of thought", assistant.ReasoningContent);
    }

    [Fact]
    public async System.Threading.Tasks.Task SendsTheToolSchemas_InTheLlmConfig()
    {
        var tool = new SpyTool("schema_tool", result: "ok");
        var agent = BuildAgent(5, tool);
        var (loop, provider, _) = BuildLoop(agent, registeredTools: [tool]);
        provider.EnqueueText("done");

        await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        var config = Assert.Single(provider.ReceivedConfigs);
        Assert.NotNull(config);
        Assert.NotNull(config!.Tools);
        Assert.Equal("schema_tool", Assert.Single(config.Tools!).Name);
        Assert.Equal("system prompt", config.SystemMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task Throws_WhenCancelledBeforeTheFirstCall()
    {
        var agent = BuildAgent();
        var (loop, _, _) = BuildLoop(agent);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, cts.Token));
    }

    /// <summary>Reads the Anthropic content[].tool_use body shape.</summary>
    private sealed class AnthropicShapedParser : IAppToolCallParser
    {
        public IReadOnlyList<AppParsedToolCall> ParseToolCalls(System.Text.Json.JsonElement responseBody)
        {
            var calls = new List<AppParsedToolCall>();
            if (!responseBody.TryGetProperty("content", out var content) ||
                content.ValueKind != System.Text.Json.JsonValueKind.Array)
                return calls;

            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
                {
                    calls.Add(new AppParsedToolCall(
                        block.GetProperty("id").GetString() ?? "",
                        block.GetProperty("name").GetString() ?? "",
                        []));
                }
            }

            return calls;
        }

        public object FormatToolResult(AppParsedToolCall toolCall, string result, bool success) => result;

        public object FormatAssistantToolCallMessage(System.Text.Json.JsonElement responseBody) => responseBody.GetRawText();
    }
}
