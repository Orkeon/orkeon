using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class AppendCoalescerTests
{
    [Fact]
    public void ShouldDispatchOnce_WhenABurstIsPostedBetweenTwoFrames()
    {
        var dispatcher = new FakeUiDispatcher();
        var applied = new List<IReadOnlyList<int>>();
        var coalescer = new AppendCoalescer<int>(dispatcher, batch => applied.Add([.. batch]));

        for (var i = 0; i < 1000; i++) coalescer.Post(i);

        Assert.Equal(1, dispatcher.InvokeCount);
        Assert.Empty(applied);
        Assert.Equal(1000, coalescer.PendingCount);

        dispatcher.RunPending();

        var batch = Assert.Single(applied);
        Assert.Equal(Enumerable.Range(0, 1000), batch);
        Assert.Equal(0, coalescer.PendingCount);
    }

    [Fact]
    public void ShouldScheduleANewDispatch_WhenPostedAfterTheDrain()
    {
        var dispatcher = new FakeUiDispatcher();
        var applied = new List<IReadOnlyList<string>>();
        var coalescer = new AppendCoalescer<string>(dispatcher, batch => applied.Add([.. batch]));

        coalescer.Post("a");
        coalescer.Post("b");
        dispatcher.RunPending();
        coalescer.Post("c");
        dispatcher.RunPending();

        Assert.Equal(2, dispatcher.InvokeCount);
        Assert.Equal(["a", "b"], applied[0]);
        Assert.Equal(["c"], applied[1]);
    }

    [Fact]
    public void ShouldKeepEveryItem_WhenPostedFromSeveralThreads()
    {
        var dispatcher = new FakeUiDispatcher();
        var applied = new List<int>();
        var coalescer = new AppendCoalescer<int>(dispatcher, batch => applied.AddRange(batch));

        Parallel.For(0, 8, t =>
        {
            for (var i = 0; i < 500; i++) coalescer.Post((t * 1000) + i);
        });
        dispatcher.RunPending();

        Assert.Equal(1, dispatcher.InvokeCount);
        Assert.Equal(4000, applied.Count);
        Assert.Equal(4000, applied.Distinct().Count());
    }
}
