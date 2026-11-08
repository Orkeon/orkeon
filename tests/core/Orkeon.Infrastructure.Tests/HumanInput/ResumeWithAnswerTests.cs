using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.HumanInput;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>
/// FWK-23: a question is matched to its answer by its id alone. A model that words the question differently on the
/// resumed run still gets the answer; one the human has not answered yet is shown with the prompt asked the first time.
/// </summary>
public sealed class ResumeWithAnswerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldConsumeTheAnswer_WhenTheResumedTaskWordsTheQuestionDifferently()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();
        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour do you want?"), Ct));
        await fx.Manager.AnswerAsync(fx.Id.ToString(), null, "blue", Ct);
        fx.LeaveRun();
        fx.EnterRun();

        var answer = await fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Pick a colour, please."), Ct);

        Assert.Equal("blue", answer);
        Assert.Null(fx.Run!.Suspension);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldShowThePersistedPrompt_WhenTheQuestionIsStillUnanswered()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Suspend);
        fx.EnterRun();
        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour do you want?"), Ct));
        fx.LeaveRun();
        fx.EnterRun();

        await Assert.ThrowsAsync<RunSuspendedException>(() => fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Pick a colour, please."), Ct));

        Assert.Equal("Which colour do you want?", (await fx.StateAsync())!.PendingInput!.Prompt);
        Assert.Equal("Which colour do you want?", fx.Notices.Last().Request.Prompt);
        Assert.Equal("Which colour do you want?", fx.Run!.Suspension!.Prompt);
    }
}
