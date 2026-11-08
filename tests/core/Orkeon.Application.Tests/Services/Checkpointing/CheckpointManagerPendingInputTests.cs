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
        Assert.Equal("yes", state!.AnsweredInputs["ask#1.1"]);
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
    public async System.Threading.Tasks.Task ShouldKeepTheFirstQuestion_WhenASecondOneIsAskedWhileOneIsPending()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("a#1.1"), TestContext.Current.CancellationToken);

        var second = await _manager.AskAsync(session, Question("b#1.1"), TestContext.Current.CancellationToken);

        Assert.Null(second.Pending);
        Assert.Equal("a#1.1", (await _manager.GetPendingAsync(session, TestContext.Current.CancellationToken))!.RequestId);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRunAgainAndKeepTheAnswer_WhenTheRunReceivedItWhileHolding()
    {
        var session = await StartAsync();
        await _manager.AskAsync(session, Question("ask#1.1"), TestContext.Current.CancellationToken);

        await _manager.RecordAnswerAsync(session, "ask#1.1", "ok", TestContext.Current.CancellationToken);

        var state = await _store.GetAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(SessionPhase.Running, state!.Phase);
        Assert.Null(state.PendingInput);
        Assert.Equal("ok", state.AnsweredInputs["ask#1.1"]);
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
        Assert.Equal("blue", (await _store.GetAsync(session, TestContext.Current.CancellationToken))!.AnsweredInputs["ask#1.1"]);
    }
}
