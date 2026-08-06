using Orkeon.Cli.Scripting.Progress;

namespace Orkeon.Cli.Scripting.Tests.Progress;

public sealed class ProgressBrokerTests
{
    private static ProgressSnapshot Snap(
        string label = "Compacting conversation",
        int? step = null,
        int? total = null,
        double? percent = null,
        string? ticket = null,
        DateTimeOffset startedAt = default)
        => new() { Label = label, Step = step, Total = total, Percent = percent, Ticket = ticket, StartedAt = startedAt };

    [Fact]
    public void Report_publishes_and_Current_reads_back()
    {
        var broker = new ProgressBroker();
        Assert.Null(broker.Current);

        broker.Report(Snap(step: 2, total: 5));

        var current = broker.Current;
        Assert.NotNull(current);
        Assert.Equal("Compacting conversation", current!.Label);
        Assert.Equal(2, current.Step);
    }

    [Fact]
    public void Same_label_updates_keep_the_original_StartedAt()
    {
        // The elapsed readout must not restart on every phase report.
        var broker = new ProgressBroker();
        var t0 = DateTimeOffset.UtcNow.AddSeconds(-30);
        broker.Report(Snap(step: 1, total: 5, startedAt: t0));
        broker.Report(Snap(step: 2, total: 5, startedAt: DateTimeOffset.UtcNow));

        Assert.Equal(t0, broker.Current!.StartedAt);
    }

    [Fact]
    public void A_new_label_restarts_the_clock()
    {
        var broker = new ProgressBroker();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        broker.Report(Snap(label: "one", startedAt: t0));
        var t1 = DateTimeOffset.UtcNow;
        broker.Report(Snap(label: "two", startedAt: t1));

        Assert.Equal(t1, broker.Current!.StartedAt);
    }

    [Fact]
    public void ClearTicket_only_clears_its_own_operation()
    {
        // The completion path of one instance must not erase a newer operation's bar.
        var broker = new ProgressBroker();
        broker.Report(Snap(ticket: "t1"));
        broker.ClearTicket("t2");
        Assert.NotNull(broker.Current);

        broker.ClearTicket("t1");
        Assert.Null(broker.Current);
    }

    [Fact]
    public void ClearLabel_only_clears_its_own_operation()
    {
        var broker = new ProgressBroker();
        broker.Report(Snap(label: "deploy"));
        broker.ClearLabel("other");
        Assert.NotNull(broker.Current);

        broker.ClearLabel("deploy");
        Assert.Null(broker.Current);
    }

    [Theory]
    [InlineData(null, null, 34.0, 0.34)]      // percent wins
    [InlineData(2, 5, null, 0.4)]             // step/total
    [InlineData(7, 5, null, 1.0)]             // overshoot clamps
    public void Ratio_derives_from_percent_first_then_steps(int? step, int? total, double? percent, double expected)
    {
        var snap = Snap(step: step, total: total, percent: percent);
        Assert.NotNull(snap.Ratio);
        Assert.Equal(expected, snap.Ratio!.Value, precision: 6);
    }

    [Fact]
    public void Ratio_is_null_for_indeterminate_operations()
    {
        Assert.Null(Snap().Ratio);
        Assert.Null(Snap(step: 3).Ratio); // a step count without a total measures nothing
    }
}
