using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Process;

/// <summary>
/// The two-step stop: ask, then kill. Asserted on a fake handle so the ordering is checked
/// deterministically, without a real process and without a real grace period elapsing.
/// </summary>
public sealed class ProcessTerminatorTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_process_that_honours_the_signal_is_never_killed()
    {
        var handle = new FakeProcessHandle { ExitsOnGracefulStop = true };

        var outcome = await ProcessTerminator.TerminateAsync(handle, Grace, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.StoppedBySignal, outcome.Mode);
        Assert.True(outcome.GracefulStopRequested);
        Assert.Null(outcome.GracefulStopFailureReason);
        Assert.Equal(1, handle.GracefulStopRequests);
        Assert.False(handle.WasKilled);
    }

    [Fact]
    public async Task A_process_that_ignores_the_signal_is_killed_after_the_grace_period()
    {
        var handle = new FakeProcessHandle { ExitsOnGracefulStop = false };

        var outcome = await ProcessTerminator.TerminateAsync(handle, Grace, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.Killed, outcome.Mode);
        Assert.True(handle.WasKilled);

        // The order is the contract: signal, wait out the grace, then kill.
        Assert.Equal(["graceful-stop", "wait", "kill", "wait"], handle.Calls);
        Assert.Equal(Grace, handle.WaitTimeouts[0]);
    }

    [Fact]
    public async Task A_platform_without_a_graceful_stop_goes_straight_to_the_kill()
    {
        // Windows: no per-child Ctrl+C. The signal step is attempted and reported as
        // unavailable rather than skipped silently, then the kill happens immediately.
        var handle = new FakeProcessHandle { GracefulStopSucceeds = false };

        var outcome = await ProcessTerminator.TerminateAsync(handle, Grace, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.Killed, outcome.Mode);
        Assert.False(outcome.GracefulStopRequested);
        Assert.Equal(handle.GracefulStopFailure, outcome.GracefulStopFailureReason);
        Assert.Equal(1, handle.GracefulStopRequests);
        Assert.Equal(["graceful-stop", "kill", "wait"], handle.Calls);
    }

    [Fact]
    public async Task A_zero_grace_period_kills_without_waiting()
    {
        var handle = new FakeProcessHandle();

        var outcome = await ProcessTerminator.TerminateAsync(handle, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.Killed, outcome.Mode);
        Assert.Equal(["graceful-stop", "kill", "wait"], handle.Calls);
    }

    [Fact]
    public async Task An_already_finished_process_is_left_alone()
    {
        var handle = new FakeProcessHandle { HasExited = true };

        var outcome = await ProcessTerminator.TerminateAsync(handle, Grace, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.Exited, outcome.Mode);
        Assert.Empty(handle.Calls);
    }

    [Fact]
    public async Task A_process_that_exits_between_the_signal_and_the_kill_is_not_killed()
    {
        // The race is normal: a slow SIGINT handler can finish just after the grace expired.
        var handle = new LateExitingHandle();

        var outcome = await ProcessTerminator.TerminateAsync(handle, Grace, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.StoppedBySignal, outcome.Mode);
        Assert.True(outcome.GracefulStopRequested);
        Assert.False(handle.WasKilled);
    }

    [Fact]
    public async Task A_process_that_ends_by_itself_without_a_signal_is_not_credited_to_the_signal()
    {
        // Windows again: no signal could be delivered, and the child happened to finish on its
        // own before the kill. Reporting StoppedBySignal here would claim the CLI was given the
        // chance to flush its crew output, which it never was.
        var handle = new FakeProcessHandle { GracefulStopSucceeds = false, ExitsAfterFailedSignal = true };

        var outcome = await ProcessTerminator.TerminateAsync(handle, Grace, TestContext.Current.CancellationToken);

        Assert.Equal(ProcessTerminationMode.ExitedWithoutSignal, outcome.Mode);
        Assert.False(outcome.GracefulStopRequested);
        Assert.Equal(handle.GracefulStopFailure, outcome.GracefulStopFailureReason);
        Assert.False(handle.WasKilled);
    }

    [Fact]
    public void The_reported_description_keeps_the_reason_the_signal_could_not_be_sent()
    {
        var outcome = new ProcessTerminationOutcome
        {
            Mode = ProcessTerminationMode.ExitedWithoutSignal,
            GracefulStopFailureReason = "no per-child Ctrl+C on this platform",
        };

        var result = ProcessRunResult.FromCancellation(0, outcome, TimeSpan.FromSeconds(1));

        Assert.Equal(ProcessTerminationMode.ExitedWithoutSignal, result.Termination);
        Assert.Equal("no per-child Ctrl+C on this platform", result.GracefulStopFailureReason);
        Assert.Contains("no per-child Ctrl+C on this platform", result.Description, StringComparison.Ordinal);
    }

    /// <summary>Reports "still running" during the grace wait, then exits just after it.</summary>
    private sealed class LateExitingHandle : IProcessHandle
    {
        private bool _waited;

        public bool WasKilled { get; private set; }

        public int ProcessId => 1234;

        public bool HasExited => _waited;

        public bool TryRequestGracefulStop(out string? failureReason)
        {
            failureReason = null;
            return true;
        }

        public void Kill() => WasKilled = true;

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var exitedDuringWait = _waited;
            _waited = true;
            return Task.FromResult(exitedDuringWait);
        }
    }
}
