using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SONAR-14 T2: exercises the legacy text loop — multi-turn [TOOL_CALL] rounds, the
/// tool-not-found and tool-fault paths, the circuit breaker, the iteration bound and the
/// conversation trim — with a scripted <c>IBasicLlmProvider</c>.
/// </summary>
public class LegacyTextAgentLoopTests
{
    private static DomainAgent BuildAgent(int maxIterations = 5, params ITool[] tools)
    {
        var builder = new AgentBuilder()
            .Role("Legacy Agent")
            .Goal("Exercise the legacy loop")
            .MaxIterations(maxIterations);

        foreach (var tool in tools)
            builder = builder.WithTool(tool);

        return builder.Build();
    }

    private static DomainTask BuildTask() =>
        DomainTask.Create(
            TaskDescription.From("Legacy loop task"),
            ExpectedOutput.From("An answer"));

    private static (LegacyTextAgentLoop loop, ScriptedBasicLlmProvider provider) BuildLoop()
    {
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedBasicLlmProvider();
        var loop = new LegacyTextAgentLoop(logger, provider, new LlmCallGate(logger, provider, rateLimiter: null));
        return (loop, provider);
    }

    private static ExecutionInvocationContext BuildInvocation(DomainAgent agent, DomainTask task) =>
        new(agent, task, "system prompt", "user prompt", Context: null,
            ToolsUsed: [], Stopwatch: System.Diagnostics.Stopwatch.StartNew());

    private const string ToolCallResponse =
        """[TOOL_CALL]{tool => "echo_tool", args => {--path "a.txt"}}[/TOOL_CALL]""";

    [Fact]
    public async System.Threading.Tasks.Task RunsAMultiTurnToolRound_ThenReturnsTheFinalAnswer()
    {
        var tool = new SpyTool("echo_tool", result: "tool says hello");
        var agent = BuildAgent(5, tool);
        var (loop, provider) = BuildLoop();

        provider.Enqueue("Working on it. " + ToolCallResponse);
        provider.Enqueue("The final answer, informed by the tool.");

        var invocation = BuildInvocation(agent, BuildTask());
        var result = await loop.ExecuteAsync(invocation, 5, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(2, result.IterationsUsed);
        Assert.Contains("final answer", result.Output, StringComparison.Ordinal);

        var call = Assert.Single(tool.Calls);
        Assert.Equal("a.txt", call.Parameters["path"]);
        var usage = Assert.Single(invocation.ToolsUsed);
        Assert.True(usage.Success);

        // The second prompt replays the assistant turn and the tool result.
        var secondPrompt = provider.ReceivedPrompts[1];
        Assert.Contains("[Tool echo_tool result]: tool says hello", secondPrompt, StringComparison.Ordinal);
        Assert.Contains("Continue working on the task", secondPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task ReportsAMissingTool_InTheToolResults()
    {
        var agent = BuildAgent(5, new SpyTool("other_tool"));
        var (loop, provider) = BuildLoop();

        provider.Enqueue(ToolCallResponse); // echo_tool is not on the agent
        provider.Enqueue("recovered without the tool");

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Contains("[Tool 'echo_tool' not found]", provider.ReceivedPrompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task RecordsAFailure_WhenTheToolThrows()
    {
        var tool = new SpyTool("echo_tool", exceptionToThrow: new InvalidOperationException("kaboom"));
        var agent = BuildAgent(5, tool);
        var (loop, provider) = BuildLoop();

        provider.Enqueue(ToolCallResponse);
        provider.Enqueue("moving on");

        var invocation = BuildInvocation(agent, BuildTask());
        await loop.ExecuteAsync(invocation, 5, TestContext.Current.CancellationToken);

        var usage = Assert.Single(invocation.ToolsUsed);
        Assert.False(usage.Success);
        Assert.Contains("[Tool echo_tool error]: kaboom", provider.ReceivedPrompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task TripsTheCircuitBreaker_OnRepeatedIdenticalToolErrors()
    {
        var tool = new SpyTool("echo_tool", result: "identical failure", succeed: false);
        var agent = BuildAgent(10, tool);
        var (loop, provider) = BuildLoop();

        for (var i = 0; i < 10; i++)
            provider.Enqueue(ToolCallResponse);

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 10, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.CircuitBreakerTripped, result.ExitReason);
        Assert.Contains("identical tool call failures", result.Output, StringComparison.Ordinal);
        Assert.NotNull(result.LastError);
        Assert.True(provider.ReceivedPrompts.Count < 10);
    }

    [Fact]
    public async System.Threading.Tasks.Task StopsAtTheAgentIterationBound()
    {
        // A succeeding tool whose result text varies per call keeps the breaker quiet,
        // so the loop must stop on the agent's own MaxIterations.
        var tool = new SpyTool("echo_tool", result: "fresh work every time");
        var agent = BuildAgent(maxIterations: 3, tool);
        var (loop, provider) = BuildLoop();

        for (var i = 0; i < 10; i++)
            provider.Enqueue(ToolCallResponse);

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), defaultMaxIterations: 99, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.MaxIterationsReached, result.ExitReason);
        Assert.Equal(3, result.IterationsUsed);
        Assert.Equal(3, provider.ReceivedPrompts.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task TrimsTheConversation_WhenItOutgrowsTheContextBudget()
    {
        // A tool result large enough to blow past MaxContextMessages * 200 characters in
        // one round forces the trim before the second LLM call.
        var hugeResult = new string('x', 60_000);
        var tool = new SpyTool("echo_tool", result: hugeResult);
        var agent = BuildAgent(5, tool);
        var (loop, provider) = BuildLoop();

        provider.Enqueue(ToolCallResponse);
        provider.Enqueue("done after trim");

        var result = await loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Contains("[Context trimmed", provider.ReceivedPrompts[1], StringComparison.Ordinal);
        // The trimmed prompt still opens with the system prompt.
        Assert.StartsWith("system prompt", provider.ReceivedPrompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task FallsBackToNameMatching_WhenNoStructuredBlockIsEmitted()
    {
        var tool = new SpyTool("mentioned_tool", result: "fallback output");
        var agent = BuildAgent(5, tool);
        var (loop, provider) = BuildLoop();

        provider.Enqueue("I would use mentioned_tool for this.");

        var invocation = BuildInvocation(agent, BuildTask());
        var result = await loop.ExecuteAsync(invocation, 5, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(1, result.IterationsUsed);
        Assert.Contains("Tool mentioned_tool result: fallback output", result.Output, StringComparison.Ordinal);
        Assert.Single(invocation.ToolsUsed);
    }

    [Fact]
    public async System.Threading.Tasks.Task Throws_WhenCancelledBeforeTheFirstCall()
    {
        var agent = BuildAgent();
        var (loop, _) = BuildLoop();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            loop.ExecuteAsync(BuildInvocation(agent, BuildTask()), 5, cts.Token));
    }
}
