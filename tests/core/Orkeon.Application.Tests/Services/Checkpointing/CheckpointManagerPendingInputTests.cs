using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Application.Tests.Services.Checkpointing;

/// <summary>FWK-23: the question a run waits on is kept with its checkpoint session, in the same store.</summary>
public sealed class CheckpointManagerPendingInputTests
{
    private readonly InMemoryStateStore _store = new();
    private readonly CheckpointManager _manager;

    public CheckpointManagerPendingInputTests() =>
        _manager = new CheckpointManager(_store, NullLogger<CheckpointManager>.Instance);

    private static PendingInputRequest Question(string id, string prompt = "Which one?") =>
        new() { RequestId = id, Kind = PendingInputKind.Text, Prompt = prompt, TaskKey = "ask#1" };

    private async System.Threading.Tasks.Task<string> StartAsync()
    {
        var id = ExecutionId.New();
        await _manager.StartSessionAsync(id, "crew", TestContext.Current.CancellationToken);
        return id.ToString();
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldPersistTheQuestionAndWaitForInput_WhenAsked()
    {
        var session = await StartAsync();

        var asked = await _manager.AskAsync(session, Question("ask#1.1"), TestContext.Current.CancellationToken);

        Assert.Null(asked.Answer);
        Assert.Equal("ask#1.1", asked.Pending!.RequestId);
        var state = await _store.GetAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(SessionPhase.WaitingForInput, state!.Phase);
        Assert.Equal("Which one?", state.PendingInput!.Prompt);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReturnThePersistedPrompt_WhenTheSameQuestionIsAskedAgainWithAnotherPrompt()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("ask#1.1", "first wording"), TestContext.Current.CancellationToken);

        var again = await _manager.AskAsync(session, Question("ask#1.1", "second wording"), TestContext.Current.CancellationToken);

        Assert.Equal("first wording", again.Pending!.Prompt);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAcceptTheFirstAnswerOnly_WhenTwoAnswerTheSameQuestion()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("ask#1.1"), TestContext.Current.CancellationToken);

        var first = await _manager.AnswerAsync(session, "ask#1.1", "yes", TestContext.Current.CancellationToken);
        var second = await _manager.AnswerAsync(session, "ask#1.1", "no", TestContext.Current.CancellationToken);

        Assert.Equal(PendingInputAnswerOutcome.Accepted, first.Outcome);
        Assert.Equal(PendingInputAnswerOutcome.NotPending, second.Outcome);
        var state = await _store.GetAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal("yes", state!.AnsweredInputs["ask#1.1"].Answer);
        Assert.Null(state.PendingInput);
        Assert.Equal(SessionPhase.Suspended, state.Phase);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRefuseTheAnswer_WhenAnotherQuestionIsPending()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("ask#1.1"), TestContext.Current.CancellationToken);

        var result = await _manager.AnswerAsync(session, "other#1.1", "x", TestContext.Current.CancellationToken);

        Assert.Equal(PendingInputAnswerOutcome.Mismatch, result.Outcome);
        Assert.NotNull((await _store.GetAsync(session, TestContext.Current.CancellationToken))!.PendingInput);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReturnTheAnswer_WhenAResumedRunAsksTheAnsweredQuestionAgain()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("ask#1.1"), TestContext.Current.CancellationToken);
        await _manager.AnswerAsync(session, null, "blue", TestContext.Current.CancellationToken);

        var again = await _manager.AskAsync(session, Question("ask#1.1", "another wording"), TestContext.Current.CancellationToken);

        Assert.Equal("blue", again.Answer);
        Assert.Null(again.Pending);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRunAgainAndKeepTheAnswer_WhenTheRunReceivedItWhileHolding()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("ask#1.1"), TestContext.Current.CancellationToken);

        await _manager.RecordAnswerAsync(session, Question("ask#1.1"), "ok", TestContext.Current.CancellationToken);

        var state = await _store.GetAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(SessionPhase.Running, state!.Phase);
        Assert.Null(state.PendingInput);
        Assert.Equal("ok", state.AnsweredInputs["ask#1.1"].Answer);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReportAnUnknownSession_WhenAnsweringIt()
    {
        var result = await _manager.AnswerAsync("nope", null, "x", TestContext.Current.CancellationToken);

        Assert.Equal(PendingInputAnswerOutcome.UnknownSession, result.Outcome);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReportAlreadyAnswered_WhenTheSameAnswerIsGivenAgainBeforeTheResume()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("ask#1.1"), TestContext.Current.CancellationToken);
        await _manager.AnswerAsync(session, null, "blue", TestContext.Current.CancellationToken);

        var again = await _manager.AnswerAsync(session, null, "blue", TestContext.Current.CancellationToken);
        var other = await _manager.AnswerAsync(session, null, "red", TestContext.Current.CancellationToken);

        Assert.Equal(PendingInputAnswerOutcome.AlreadyAnswered, again.Outcome);
        Assert.Equal("ask#1.1", again.RequestId);
        Assert.Equal(PendingInputAnswerOutcome.NotPending, other.Outcome);
        Assert.Equal("blue", (await _store.GetAsync(session, TestContext.Current.CancellationToken))!.AnsweredInputs["ask#1.1"].Answer);
    }

    private static PendingInputRequest Approval(string id, string tool, string argsHash) =>
        new() { RequestId = id, Kind = PendingInputKind.ToolApproval, Prompt = $"Allow {tool}?", TaskKey = "ask#1", ToolName = tool, ArgsHash = argsHash };

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAskAgain_WhenTheStoredAnswerWasGivenToAnotherToolCallUnderTheSameId()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Approval("ask#1.1", "shell_command", "AAAA"), Ct);
        await _manager.AnswerAsync(session, "ask#1.1", "allow", Ct);

        var otherArgs = await _manager.AskAsync(session, Approval("ask#1.1", "shell_command", "BBBB"), Ct);

        Assert.Null(otherArgs.Answer);
        Assert.Equal("BBBB", otherArgs.Pending!.ArgsHash);
        var state = await _store.GetAsync(session, Ct);
        Assert.DoesNotContain("ask#1.1", state!.AnsweredInputs.Keys);
        Assert.Equal("BBBB", state.PendingInput!.ArgsHash);
    }

    [Theory]
    [InlineData("file_delete", "AAAA", PendingInputKind.ToolApproval)]
    [InlineData(null, null, PendingInputKind.Confirm)]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldNotReuseAnAnswer_WhenTheToolOrTheKindDiffers(string? tool, string? hash, PendingInputKind kind)
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Approval("ask#1.1", "shell_command", "AAAA"), Ct);
        await _manager.AnswerAsync(session, "ask#1.1", "allow", Ct);

        var asked = await _manager.AskAsync(session, new PendingInputRequest { RequestId = "ask#1.1", Kind = kind, Prompt = "?", ToolName = tool, ArgsHash = hash }, Ct);

        Assert.Null(asked.Answer);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReturnTheStoredAnswer_WhenTheSameCallAsksAgain()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Approval("ask#1.1", "shell_command", "AAAA"), Ct);
        await _manager.AnswerAsync(session, "ask#1.1", "allow", Ct);

        var again = await _manager.AskAsync(session, Approval("ask#1.1", "shell_command", "AAAA"), Ct);

        Assert.Equal("allow", again.Answer);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldKeepEveryQuestionOpenAndCloseOnlyTheAnsweredOne_WhenTwoTasksAsk()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("a#1.1", "A?"), Ct);
        var second = await _manager.AskAsync(session, Question("b#1.1", "B?"), Ct);

        await _manager.RecordAnswerAsync(session, Question("b#1.1", "B?"), "for b", Ct);

        Assert.Equal("b#1.1", second.Pending!.RequestId);
        var state = await _store.GetAsync(session, Ct);
        Assert.Equal("a#1.1", Assert.Single(state!.PendingInputs).RequestId);
        Assert.Equal(SessionPhase.WaitingForInput, state.Phase);
        Assert.Equal("for b", state.AnsweredInputs["b#1.1"].Answer);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAnswerTheQuestionTheRunStoppedOn_WhenTheAnswerNamesNone()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("a#1.1", "A?"), Ct);
        await _manager.AskAsync(session, Question("b#1.1", "B?"), Ct);
        await _manager.SuspendAsync(session, "b#1.1", Ct);

        var shown = await _manager.GetPendingAsync(session, Ct);
        var answered = await _manager.AnswerAsync(session, null, "yes", Ct);
        var retried = await _manager.AnswerAsync(session, null, "yes", Ct);

        Assert.Equal("b#1.1", shown!.RequestId);
        Assert.Equal(PendingInputAnswerOutcome.Accepted, answered.Outcome);
        Assert.Equal("b#1.1", answered.RequestId);
        Assert.Equal(PendingInputAnswerOutcome.AlreadyAnswered, retried.Outcome);
        var state = await _store.GetAsync(session, Ct);
        Assert.Equal("a#1.1", Assert.Single(state!.PendingInputs).RequestId);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRefuseAnAnswerThatNamesNone_WhenSeveralQuestionsWaitAndTheRunStoppedOnNone()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("a#1.1", "A?"), Ct);
        await _manager.AskAsync(session, Question("b#1.1", "B?"), Ct);

        var answered = await _manager.AnswerAsync(session, null, "yes", Ct);

        Assert.Equal(PendingInputAnswerOutcome.RequestIdRequired, answered.Outcome);
        Assert.Equal(2, (await _store.GetAsync(session, Ct))!.PendingInputs.Count);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldForgetTheStopOfTheLastRun_WhenTheSessionContinues()
    {
        var id = ExecutionId.New();
        await _manager.StartSessionAsync(id, "crew", Ct);
        await _manager.AskAsync(id.ToString(), Question("a#1.1"), Ct);
        await _manager.SuspendAsync(id.ToString(), "a#1.1", Ct);

        await _manager.StartSessionAsync(id, "crew", Ct);

        Assert.Null((await _store.GetAsync(id.ToString(), Ct))!.SuspendedOn);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
