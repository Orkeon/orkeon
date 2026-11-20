using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>
/// #434: before compaction, the decision model judges each older read-tool result of a loop - keep, head or drop -
/// from the call alone; with the decision circuit off, nothing is asked and nothing is pruned.
/// </summary>
public sealed class ToolResultPruningStageTests
{
    private const string ReadTool = "file_read";
    private const string WriteTool = "file_write";

    // A usable window of 10,000 tokens: the results are judged from 4,000, compaction triggers at 7,500.
    private static readonly ContextWindow Window = new(10_000, ContextWindowSources.Table);

    private readonly FakeDecisionModel _model = new();
    private readonly FakeAgentPruneObserver _observer = new();
    private readonly RecordingLogger _logger = new();

    private ToolResultPruningStage Stage(
        DecisionCircuit? circuit = null, DecisionCircuit? scope = null, bool withModel = true, ToolResultPruningOptions? options = null)
    {
        var services = new ServiceCollection();
        if (withModel)
            services.AddSingleton<IDecisionModel>(_model);
        if (circuit is not null)
            services.AddSingleton(circuit);
        return new ToolResultPruningStage(services.BuildServiceProvider(), options ?? new ToolResultPruningOptions { Enabled = true },
            scope, _observer, _logger);
    }

    // The system prompt, the task, `old` turns that read a file each, then three recent turns the last six messages hold.
    private static (List<ChatMessage> Messages, ChatConversationView View) Conversation(
        int old = 3, string tool = ReadTool, int resultChars = 2_000)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, "sys"), new(ChatRole.User, "Summarise the parser") };
        for (var call = 1; call <= old + 3; call++)
        {
            var id = $"call-{call}";
            messages.Add(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent(id, tool, new Dictionary<string, object?> { ["path"] = $"src/f{call}.cs" })]));
            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(id, Body(call, resultChars))]));
        }
        return (messages, new ChatConversationView(messages, (name, _) => name == ReadTool));
    }

    private static string Body(int call, int chars) => string.Concat($"SECRET-{call}:", new string('x', chars))[..chars];

    // The tool result of call n sits at index 2n + 1.
    private static string ResultOf(List<ChatMessage> messages, int call) =>
        messages[(2 * call) + 1].Contents.OfType<FunctionResultContent>().Single().Result?.ToString() ?? string.Empty;

    private static string Render(List<ChatMessage> messages) =>
        string.Join("|", messages.Select(m => m.Text + string.Concat(m.Contents.OfType<FunctionResultContent>().Select(r => r.Result))));

    private static ContextPressure Pressure(int estimated = 5_000, AgentCompaction? agent = null) => new()
    {
        TaskId = "t1",
        AgentRole = "Reader",
        AgentCompaction = agent,
        EstimatedTokens = estimated,
        Window = Window,
        ReservedOutputTokens = 0,
        TriggerTokens = 7_500,
    };

    private void Answer(params (int Call, double Needed, double Whole)[] calls) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = calls
                .SelectMany(c => new[] { (ToolResultPruningStage.NeededName(c.Call), c.Needed), (ToolResultPruningStage.WholeName(c.Call), c.Whole) })
                .ToImmutableDictionary(a => a.Item1, a => (DecisionAnswer)new NoulAnswer { Noul = a.Item2 }),
        };

    private void DropAll(int calls) => Answer([.. Enumerable.Range(1, calls).Select(c => (c, 0.1, 0.1))]);

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldDropCutOrKeepEachResultAsTheAnswersSay_WhenTheEstimateReachesTheFillShare()
    {
        // Arrange: call 1 is no longer needed, call 2 is needed in part, call 3 whole.
        var (messages, view) = Conversation();
        Answer((1, 0.1, 0.1), (2, 0.9, 0.2), (3, 0.9, 0.9));

        // Act
        var changed = await Stage().RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(changed);
        Assert.StartsWith("[pruned to save context: the result of file_read", ResultOf(messages, 1), StringComparison.Ordinal);
        Assert.Equal("call-1", messages[3].Contents.OfType<FunctionResultContent>().Single().CallId);
        Assert.StartsWith(Body(2, ToolResultPruningOptions.DefaultHeadChars), ResultOf(messages, 2), StringComparison.Ordinal);
        Assert.Contains("1700 of 2000 characters cut here", ResultOf(messages, 2), StringComparison.Ordinal);
        Assert.Equal(Body(3, 2_000), ResultOf(messages, 3));
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldShowTheDecisionModelTheCallsAndNeverTheirResults_WhenItJudgesThem()
    {
        // Arrange
        var (_, view) = Conversation();

        // Act
        await Stage().RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(_model.Requests);
        var state = request.State.ToString();
        Assert.Contains("Summarise the parser", state, StringComparison.Ordinal);
        Assert.Contains("1. file_read {\"path\":\"src/f1.cs\"}: a result of 2000 characters, 5 model turn(s) ago", state, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-", state, StringComparison.Ordinal);
        Assert.Equal(["needed_1", "needed_2", "needed_3", "whole_1", "whole_2", "whole_3"], request.Questions.Keys.Order(StringComparer.Ordinal));
        Assert.All(request.Questions.Values, q => Assert.IsType<NoulQuestion>(q));
    }

    public static TheoryData<string, int, string, int> NothingToOffer => new()
    {
        { "the results of a write tool", 3, WriteTool, 2_000 },
        { "the last six messages only", 0, ReadTool, 2_000 },
        { "results of twice the head or less", 3, ReadTool, 600 },
    };

    [Theory]
    [Trait("BR", "CH-434")]
    [MemberData(nameof(NothingToOffer))]
    public async System.Threading.Tasks.Task ShouldAskNothing_WhenNoResultIsWorthAVerdict(string conversation, int old, string tool, int resultChars)
    {
        // Arrange
        _ = conversation;
        var (messages, view) = Conversation(old, tool, resultChars);
        var before = Render(messages);

        // Act
        var changed = await Stage().RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.Empty(_model.Requests);
        Assert.Equal(before, Render(messages));
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData(DecisionCircuitCause.Switch)]
    [InlineData(DecisionCircuitCause.Settings)]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCallAndPruneNothing_WhenTheCircuitIsOff(DecisionCircuitCause cause)
    {
        // Arrange
        var (messages, view) = Conversation();
        var before = Render(messages);
        DropAll(3);

        // Act
        var changed = await Stage(DecisionCircuit.OffBy(cause)).RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.Empty(_model.Requests);
        Assert.Equal(before, Render(messages));
        Assert.Empty(_observer.Notices);
        Assert.Contains(DecisionCircuit.OffBy(cause).OffReason!,
            Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCall_WhenTheRunScopesCircuitIsOff()
    {
        // Arrange
        var (_, view) = Conversation();
        DropAll(3);

        // Act
        var changed = await Stage(DecisionCircuit.On, scope: DecisionCircuit.Off).RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPruneNothingAndSayWhy_WhenTheHostRegistersNoDecisionModel()
    {
        // Arrange
        var (_, view) = Conversation();

        // Act
        var changed = await Stage(withModel: false).RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.Contains(DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!,
            Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnOnce_WhenTheCircuitIsOffAtSeveralCalls()
    {
        // Arrange
        var stage = Stage(DecisionCircuit.Off);
        await stage.RelieveAsync(Pressure(), Conversation().View, TestContext.Current.CancellationToken);

        // Act
        await stage.RelieveAsync(Pressure(), Conversation().View, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldAskNothing_WhenTheEstimateIsUnderTheFillShare()
    {
        // Arrange: 3,999 tokens, under 0.4 of the usable window.
        var (_, view) = Conversation();

        // Act
        var changed = await Stage().RelieveAsync(Pressure(estimated: 3_999), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldJudgeAtTheCompactionTrigger_WhenTheFillShareIsAboveIt()
    {
        // Arrange: a fill share of 0.9 would come after the trigger at 7,500 tokens.
        var (_, view) = Conversation();
        var stage = Stage(options: new ToolResultPruningOptions { Enabled = true, FillShare = 0.9 });

        // Act
        await stage.RelieveAsync(Pressure(estimated: 7_500), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
    }

    public static TheoryData<string, bool, bool?> PruningOff => new()
    {
        { "the host's default", false, null },
        { "the agent's prune: false over the host's", true, false },
    };

    [Theory]
    [Trait("BR", "CH-434")]
    [MemberData(nameof(PruningOff))]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCall_WhenPruningIsOff(string setting, bool hostEnabled, bool? agentPrune)
    {
        // Arrange
        _ = setting;
        var (_, view) = Conversation();
        var stage = Stage(options: new ToolResultPruningOptions { Enabled = hostEnabled });

        // Act
        var changed = await stage.RelieveAsync(Pressure(agent: new AgentCompaction { Prune = agentPrune }), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.Empty(_model.Requests);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPrune_WhenTheAgentTurnsItOnOverTheHostsDefault()
    {
        // Arrange
        var (_, view) = Conversation();
        DropAll(3);
        var stage = Stage(options: new ToolResultPruningOptions());

        // Act
        var changed = await stage.RelieveAsync(Pressure(agent: new AgentCompaction { Prune = true }), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(changed);
        Assert.Single(_model.Requests);
    }

    public static TheoryData<string, double> SettledVerdicts => new()
    {
        { "dropped", 0.1 },
        { "kept", 0.9 },
    };

    [Theory]
    [Trait("BR", "CH-434")]
    [MemberData(nameof(SettledVerdicts))]
    public async System.Threading.Tasks.Task ShouldNotOfferAResultAgain_WhenItWasJudgedAtTheLastCall(string verdict, double answer)
    {
        // Arrange: a kept result waits eight model turns before it is judged again.
        _ = verdict;
        var (_, view) = Conversation();
        Answer((1, answer, answer), (2, answer, answer), (3, answer, answer));
        var stage = Stage();
        await stage.RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Act
        await stage.RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldLeaveEveryResultAsItIs_WhenTheDecisionModelFails()
    {
        // Arrange
        var (messages, view) = Conversation();
        var before = Render(messages);
        _model.Failure = new InvalidOperationException("model down");

        // Act
        var changed = await Stage().RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.Equal(before, Render(messages));
        Assert.Contains("model down", Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
        Assert.Equal(0, Assert.Single(_observer.Notices).Judged);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldTellTheObserverWhatBecameOfTheResults_WhenTheyWereJudged()
    {
        // Arrange
        var (_, view) = Conversation();
        Answer((1, 0.1, 0.1), (2, 0.9, 0.2), (3, 0.9, 0.9));

        // Act
        await Stage().RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        var notice = Assert.Single(_observer.Notices);
        Assert.Equal(("t1", "Reader", 5_000, 4_000), (notice.TaskId, notice.AgentRole, notice.EstimatedTokens, notice.ThresholdTokens));
        Assert.Equal((3, 3, 1, 1, 1), (notice.Offered, notice.Judged, notice.Kept, notice.Cut, notice.Dropped));
        Assert.True(notice.FreedTokens > 0, $"the pruning freed {notice.FreedTokens} tokens");
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldAskEightCallsARequest_WhenMoreResultsAreOffered()
    {
        // Arrange
        var (_, view) = Conversation(old: 10);

        // Act
        await Stage().RelieveAsync(Pressure(), view, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([16, 4], _model.Requests.Select(r => r.Questions.Count));
    }
}
