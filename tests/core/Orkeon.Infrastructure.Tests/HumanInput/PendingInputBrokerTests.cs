using Orkeon.Infrastructure.HumanInput;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>FWK final review H16: a run may hold on several questions; the broker lists them, oldest first.</summary>
public sealed class PendingInputBrokerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldListEveryHeldQuestionOfTheRun_OldestFirst()
    {
        var broker = new PendingInputBroker();
        var first = broker.WaitAsync("run", "zeta", Ct);
        var second = broker.WaitAsync("run", "alpha", Ct);
        var other = broker.WaitAsync("other", "q", Ct);

        Assert.Equal(["zeta", "alpha"], broker.HeldRequestIds("run"));
        Assert.Equal("zeta", broker.HeldRequestId("run"));

        broker.TryComplete("run", null, "a");
        Assert.Equal("a", await first);
        Assert.Equal(["alpha"], broker.HeldRequestIds("run"));
        broker.TryComplete("run", "alpha", "b");
        broker.TryComplete("other", "q", "c");
        await second;
        await other;
        Assert.Empty(broker.HeldRequestIds("run"));
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async Task ShouldTellTheWatcherWhenTheFirstQuestionIsHeldAndWhenTheLastIsReleased()
    {
        var broker = new PendingInputBroker();
        var states = new List<bool>();
        using var watch = broker.WatchHolds("run", states.Add);

        var a = broker.WaitAsync("run", "a", Ct);
        var b = broker.WaitAsync("run", "b", Ct);
        broker.TryComplete("run", "a", "x");
        await a;
        Assert.Equal([true], states);
        broker.TryComplete("run", "b", "y");
        await b;

        Assert.Equal([true, false], states);
    }
}
