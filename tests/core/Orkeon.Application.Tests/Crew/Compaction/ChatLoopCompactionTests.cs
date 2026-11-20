using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>
/// #432: the chat-client loop compacts its conversation against the model's window - once past the
/// trigger, never below it, the trim as the breaker's fallback, and today's trim exactly when off.
/// </summary>
public sealed class ChatLoopCompactionTests
{
    private const string ToolName = "read_page";

    // 2,000 tokens: 1,000 held for the output, a trigger at 750. A large window never triggers.
    private static readonly ContextWindow SmallWindow = new(2_000, ContextWindowSources.Table);
    private static readonly ContextWindow MidWindow = new(10_000, ContextWindowSources.Table);
    private static readonly ContextWindow LargeWindow = new(1_000_000, ContextWindowSources.Profile);

    private sealed record Run(AgentLoopResult Result, FakeCompactionChatClient Client, FakeCompactionObserver Observer);

    private static DomainAgent Agent(SpyTool tool, int maxIterations, AgentCompaction? compaction = null) =>
        new AgentBuilder().Role("Reader").Goal("Read the pages").MaxIterations(maxIterations)
            .WithTool(tool).Compaction(compaction).Build();

    private static async System.Threading.Tasks.Task<Run> RunAsync(
        int toolTurns, int resultChars, ContextWindow? window, ContextCompactionOptions? options = null,
        AgentCompaction? agentCompaction = null, bool summaryFails = false, IReadOnlyList<IContextPressureStage>? stages = null)
    {
        var tool = new SpyTool(ToolName, result: new string('x', resultChars));
        // The double holds nothing to release, and the test reads its records after the run.
#pragma warning disable CA2000
        var client = new FakeCompactionChatClient { ToolName = ToolName, ToolTurns = toolTurns, SummaryFails = summaryFails };
#pragma warning restore CA2000
        var observer = new FakeCompactionObserver();
        var logger = new SpyExecutionLogger();
        var compaction = window is null
            ? null
            : new ContextCompactionServices
            {
                Options = options ?? new ContextCompactionOptions { KeepRecentTurns = 2 },
                Windows = new FakeContextWindowResolver(window),
                Logger = logger,
                Observer = observer,
                Stages = stages ?? [],
            }.For(LlmProfiles.Default, baseConfig: null);
        var loop = new ChatClientAgentLoop(
            logger, client, new LlmCallGate(logger, new ScriptedBasicLlmProvider()),
            new ChatOptionsComposer(logger, [tool], new FakeFileSystemService(), ToolInvocationPipeline.Unguarded),
            new ChatToolDispatcher(logger, ToolInvocationPipeline.Unguarded),
            new ChatClientAgentLoop.ChatClientAgentLoopOptions(Compaction: compaction));
        var task = DomainTask.Create(TaskDescription.From("Read every page"), ExpectedOutput.From("A digest"));

        var result = await loop.ExecuteAsync(Agent(tool, toolTurns + 4, agentCompaction), task, "sys", "user", [],
            TestContext.Current.CancellationToken);
        return new Run(result, client, observer);
    }

    private static string? CallIdOf(ChatMessage message) =>
        message.Contents.OfType<FunctionCallContent>().FirstOrDefault()?.CallId
        ?? message.Contents.OfType<FunctionResultContent>().FirstOrDefault()?.CallId;

    private static List<string> Render(IEnumerable<List<ChatMessage>> requests) =>
        [.. requests.Select(r => string.Join("|", r.Select(m => $"{m.Role}:{m.Text}:{CallIdOf(m)}")))];

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldCompactOnceKeepingTheTaskAndTheRecentTurns_WhenTheEstimatePassesTheTrigger()
    {
        // Arrange: 1,000 tokens a tool result against a trigger at 4,428 - the fifth turn crosses it.
        var toolTurns = 6;

        // Act
        var run = await RunAsync(toolTurns, resultChars: 4_000, MidWindow);

        // Assert
        Assert.Single(run.Client.SummaryRequests);
        var notice = Assert.Single(run.Observer.Notices);
        Assert.Equal(AgentCompactionOutcomes.Compacted, notice.Outcome);
        var afterCompaction = run.Client.AgentRequests[5];
        Assert.Equal(7, afterCompaction.Count);
        Assert.Equal("sys", afterCompaction[0].Text);
        Assert.Equal("user", afterCompaction[1].Text);
        Assert.StartsWith("[Context compacted: 6 earlier messages", afterCompaction[2].Text, StringComparison.Ordinal);
        Assert.Equal(
            ["call-4", "call-4", "call-5", "call-5"],
            afterCompaction.Skip(3).Select(CallIdOf));
        Assert.Equal([ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.Tool], afterCompaction.Skip(3).Select(m => m.Role));
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldChargeTheSummaryTokensToTheTask_WhenTheLoopCompacts()
    {
        // Arrange: the agent's turns report no usage, so the task's tokens are the summary's alone.
        var toolTurns = 6;

        // Act
        var run = await RunAsync(toolTurns, resultChars: 4_000, MidWindow);

        // Assert
        Assert.Equal(AgentExitReason.Completed, run.Result.ExitReason);
        Assert.Equal(50, run.Result.TokensUsed);
        Assert.Equal(50, Assert.Single(run.Observer.Notices).SummaryTokens);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldNotTrim_WhenTheConversationPassesFortyMessagesUnderTheTrigger()
    {
        // Arrange: 25 turns make 52 messages, far under a million-token window.
        var toolTurns = 25;

        // Act
        var run = await RunAsync(toolTurns, resultChars: 20, LargeWindow);

        // Assert
        var last = run.Client.AgentRequests[^1];
        Assert.Equal(52, last.Count);
        Assert.DoesNotContain(last, m => m.Text.StartsWith("[Context trimmed", StringComparison.Ordinal));
        Assert.Empty(run.Client.SummaryRequests);
        Assert.Empty(run.Observer.Notices);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldRevertToTheTrimAndSaySo_WhenThreeCompactionsFailInARow()
    {
        // Arrange: every summary call throws; the conversation crosses 750 tokens at the eighth turn.
        var toolTurns = 25;

        // Act
        var run = await RunAsync(toolTurns, resultChars: 400, SmallWindow, summaryFails: true);

        // Assert
        Assert.Equal(
            [AgentCompactionOutcomes.Failed, AgentCompactionOutcomes.Failed, AgentCompactionOutcomes.Reverted],
            run.Observer.Notices.Select(n => n.Outcome));
        Assert.Equal(3, run.Client.SummaryRequests.Count);
        var last = run.Client.AgentRequests[^1];
        Assert.True(last.Count <= 40, $"the trim keeps 40 messages at most, the last request held {last.Count}");
        Assert.StartsWith("[Context trimmed", last[2].Text, StringComparison.Ordinal);
    }

    public static TheoryData<string> DisabledCases => ["globally", "for the agent"];

    [Theory]
    [Trait("BR", "CH-432")]
    [MemberData(nameof(DisabledCases))]
    public async System.Threading.Tasks.Task ShouldReproduceTodaysTrimExactly_WhenCompactionIsDisabled(string where)
    {
        // Arrange: the same run without any compaction is today's behaviour.
        var today = await RunAsync(toolTurns: 25, resultChars: 400, window: null);
        var globally = where == "globally";

        // Act
        var disabled = await RunAsync(toolTurns: 25, resultChars: 400, SmallWindow,
            options: new ContextCompactionOptions { Enabled = !globally, KeepRecentTurns = 2 },
            agentCompaction: globally ? null : new AgentCompaction { Enabled = false });

        // Assert
        Assert.Equal(Render(today.Client.AgentRequests), Render(disabled.Client.AgentRequests));
        Assert.Empty(disabled.Client.SummaryRequests);
        Assert.Empty(disabled.Observer.Notices);
        Assert.StartsWith("[Context trimmed", disabled.Client.AgentRequests[^1][2].Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldRunThePressureStageBeforeCompaction_WhenOneIsRegistered()
    {
        // Arrange: a stage that prunes the tool results from half the window keeps the trigger out of reach.
        var stage = new FakeContextPressureStage { FromShare = 0.5 };

        // Act
        var run = await RunAsync(toolTurns: 12, resultChars: 400, SmallWindow, stages: [stage]);

        // Assert
        Assert.Equal(13, stage.Pressures.Count);
        Assert.Contains(stage.Pressures, p => p.FillShare >= 0.5);
        Assert.All(stage.Pressures, p => Assert.Equal(750, p.TriggerTokens));
        Assert.Empty(run.Client.SummaryRequests);
        Assert.Empty(run.Observer.Notices);
        Assert.Contains(run.Client.AgentRequests[^1], m => m.Contents.OfType<FunctionResultContent>().Any(r => Equals(r.Result, "pruned")));
    }
}
