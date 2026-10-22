using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>RT-02: the final release closes a crew-gate permit; a take on a closed permit never holds a slot.</summary>
public sealed class GatePermitTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task Take_after_the_final_release_gives_the_slot_back_and_does_not_hold_it()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var permit = new GatePermit(gate);
        await permit.AcquireAsync(TestContext.Current.CancellationToken).WaitAsync(Bound, TestContext.Current.CancellationToken);
        permit.Release();
        permit.Close();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => permit.AcquireAsync(TestContext.Current.CancellationToken).WaitAsync(Bound, TestContext.Current.CancellationToken));

        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task Take_pending_when_the_permit_closes_gives_its_slot_back()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var permit = new GatePermit(gate);
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        var retake = permit.AcquireAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.False(retake.IsCompleted);

        permit.Close();
        gate.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retake.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task Hand_back_during_a_wait_does_not_close_the_permit()
    {
        using var gate = new SemaphoreSlim(1, 1);
        var permit = new GatePermit(gate);
        await permit.AcquireAsync(TestContext.Current.CancellationToken).WaitAsync(Bound, TestContext.Current.CancellationToken);
        permit.Release();

        await permit.AcquireAsync(TestContext.Current.CancellationToken).WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(0, gate.CurrentCount);

        permit.Release();
        Assert.Equal(1, gate.CurrentCount);
    }
}
