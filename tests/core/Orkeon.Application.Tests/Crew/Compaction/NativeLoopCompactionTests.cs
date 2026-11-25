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
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>#432: the native tool-calling loop compacts its conversation like the chat-client loop.</summary>
public sealed class NativeLoopCompactionTests
{
    private const string ToolName = "read_page";

    // 2,000 tokens: 1,000 held for the output, a trigger at 750.
    private static readonly ContextWindow SmallWindow = new(2_000, ContextWindowSources.Table);

    private sealed record NativeRun(ScriptedFullLlmProvider Provider, FakeCompactionObserver Observer);

    /// <summary>25 reads of 100 tokens each, then an answer, on <paramref name="options"/>; no compaction at all when null.</summary>
    private static async System.Threading.Tasks.Task<NativeRun> RunTwentyFiveReadsAsync(
        ContextCompactionOptions? options, ILlmProvider? summaryProvider = null, ContextWindow? window = null, int resultChars = 400)
    {
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider();
        for (var turn = 1; turn <= 25; turn++)
            provider.EnqueueOpenAiToolCall($"call-{turn}", ToolName, "{}");
        provider.EnqueueText("done");
        var observer = new FakeCompactionObserver();
        var compaction = options is null
            ? null
            : new ContextCompactionServices
            {
                Options = options,
                Windows = new FakeContextWindowResolver(window ?? SmallWindow),
                Logger = logger,
                Observer = observer,
                Stages = [],
                SummaryProvider = () => summaryProvider,
            }.For(LlmProfiles.Default, baseConfig: null);
        var tool = new SpyTool(ToolName, result: new string('x', resultChars));
        var loop = new NativeToolCallingAgentLoop(logger, provider,
            new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()), [tool],
            new LlmCallGate(logger, new ScriptedBasicLlmProvider()), ToolInvocationPipeline.Unguarded,
            new NativeToolCallingAgentLoop.NativeToolCallingLoopOptions(Compaction: compaction));
        DomainAgent agent = new AgentBuilder().Role("Reader").Goal("Read the pages").MaxIterations(30).WithTool(tool).Build();
        var task = DomainTask.Create(TaskDescription.From("Read every page"), ExpectedOutput.From("A digest"));
        await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, task, "sys", "user", Context: null, ToolsUsed: [],
                Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            TestContext.Current.CancellationToken);
        return new NativeRun(provider, observer);
    }

    private static List<string> Render(IEnumerable<LlmMessage[]> turns) =>
        [.. turns.Select(t => string.Join("|", t.Select(m => $"{m.Role}:{m.Content}:{m.ToolCallId}")))];

    [Fact]
    public async System.Threading.Tasks.Task ShouldShowTheImageAfterACompaction_AndNeverToTheSummary()
    {
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider
        {
            BaseConfig = LlmConfig.Create("gpt-4o"), Capabilities = LlmProviderCapabilities.Unknown with { Vision = true },
        };
        for (var turn = 1; turn <= 5; turn++)
            provider.EnqueueOpenAiToolCall($"call-{turn}", ToolName, "{}");
        provider.Enqueue(new LlmResponse { Content = "The agent read five pages.", TokensUsed = 50 });
        provider.EnqueueOpenAiToolCall("call-6", ToolName, "{}");
        provider.EnqueueText("done");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var files = new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService().AddMount("/inbox")
            .AddFile("/inbox/photo.png", png);
        var shown = new Orkeon.Infrastructure.LLMs.RunImageLlmProvider(
            provider, files, new Orkeon.Application.Validation.MultiModalOptions(),
            new Orkeon.Infrastructure.LLMs.Capabilities.ModelCapabilityRegistry());
        var compaction = new ContextCompactionServices
        {
            Options = new ContextCompactionOptions { KeepRecentTurns = 2 },
            Windows = new FakeContextWindowResolver(new ContextWindow(10_000, ContextWindowSources.Table)),
            Logger = logger,
            Observer = new FakeCompactionObserver(),
            Stages = [],
        }.For(LlmProfiles.Default, baseConfig: null);
        var tool = new SpyTool(ToolName, result: new string('x', 4_000));
        var loop = new NativeToolCallingAgentLoop(logger, shown,
            new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()), [tool],
            new LlmCallGate(logger, new ScriptedBasicLlmProvider()), ToolInvocationPipeline.Unguarded,
            new NativeToolCallingAgentLoop.NativeToolCallingLoopOptions(Compaction: compaction));
        DomainAgent agent = new AgentBuilder().Role("Reader").Goal("Read the pages").MaxIterations(10).WithTool(tool).Build();
        var task = DomainTask.Create(TaskDescription.From("Read every page"), ExpectedOutput.From("A digest"));

        using var images = RunImages.Begin([new Orkeon.Application.Interfaces.Services.CrewInputImage { Path = "/inbox/photo.png" }]);
        await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, task, "sys", "user", Context: null, ToolsUsed: [],
                Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            TestContext.Current.CancellationToken);

        Assert.Equal(CompactionPrompts.Instructions, provider.ReceivedTurns[5][0].Content);
        for (var i = 0; i < provider.ReceivedTurns.Count; i++)
        {
            var pictured = provider.ReceivedTurns[i].Where(m => m.MultiModalContent is not null).ToList();
            if (i == 5)
                Assert.Empty(pictured);
            else
                Assert.Equal("user", Assert.Single(pictured).Content);
        }

        Assert.Empty(RunImages.Unseen);
    }

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
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldCountTheNewestTurn_WhenTheTrimRewroteTheConversationBesideAnAssumedWindow()
    {
        // Arrange: the provider counts 100 prompt tokens a call; past 40 messages the trim runs after every turn,
        // and the estimate must still hold the turn added since the last call.
        var logger = new SpyExecutionLogger();
        var provider = new ScriptedFullLlmProvider();
        for (var turn = 1; turn <= 25; turn++)
            provider.EnqueueOpenAiToolCall($"call-{turn}", ToolName, "{}", promptTokens: 100);
        provider.EnqueueText("done");
        var stage = new FakeContextPressureStage { FromShare = 2 };
        var compaction = new ContextCompactionServices
        {
            Options = new ContextCompactionOptions { KeepRecentTurns = 2 },
            Windows = new FakeContextWindowResolver(new ContextWindow(SmallWindow.Tokens, ContextWindowSources.Settings)),
            Logger = logger,
            Stages = [stage],
        }.For(LlmProfiles.Default, baseConfig: null);
        var tool = new SpyTool(ToolName, result: new string('x', 20));
        var loop = new NativeToolCallingAgentLoop(logger, provider,
            new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()), [tool],
            new LlmCallGate(logger, new ScriptedBasicLlmProvider()), ToolInvocationPipeline.Unguarded,
            new NativeToolCallingAgentLoop.NativeToolCallingLoopOptions(Compaction: compaction));
        DomainAgent agent = new AgentBuilder().Role("Reader").Goal("Read the pages").MaxIterations(30).WithTool(tool).Build();
        var task = DomainTask.Create(TaskDescription.From("Read every page"), ExpectedOutput.From("A digest"));

        // Act
        await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, task, "sys", "user", Context: null, ToolsUsed: [],
                Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(provider.ReceivedTurns, t => t.Length > 2 && t[2].Content.StartsWith("[Context trimmed", StringComparison.Ordinal));
        Assert.All(stage.Pressures.Skip(1), p => Assert.True(p.EstimatedTokens > 100,
            $"the estimate holds the turn added since the last call, it was {p.EstimatedTokens}"));
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

    [Fact]
    [Trait("BR", "CH-429")]
    public async System.Threading.Tasks.Task ShouldPruneNothing_WhenTheRunsCrewAsksSystemOneNothing()
    {
        // Arrange: the same six reads and a host that prunes, in a run of a decision.mode: none crew.
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
        using var run = SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.None);

        // Act
        await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, task, "sys", "user", Context: null, ToolsUsed: [],
                Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldReproduceTodaysTrimExactly_WhenCompactionIsDisabled()
    {
        // Arrange: the same run without any compaction is today's behaviour.
        var today = await RunTwentyFiveReadsAsync(options: null);

        // Act
        var disabled = await RunTwentyFiveReadsAsync(new ContextCompactionOptions { Enabled = false, KeepRecentTurns = 2 });

        // Assert
        Assert.Equal(Render(today.Provider.ReceivedTurns), Render(disabled.Provider.ReceivedTurns));
        Assert.Empty(disabled.Observer.Notices);
        Assert.StartsWith("[Context trimmed", disabled.Provider.ReceivedTurns[^1][2].Content, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldRevertToTheTrimAndSaySo_WhenThreeSummariesFailInARow()
    {
        // Arrange: the summary profile fails every call it is asked.
        var summariser = new ScriptedFullLlmProvider();
        for (var call = 0; call < 3; call++)
        {
            summariser.Enqueue(new LlmResponse
            {
                Content = string.Empty,
                Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = "summary endpoint down" },
            });
        }

        // Act
        var run = await RunTwentyFiveReadsAsync(
            new ContextCompactionOptions { KeepRecentTurns = 2, Profile = "summariser" }, summariser);

        // Assert
        Assert.Equal(
            [AgentCompactionOutcomes.Failed, AgentCompactionOutcomes.Failed, AgentCompactionOutcomes.Reverted],
            run.Observer.Notices.Select(n => n.Outcome));
        Assert.Equal(3, summariser.ReceivedTurns.Count);
        Assert.StartsWith("[Context trimmed", run.Provider.ReceivedTurns[^1][2].Content, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldTrimAsWell_WhenTheWindowIsTheSettingsFallback()
    {
        // Arrange: a window no profile sets and no table knows, far above what 25 reads of 5 tokens fill.
        var assumed = new ContextWindow(1_000_000, ContextWindowSources.Settings);

        // Act
        var run = await RunTwentyFiveReadsAsync(new ContextCompactionOptions { KeepRecentTurns = 2 }, window: assumed, resultChars: 20);

        // Assert
        var last = run.Provider.ReceivedTurns[^1];
        Assert.True(last.Length <= 40, $"the trim keeps 40 messages at most, the last request held {last.Length}");
        Assert.StartsWith("[Context trimmed", last[2].Content, StringComparison.Ordinal);
        Assert.Empty(run.Observer.Notices);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldNotTrim_WhenTheWindowIsKnownForTheModel()
    {
        // Arrange: the same run against a documented window.
        var documented = new ContextWindow(1_000_000, ContextWindowSources.Table);

        // Act
        var run = await RunTwentyFiveReadsAsync(new ContextCompactionOptions { KeepRecentTurns = 2 }, window: documented, resultChars: 20);

        // Assert
        Assert.DoesNotContain(run.Provider.ReceivedTurns[^1], m => m.Content.StartsWith("[Context trimmed", StringComparison.Ordinal));
    }
}
