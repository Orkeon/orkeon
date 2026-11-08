using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>FWK-23: a held run waits for its answer up to the hold timeout, then suspends - the question stays, nothing is refused.</summary>
public sealed class HoldTimeoutTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(30);

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldSuspendTheRunAndKeepTheQuestion_WhenTheHoldTimeoutExpires()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold, Limit);
        fx.EnterRun();

        var call = fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct);
        await fx.Needed.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        Assert.False(call.IsCompleted);
        fx.Clock.Advance(Limit + TimeSpan.FromSeconds(1));

        var suspended = await Assert.ThrowsAsync<RunSuspendedException>(() => call.WaitAsync(Polling.DefaultTimeout, Ct));

        Assert.Equal("ask#1.1", suspended.RequestId);
        var state = await fx.StateAsync();
        Assert.Equal(SessionPhase.WaitingForInput, state!.Phase);
        Assert.Equal("ask#1.1", state.PendingInput!.RequestId);
        Assert.Equal("ask#1.1", fx.Run!.Suspension!.RequestId);
        Assert.Null(fx.Broker.HeldRequestId(fx.Id.ToString()));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldKeepWaiting_WhenTheClockIsBeforeTheHoldTimeout()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold, Limit);
        fx.EnterRun();

        var call = fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct);
        await fx.Needed.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        fx.Clock.Advance(Limit - TimeSpan.FromSeconds(1));
        Assert.True(fx.Broker.TryComplete(fx.Id.ToString(), null, "late but fine"));

        Assert.Equal("late but fine", await call.WaitAsync(Polling.DefaultTimeout, Ct));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldHoldWithoutLimit_WhenNoHoldTimeoutIsSet()
    {
        using var fx = new DurableHumanInputFixture(HumanInputPause.Hold);
        fx.EnterRun();

        var call = fx.Provider.GetInputAsync(DurableHumanInputFixture.Ask("Which colour?"), Ct);
        await fx.Needed.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        fx.Clock.Advance(TimeSpan.FromDays(30));

        Assert.False(call.IsCompleted);
        Assert.True(fx.Broker.TryComplete(fx.Id.ToString(), null, "finally"));
        Assert.Equal("finally", await call.WaitAsync(Polling.DefaultTimeout, Ct));
    }
}
