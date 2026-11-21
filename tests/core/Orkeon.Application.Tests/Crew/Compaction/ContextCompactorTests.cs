using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>
/// #432: what a compaction cuts - the recent turns it keeps, shrunk when they are all there is, a call never apart
/// from its result, and the system messages of the cut span kept beside the summary.
/// </summary>
public sealed class ContextCompactorTests
{
    // A usable window of 10,000 tokens and a trigger at 5,000: a result of 4,000 characters is about 1,000 tokens.
    private static readonly ContextWindow Window = new(10_000, ContextWindowSources.Table);

    private const string Steering = "Token budget approaching its limit for this task.";

    private readonly FakeCompactionObserver _observer = new();
    private readonly List<string> _transcripts = [];

    private ContextCompactor Compactor(int keepRecentTurns) => new(new ContextCompactorSetup
    {
        Window = Window,
        ReservedOutputTokens = 0,
        TriggerShare = 0.5,
        KeepRecentTurns = keepRecentTurns,
        TaskId = "t1",
        AgentRole = "Reader",
        Observer = _observer,
        Logger = new RecordingLogger(),
    }, (_, transcript, _) =>
    {
        _transcripts.Add(transcript);
        return System.Threading.Tasks.Task.FromResult(new CompactionSummary("The agent read the pages.", 10, null));
    });

    private static List<ChatMessage> Start() => [new(ChatRole.System, "sys"), new(ChatRole.User, "Read every page")];

    private static void AddTurn(List<ChatMessage> messages, int call, int resultChars = 4_000, string? splitText = null)
    {
        var id = $"call-{call}";
        messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(id, "read_page", arguments: null)]));
        if (splitText is not null)
            messages.Add(new ChatMessage(ChatRole.Assistant, splitText));
        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(id, new string('x', resultChars))]));
    }

    private static string? CallIdOf(ChatMessage message) =>
        message.Contents.OfType<FunctionCallContent>().FirstOrDefault()?.CallId
        ?? message.Contents.OfType<FunctionResultContent>().FirstOrDefault()?.CallId;

    // Every tool result's call is in an assistant message before it.
    private static void AssertEveryResultHasItsCall(List<ChatMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].Role != ChatRole.Tool)
                continue;
            var id = CallIdOf(messages[i]);
            Assert.Contains(messages.Take(i), m => m.Role == ChatRole.Assistant
                && m.Contents.OfType<FunctionCallContent>().Any(c => c.CallId == id));
        }
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldKeepFewerRecentTurns_WhenTheTurnsItKeepsAreAllThereIs()
    {
        // Arrange: five turns of 1,000 tokens pass the trigger, and six are kept.
        var messages = Start();
        for (var call = 1; call <= 5; call++)
            AddTurn(messages, call);
        var view = new ChatConversationView(messages);

        // Act
        await Compactor(keepRecentTurns: 6).BeforeCallAsync(view, TestContext.Current.CancellationToken);

        // Assert: four turns fit under the trigger, the first is summarised.
        Assert.Single(_transcripts);
        Assert.Equal(AgentCompactionOutcomes.Compacted, Assert.Single(_observer.Notices).Outcome);
        Assert.StartsWith("[Context compacted: 2 earlier messages", messages[2].Text, StringComparison.Ordinal);
        Assert.Equal(["call-2", "call-2", "call-3", "call-3", "call-4", "call-4", "call-5", "call-5"], messages.Skip(3).Select(CallIdOf));
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldCompactAgain_WhenTheTurnsAfterASummaryAreAllThereIsToKeep()
    {
        // Arrange: a first compaction, then a sixth turn that passes the trigger with the summary and five turns.
        var messages = Start();
        for (var call = 1; call <= 5; call++)
            AddTurn(messages, call);
        var view = new ChatConversationView(messages);
        var compactor = Compactor(keepRecentTurns: 6);
        await compactor.BeforeCallAsync(view, TestContext.Current.CancellationToken);
        AddTurn(messages, 6);

        // Act
        await compactor.BeforeCallAsync(view, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([AgentCompactionOutcomes.Compacted, AgentCompactionOutcomes.Compacted], _observer.Notices.Select(n => n.Outcome));
        Assert.Equal(["call-3", "call-3", "call-4", "call-4", "call-5", "call-5", "call-6", "call-6"], messages.Skip(3).Select(CallIdOf));
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldCountNoFailure_WhenTheLastTurnIsAllThereIs()
    {
        // Arrange: one turn alone passes the trigger: a later turn gives the cut its room.
        var messages = Start();
        AddTurn(messages, 1, resultChars: 24_000);
        var view = new ChatConversationView(messages);

        // Act
        await Compactor(keepRecentTurns: 6).BeforeCallAsync(view, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_transcripts);
        Assert.Empty(_observer.Notices);
        Assert.Equal(4, messages.Count);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldKeepEveryCallWithItsResult_WhenAResponseSplitsTheCallAndItsText()
    {
        // Arrange: each response puts its call and its text in two assistant messages, the call first.
        var messages = Start();
        for (var call = 1; call <= 6; call++)
            AddTurn(messages, call, splitText: $"Reading page {call}.");
        var view = new ChatConversationView(messages);

        // Act
        await Compactor(keepRecentTurns: 3).BeforeCallAsync(view, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentCompactionOutcomes.Compacted, Assert.Single(_observer.Notices).Outcome);
        AssertEveryResultHasItsCall(messages);
        Assert.Equal(["call-4", null, "call-4", "call-5", null, "call-5", "call-6", null, "call-6"], messages.Skip(3).Select(CallIdOf));
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public async System.Threading.Tasks.Task ShouldKeepASystemMessageOfTheCutSpanAfterTheSummary_WhenTheLoopCompacts()
    {
        // Arrange: the soft-budget steering lands between the first and the second turn.
        var messages = Start();
        AddTurn(messages, 1, resultChars: 6_000);
        messages.Add(new ChatMessage(ChatRole.System, Steering));
        for (var call = 2; call <= 4; call++)
            AddTurn(messages, call, resultChars: 6_000);
        var view = new ChatConversationView(messages);

        // Act
        await Compactor(keepRecentTurns: 2).BeforeCallAsync(view, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentCompactionOutcomes.Compacted, Assert.Single(_observer.Notices).Outcome);
        Assert.StartsWith("[Context compacted: 4 earlier messages", messages[2].Text, StringComparison.Ordinal);
        Assert.Equal(ChatRole.System, messages[3].Role);
        Assert.Equal(Steering, messages[3].Text);
        Assert.Equal(["call-3", "call-3", "call-4", "call-4"], messages.Skip(4).Select(CallIdOf));
        Assert.DoesNotContain(Steering, Assert.Single(_transcripts), StringComparison.Ordinal);
    }
}
