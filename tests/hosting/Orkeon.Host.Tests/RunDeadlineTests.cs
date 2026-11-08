using Orkeon.Infrastructure.HumanInput;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Host.Tests;

/// <summary>FWK final review H1: the run timeout counts work, not the wait for a person, so the hold timeout can fire.</summary>
public sealed class RunDeadlineTests
{
    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "FWK-23")]
    public void ShouldExpire_WhenTheBudgetIsSpentWithoutAQuestion()
    {
        var clock = new ManualTimeProvider();
        var expired = 0;
        using var deadline = new RunDeadline(clock, s_runTimeout, new PendingInputBroker(), "run-1", () => expired++);

        clock.Advance(s_runTimeout);

        Assert.Equal(1, expired);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldNotExpire_WhileTheRunHoldsOnAQuestion_AndResumeWithTheRemainingBudget()
    {
        var clock = new ManualTimeProvider();
        var broker = new PendingInputBroker();
        var expired = 0;
        using var deadline = new RunDeadline(clock, s_runTimeout, broker, "run-1", () => expired++);

        clock.Advance(TimeSpan.FromMinutes(10));
        var waiting = broker.WaitAsync("run-1", "ask#1", Ct);
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(0, expired);

        Assert.True(broker.TryComplete("run-1", "ask#1", "green"));
        Assert.Equal("green", await waiting);
        clock.Advance(TimeSpan.FromMinutes(19));
        Assert.Equal(0, expired);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, expired);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldKeepTheClockStopped_UntilTheLastOfSeveralQuestionsIsReleased()
    {
        var clock = new ManualTimeProvider();
        var broker = new PendingInputBroker();
        var expired = 0;
        using var deadline = new RunDeadline(clock, s_runTimeout, broker, "run-1", () => expired++);

        var first = broker.WaitAsync("run-1", "a", Ct);
        var second = broker.WaitAsync("run-1", "b", Ct);
        broker.TryComplete("run-1", "a", "x");
        await first;
        clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(0, expired);
        broker.TryComplete("run-1", "b", "y");
        await second;
        clock.Advance(s_runTimeout);
        Assert.Equal(1, expired);
    }
}
