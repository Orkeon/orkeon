using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>FWK-23: a question to a human is identified by its task, persisted, and either held for or suspended on.</summary>
public sealed class DurableHumanInputProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldDeriveADeterministicRequestId_WhenATaskAsksTwice()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun("ask#1");

        var first = await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("one?"), Ct));
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "a", Ct);
        fx.LeaveRun();
        fx.EnterRun();
        await fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("one?"), Ct);
        var second = await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("two?"), Ct));

        Assert.Equal("ask#1.1", first.RequestId);
        Assert.Equal("ask#1.2", second.RequestId);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAskAgain_WhenTheSameTaskRunsAgainAndOnlyItsFirstInvocationWasAnswered()
    {
        // A graph cycle runs one task twice: its invocations are t#1 and t#2, and each has its own questions.
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun("t#1");
        var first = await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("again?"), Ct));
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "first answer", Ct);
        fx.LeaveRun();
        fx.EnterRun("t#1");
        Assert.Equal("first answer", await fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("again?"), Ct));
        fx.EnterTask("t#2");

        var second = await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("again?"), Ct));

        Assert.Equal("t#1.1", first.RequestId);
        Assert.Equal("t#2.1", second.RequestId);
        var state = await fx.StateAsync();
        Assert.Equal("t#2.1", state!.PendingInput!.RequestId);
        Assert.Equal("t#2", state.PendingInput.TaskKey);
        Assert.Equal("first answer", state.AnsweredInputs["t#1.1"]);
        Assert.DoesNotContain("t#2.1", state.AnsweredInputs.Keys);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldKeepOneQuestionPending_AndAskTheOtherAgain_WhenTwoTasksAskAtOnce()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun("unused#1");
        async System.Threading.Tasks.Task<string?> AskAs(string task)
        {
            await System.Threading.Tasks.Task.Yield();
            using var scope = Orkeon.Infrastructure.Checkpointing.TaskInputScope.Enter(task);
            try
            {
                return await fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask($"{task}?"), Ct);
            }
            catch (RunSuspendedException)
            {
                return null;
            }
        }

        // Two tasks of one wave ask together: both stop, and exactly one question is persisted.
        var together = await System.Threading.Tasks.Task.WhenAll(AskAs("a#1"), AskAs("b#1"));
        Assert.All(together, answer => Assert.Null(answer));
        var pending = (await fx.StateAsync())!.PendingInput!;
        var other = pending.RequestId == "a#1.1" ? "b#1" : "a#1";
        var otherId = other + ".1";
        Assert.NotNull(fx.Run!.Suspension);

        // The first is answered and the run resumes: the one that was not persisted is asked again, not lost.
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "first", Ct);
        fx.LeaveRun();
        fx.EnterRun("unused#1");
        var answered = await AskAs(pending.TaskKey!);
        var again = await AskAs(other);

        Assert.Equal("first", answered);
        Assert.Null(again);
        var state = await fx.StateAsync();
        Assert.Equal(otherId, state!.PendingInput!.RequestId);
        Assert.DoesNotContain(otherId, state.AnsweredInputs.Keys);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldPersistTheQuestionAndSuspendTheRun_WhenThePolicyIsSuspend()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();

        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct));

        var state = await fx.StateAsync();
        Assert.Equal(SessionPhase.WaitingForInput, state!.Phase);
        Assert.Equal("Which colour?", state.PendingInput!.Prompt);
        Assert.Equal(PendingInputKind.Text, state.PendingInput.Kind);
        Assert.Equal("ask#1", state.PendingInput.TaskKey);
        Assert.Equal("ask#1.1", fx.Run!.Suspension!.RequestId);
        Assert.Equal(0, fx.Inner.GetInputCallCount);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldTellTheObserversTheRequestId_WhenSuspending()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();

        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct));

        var notice = Assert.Single(fx.Notices);
        Assert.Equal(HumanInputNoticeKind.Needed, notice.Kind);
        Assert.Equal("ask#1.1", notice.Request.RequestId);
        Assert.True(notice.Suspending);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReturnTheStoredAnswerWithoutAskingAnyone_WhenAResumedTaskAsksAgain()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();
        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct));
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "green", Ct);
        fx.LeaveRun();
        fx.EnterRun(startSession: true);

        var answer = await fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct);

        Assert.Equal("green", answer);
        Assert.Null(fx.Run!.Suspension);
        Assert.Equal(0, fx.Inner.GetInputCallCount);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReadAConfirmationFromTheAnswer_WhenTheKindIsConfirm()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();
        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetConfirmationAsync(DurableHumanInputFixture.Ask("Sure?", "approval"), Ct));
        Assert.Equal(PendingInputKind.Confirm, (await fx.StateAsync())!.PendingInput!.Kind);
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "oui", Ct);
        fx.LeaveRun();
        fx.EnterRun();

        Assert.True(await fx.Provider.GetConfirmationAsync(DurableHumanInputFixture.Ask("Sure?", "approval"), Ct));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldReadSeveralChoicesFromTheAnswer_WhenTheKindIsMultiChoice()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();
        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetMultiChoiceAsync(DurableHumanInputFixture.Ask("Which?", "multi", "a", "b", "c"), Ct));
        var pending = (await fx.StateAsync())!.PendingInput!;
        Assert.Equal(PendingInputKind.MultiChoice, pending.Kind);
        Assert.Equal(["a", "b", "c"], pending.Choices);
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "[\"a\",\"c\"]", Ct);
        fx.LeaveRun();
        fx.EnterRun();

        var answer = await fx.Provider.GetMultiChoiceAsync(DurableHumanInputFixture.Ask("Which?", "multi", "a", "b", "c"), Ct);

        Assert.Equal(["a", "c"], answer);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldDelegateToTheInnerProvider_WhenNoTaskIsRunning()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.Inner.SetInputResult("from inner");

        var answer = await fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("anything?"), Ct);

        Assert.Equal("from inner", answer);
        Assert.Equal(1, fx.Inner.GetInputCallCount);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldRecordTheAnswerAndRunAgain_WhenTheBrokerAnswersAHeldQuestion()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold);
        fx.EnterRun();

        var call = fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct);
        await fx.Needed.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        Assert.Equal("ask#1.1", fx.Broker.HeldRequestId(fx.Id.ToString()));
        Assert.True(fx.Broker.TryComplete(fx.Id.ToString(), "ask#1.1", "red"));
        var answer = await call.WaitAsync(Polling.DefaultTimeout, Ct);

        Assert.Equal("red", answer);
        var state = await fx.StateAsync();
        Assert.Equal(SessionPhase.Running, state!.Phase);
        Assert.Equal("red", state.AnsweredInputs["ask#1.1"]);
        Assert.Null(fx.Run!.Suspension);
        Assert.Contains(fx.Notices, n => n.Kind == HumanInputNoticeKind.Given && n.Request.RequestId == "ask#1.1");
        Assert.False(fx.Notices.First().Suspending);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAskTheInnerProviderAndRecordItsAnswer_WhenTheInnerAsksAPerson()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold);
        fx.Inner.AsksAPerson = true;
        fx.Inner.SetInputResult("typed");
        fx.EnterRun();

        var answer = await fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct);

        Assert.Equal("typed", answer);
        Assert.Equal("ask#1.1", fx.Inner.LastGetInputContext!.Metadata[HumanInputPauseMetadata.RequestId]);
        Assert.Equal("typed", (await fx.StateAsync())!.AnsweredInputs["ask#1.1"]);
    }
}
