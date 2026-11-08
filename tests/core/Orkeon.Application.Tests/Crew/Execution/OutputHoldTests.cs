using Orkeon.Application.Crew.Execution;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>FWK review LLM-7: what a guarded task emits keeps its order, even while the hold is being released.</summary>
public sealed class OutputHoldTests
{
    [Fact]
    public async System.Threading.Tasks.Task An_emission_that_arrives_while_the_release_is_flushing_waits_for_the_held_ones()
    {
        var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var flushing = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var hold = new OutputHold();
        hold.Hold(() =>
        {
            flushing.Set();
            resume.Wait(TestContext.Current.CancellationToken);
            order.Enqueue("held");
        });

        var release = System.Threading.Tasks.Task.Run(hold.Release, TestContext.Current.CancellationToken);
        flushing.Wait(TestContext.Current.CancellationToken);
        hold.Hold(() => order.Enqueue("late"));
        resume.Set();
        await release;

        Assert.Equal(["held", "late"], order);
    }

    [Fact]
    public void After_the_release_an_emission_passes_straight_through()
    {
        var order = new List<string>();
        var hold = new OutputHold();
        hold.Hold(() => order.Add("held"));

        hold.Release();
        hold.Hold(() => order.Add("after"));

        Assert.Equal(["held", "after"], order);
    }
}
