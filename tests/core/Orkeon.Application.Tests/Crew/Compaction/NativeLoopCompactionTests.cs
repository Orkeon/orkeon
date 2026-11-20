using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>#432: the native tool-calling loop compacts its conversation like the chat-client loop.</summary>
public sealed class NativeLoopCompactionTests
{
    private const string ToolName = "read_page";

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldReplaceTheOldTurnsWithTheSummaryAndChargeItsTokens_WhenTheEstimatePassesTheTrigger()
    {
        // Arrange: 1,000 tokens a tool result against a 10,000-token window; the summary answers in 50 tokens.
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider();
        for (var turn = 1; turn <= 5; turn++)
            provider.EnqueueOpenAiToolCall($"call-{turn}", ToolName, "{}");
        provider.Enqueue(new LlmResponse { Content = "The agent read five pages.", TokensUsed = 50 });
        provider.EnqueueOpenAiToolCall("call-6", ToolName, "{}");
        provider.EnqueueText("done");
        var observer = new FakeCompactionObserver();
        var compaction = new ContextCompactionServices
        {
            Options = new ContextCompactionOptions { KeepRecentTurns = 2 },
            Windows = new FakeContextWindowResolver(new ContextWindow(10_000, ContextWindowSources.Table)),
            Logger = logger,
            Observer = observer,
            Stages = [],
        }.For(LlmProfiles.Default, baseConfig: null);
        var tool = new SpyTool(ToolName, result: new string('x', 4_000));
        var loop = new NativeToolCallingAgentLoop(logger, provider,
            new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()), [tool],
            new LlmCallGate(logger, new ScriptedBasicLlmProvider()), ToolInvocationPipeline.Unguarded,
            new NativeToolCallingAgentLoop.NativeToolCallingLoopOptions(Compaction: compaction));
        DomainAgent agent = new AgentBuilder().Role("Reader").Goal("Read the pages").MaxIterations(10).WithTool(tool).Build();
        var task = DomainTask.Create(TaskDescription.From("Read every page"), ExpectedOutput.From("A digest"));

        // Act
        var result = await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, task, "sys", "user", Context: null, ToolsUsed: [],
                Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("done", result.Output);
        Assert.Equal(50, result.TokensUsed);
        Assert.Equal(AgentCompactionOutcomes.Compacted, Assert.Single(observer.Notices).Outcome);
        Assert.Equal(CompactionPrompts.Instructions, provider.ReceivedTurns[5][0].Content);
        var afterCompaction = provider.ReceivedTurns[6];
        Assert.Equal(["sys", "user"], afterCompaction.Take(2).Select(m => m.Content));
        Assert.StartsWith("[Context compacted: 6 earlier messages", afterCompaction[2].Content, StringComparison.Ordinal);
        Assert.Equal(7, afterCompaction.Length);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldFindTheCallOfAnOldReadResultAndDropIt_WhenThePruningStageRunsFirst()
    {
        // Arrange: six reads of 1,000 tokens each against a 10,000-token window; every result offered is dropped.
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider();
        for (var turn = 1; turn <= 6; turn++)
            provider.EnqueueOpenAiToolCall($"call-{turn}", ToolName, $"{{\"page\":{turn}}}");
        provider.EnqueueText("done");
        var model = new FakeDecisionModel
        {
            Respond = request => new DecisionResponse
            {
                Model = "fake",
                Answers = request.Questions.Keys.ToImmutableDictionary(k => k, _ => (DecisionAnswer)new NoulAnswer { Noul = 0.1 }),
            },
        };
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(model).BuildServiceProvider();
        var compaction = new ContextCompactionServices
        {
            Options = new ContextCompactionOptions { KeepRecentTurns = 2 },
            Windows = new FakeContextWindowResolver(new ContextWindow(10_000, ContextWindowSources.Table)),
            Logger = logger,
            Observer = new FakeCompactionObserver(),
            Stages = [new ToolResultPruningStage(services, new ToolResultPruningOptions { Enabled = true }, null, null, new RecordingLogger())],
        }.For(LlmProfiles.Default, baseConfig: null);
        var tool = new SpyTool(ToolName, result: new string('x', 4_000)) { Access = ToolAccess.Read };
        var loop = new NativeToolCallingAgentLoop(logger, provider,
            new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()), [tool],
            new LlmCallGate(logger, new ScriptedBasicLlmProvider()), ToolInvocationPipeline.Unguarded,
            new NativeToolCallingAgentLoop.NativeToolCallingLoopOptions(Compaction: compaction));
        DomainAgent agent = new AgentBuilder().Role("Reader").Goal("Read the pages").MaxIterations(10).WithTool(tool).Build();
        var task = DomainTask.Create(TaskDescription.From("Read every page"), ExpectedOutput.From("A digest"));

        // Act
        await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, task, "sys", "user", Context: null, ToolsUsed: [],
                Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(provider.ReceivedTurns[^1], m => m.ToolCallId == "call-1"
            && m.Content.StartsWith("[pruned to save context: the result of read_page {\"page\":", StringComparison.Ordinal));
    }
}
