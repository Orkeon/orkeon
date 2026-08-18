using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SONAR-14 T2: drives the IChatClient loop directly — token and cache accounting, the
/// [TOOL_CALL] text fallback, the behavioural circuit breaker, the empty-final-message
/// retry (tool-free), the max-iteration synthesis retry and the mimicry escalation.
/// </summary>
public class ChatClientAgentLoopTests
{
    /// <summary>Scripted chat client recording every request with its options.</summary>
    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new();

        public List<(IList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

        public void EnqueueText(string text, UsageDetails? usage = null)
        {
            var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            if (usage is not null) response.Usage = usage;
            _responses.Enqueue(response);
        }

        public void EnqueueFunctionCall(string callId, string name)
        {
            var message = new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(callId, name, arguments: null)]);
            _responses.Enqueue(new ChatResponse([message]));
        }

        public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add((messages.ToList(), options));
            return System.Threading.Tasks.Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new ChatResponse([new ChatMessage(ChatRole.Assistant, "scripted default")]));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static DomainAgent BuildAgent(int maxIterations, params ITool[] tools)
    {
        var builder = new AgentBuilder()
            .Role("Chat Agent")
            .Goal("Exercise the chat loop")
            .MaxIterations(maxIterations);

        foreach (var tool in tools)
            builder = builder.WithTool(tool);

        return builder.Build();
    }

    private static DomainTask BuildTask() =>
        DomainTask.Create(TaskDescription.From("Chat loop task"), ExpectedOutput.From("An answer"));

    private static (ChatClientAgentLoop loop, ScriptedChatClient client) BuildLoop(params IBaseTool[] registeredTools)
    {
        var logger = new SpyExecutionLogger();
        var client = new ScriptedChatClient();
        var gate = new LlmCallGate(logger, new ScriptedBasicLlmProvider(), rateLimiter: null);
        var composer = new ChatOptionsComposer(logger, registeredTools, new FakeFileSystemService());
        var loop = new ChatClientAgentLoop(logger, client, gate, composer, new ChatToolDispatcher(logger));
        return (loop, client);
    }

    [Fact]
    public async System.Threading.Tasks.Task AccumulatesTokenAndCacheUsage_AcrossTheLoop()
    {
        var (loop, client) = BuildLoop();
        client.EnqueueText("final", new UsageDetails
        {
            TotalTokenCount = 100,
            InputTokenCount = 60,
            OutputTokenCount = 40,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                [LlmUsageMetadataKeys.CacheHitTokens] = 50,
                [LlmUsageMetadataKeys.CacheMissTokens] = 10,
            },
        });

        var result = await loop.ExecuteAsync(
            BuildAgent(5), BuildTask(), "sys", "user", [], 5, TestContext.Current.CancellationToken);

        Assert.Equal("final", result.Output);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal(60, result.PromptTokens);
        Assert.Equal(40, result.CompletionTokens);
        Assert.Equal(50, result.CacheHitTokens);
        Assert.Equal(10, result.CacheMissTokens);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunsTheTextFallbackToolRound_WhenTheLlmEmitsToolCallBlocks()
    {
        var tool = new SpyTool("text_tool", result: "fallback data");
        var agent = BuildAgent(5, tool);
        var (loop, client) = BuildLoop(tool);

        client.EnqueueText("""[TOOL_CALL]{tool => "text_tool", args => {--path "x"}}[/TOOL_CALL]""");
        client.EnqueueText("final after fallback");

        var toolsUsed = new List<ToolUsage>();
        var result = await loop.ExecuteAsync(
            agent, BuildTask(), "sys", "user", toolsUsed, 5, TestContext.Current.CancellationToken);

        Assert.Equal("final after fallback", result.Output);
        Assert.Equal(2, result.IterationsUsed);
        Assert.Single(tool.Calls);
        Assert.Single(toolsUsed);

        // The second request carries the fed-back tool results.
        var secondRequest = client.Requests[1].Messages;
        Assert.Contains(secondRequest, m => m.Text.Contains("[Tool text_tool result]: fallback data", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task TripsTheCircuitBreaker_AndCarriesThePartialWork()
    {
        var tool = new SpyTool("flaky", result: "identical error", succeed: false);
        var agent = BuildAgent(10, tool);
        var (loop, client) = BuildLoop(tool);

        // An assistant text turn first, so the breaker result has partial work to salvage.
        client.EnqueueText("""Progress so far. [TOOL_CALL]{tool => "flaky", args => {--path "x"}}[/TOOL_CALL]""");
        for (var i = 0; i < 9; i++)
            client.EnqueueFunctionCall($"call-{i}", "flaky");

        var result = await loop.ExecuteAsync(
            agent, BuildTask(), "sys", "user", [], 10, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.CircuitBreakerTripped, result.ExitReason);
        Assert.Contains("identical tool call failures", result.Output, StringComparison.Ordinal);
        Assert.Contains("--- Partial work before failure ---", result.Output, StringComparison.Ordinal);
        Assert.Contains("Progress so far.", result.Output, StringComparison.Ordinal);
        Assert.True(client.Requests.Count < 10);
    }

    [Fact]
    public async System.Threading.Tasks.Task RetriesToolFree_WhenAFinalTurnComesBackEmpty()
    {
        var tool = new SpyTool("worker", result: "did work");
        var agent = BuildAgent(5, tool);
        var (loop, client) = BuildLoop(tool);

        client.EnqueueFunctionCall("call-1", "worker");
        client.EnqueueText("");                    // empty final turn (MiniMax shape)
        client.EnqueueText("synthesized answer");  // the tool-free retry

        var result = await loop.ExecuteAsync(
            agent, BuildTask(), "sys", "user", [], 5, TestContext.Current.CancellationToken);

        Assert.Equal("synthesized answer", result.Output);
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(3, client.Requests.Count);

        // The retry request carries the final-answer nudge and runs without tools.
        var (retryMessages, retryOptions) = client.Requests[2];
        Assert.Contains(retryMessages, m => m.Role == ChatRole.User
            && m.Text.Contains("final deliverable", StringComparison.OrdinalIgnoreCase));
        Assert.NotSame(client.Requests[0].Options, retryOptions);
    }

    [Fact]
    public async System.Threading.Tasks.Task SynthesizesAFinalAnswer_WhenEveryIterationWasAToolCall()
    {
        var tool = new SpyTool("busy", result: "more work");
        var agent = BuildAgent(3, tool);
        var (loop, client) = BuildLoop(tool);

        for (var i = 0; i < 3; i++)
            client.EnqueueFunctionCall($"call-{i}", "busy");
        client.EnqueueText("synthesis from gathered context");

        var result = await loop.ExecuteAsync(
            agent, BuildTask(), "sys", "user", [], 99, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal("synthesis from gathered context", result.Output);
        Assert.Equal(4, client.Requests.Count); // 3 iterations + 1 tool-free synthesis
    }

    [Fact]
    public async System.Threading.Tasks.Task ReportsMaxIterations_WhenEvenTheSynthesisRetryStaysEmpty()
    {
        var tool = new SpyTool("busy", result: "more work");
        var agent = BuildAgent(2, tool);
        var (loop, client) = BuildLoop(tool);

        client.EnqueueFunctionCall("c1", "busy");
        client.EnqueueFunctionCall("c2", "busy");
        client.EnqueueText(""); // the synthesis retry also fails

        var result = await loop.ExecuteAsync(
            agent, BuildTask(), "sys", "user", [], 99, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.MaxIterationsReached, result.ExitReason);
        Assert.Equal("empty_final_message_after_retry", result.LastError);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async System.Threading.Tasks.Task EscalatesOnce_WhenTheRetryMimicsToolCalls()
    {
        var tool = new SpyTool("worker", result: "did work");
        var agent = BuildAgent(5, tool);
        var (loop, client) = BuildLoop(tool);

        client.EnqueueFunctionCall("call-1", "worker");
        client.EnqueueText("");                                     // empty final turn
        client.EnqueueText("<tool_calls>mimicked markup</tool_calls>"); // first retry: mimicry
        client.EnqueueText("clean final text");                     // escalated retry

        var result = await loop.ExecuteAsync(
            agent, BuildTask(), "sys", "user", [], 5, TestContext.Current.CancellationToken);

        Assert.Equal("clean final text", result.Output);
        Assert.Equal(4, client.Requests.Count);
    }
}
