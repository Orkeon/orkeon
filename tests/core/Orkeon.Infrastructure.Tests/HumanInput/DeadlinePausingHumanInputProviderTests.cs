using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>
/// CH-01 - the time a task waits for a human does not count against its deadline: the decorator
/// pauses the running deadline for the duration of the call. On a manual clock: no real waiting.
/// </summary>
public sealed class DeadlinePausingHumanInputProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static HumanInputContext NewContext() =>
        HumanInputContext.CreateTextInput(AgentId.Create(), TaskId.Create(), "Which option?");

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldNotCountHumanWait_WhenHumanInputTakesLongerThanLimit()
    {
        var clock = new ManualTimeProvider();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new MockHumanInputProvider { PendingInput = pending };
        var provider = new DeadlinePausingHumanInputProvider(inner);
        using var deadline = TaskDeadline.Begin(Limit, clock, Ct);

        var call = provider.GetInputAsync(NewContext(), Ct);
        await inner.InputEntered.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        clock.Advance(Limit * 10);
        pending.SetResult("the answer");
        var answer = await call.WaitAsync(Polling.DefaultTimeout, Ct);

        Assert.Equal("the answer", answer);
        Assert.False(deadline.HasExpired);
        Assert.False(deadline.Token.IsCancellationRequested);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldResumeCountdown_WhenHumanAnswers()
    {
        var clock = new ManualTimeProvider();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new MockHumanInputProvider { PendingInput = pending };
        var provider = new DeadlinePausingHumanInputProvider(inner);
        using var deadline = TaskDeadline.Begin(Limit, clock, Ct);
        clock.Advance(TimeSpan.FromSeconds(10));

        var call = provider.GetInputAsync(NewContext(), Ct);
        await inner.InputEntered.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        clock.Advance(Limit * 10);
        pending.SetResult("the answer");
        await call.WaitAsync(Polling.DefaultTimeout, Ct);

        // 10 s used before the wait: 20 s remain, so 19 s more is still inside, 2 s more is past.
        clock.Advance(TimeSpan.FromSeconds(19));
        Assert.False(deadline.HasExpired);
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(deadline.HasExpired);
        Assert.True(deadline.Token.IsCancellationRequested);
    }
}
