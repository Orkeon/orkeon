using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.ConsoleApp.Replay;
using Orkeon.Cli.Abstractions.Tests.Doubles;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary>
/// What a replay line waits for: no background work, unless a question waits; a background task launched by
/// a tracked line that fails is that line's failure; one approval is claimed by one reader.
/// </summary>
public sealed class ReplayIdleGateTests : IDisposable
{
    // A hang guard, never reached when the test passes: no test bounds a duration.
    private static readonly TimeSpan HangGuard = TimeSpan.FromMinutes(2);

    private readonly CommandInstanceRegistry _registry = new();
    private readonly FakeToolApprovalWatch _approvals = new();
    private readonly ReplayIdleGate _gate;
    private bool _otherQuestion;

    public ReplayIdleGateTests()
    {
        _gate = new ReplayIdleGate(() => _registry, () => _approvals, () => _otherQuestion);
        _gate.Attach();
    }

    public void Dispose() => _gate.Dispose();

    private CommandInstance StartBackgroundTask(string name = "dev-batches")
        => _registry.Register(name, CommandInstanceKind.Async, "dev", "run the batches", Guid.NewGuid());

    private static CommandResponse Done(string name)
        => new(name, name, success: true, payload: "ok", error: null);

    [Fact]
    public async Task ShouldOpenAtOnce_WhenNothingRuns()
    {
        await _gate.WaitAsync(TestContext.Current.CancellationToken).WaitAsync(HangGuard, TestContext.Current.CancellationToken);

        Assert.False(_gate.WorkRunning);
    }

    [Fact]
    public async Task ShouldOpen_WhenTheRunningTaskEnds()
    {
        var ct = TestContext.Current.CancellationToken;
        var task = StartBackgroundTask();

        var wait = _gate.WaitAsync(ct);
        Assert.False(wait.IsCompleted);
        task.Complete(Done("dev-batches"));

        await wait.WaitAsync(HangGuard, ct);
        Assert.Null(_gate.TakeFailure());
    }

    [Fact]
    public async Task ShouldOpen_WhenAnotherQuestionWaits()
    {
        StartBackgroundTask();
        _otherQuestion = true;

        await _gate.WaitAsync(TestContext.Current.CancellationToken).WaitAsync(HangGuard, TestContext.Current.CancellationToken);

        Assert.True(_gate.QuestionPending);
        Assert.True(_gate.OtherQuestionPending);
    }

    [Fact]
    public async Task ShouldChargeAFailedTask_ToTheLineThatLaunchedIt()
    {
        var ct = TestContext.Current.CancellationToken;
        _gate.TrackingLine = 3;
        var task = StartBackgroundTask();
        _gate.TrackingLine = 4;

        var wait = _gate.WaitAsync(ct);
        task.Fail("the build broke");
        await wait.WaitAsync(HangGuard, ct);

        Assert.True(_gate.HasFailure);
        Assert.Equal(new ReplayFailure(3, "[t1] dev-batches failed: the build broke"), _gate.TakeFailure());
        Assert.Null(_gate.TakeFailure());
    }

    [Fact]
    public void ShouldIgnoreATask_LaunchedWhileNoLineIsTracked()
    {
        var task = StartBackgroundTask();

        task.Fail("the build broke");

        Assert.False(_gate.HasFailure);
    }

    [Fact]
    public async Task ShouldGiveTheLoopTheClaim_WhenAnApprovalWaits()
    {
        StartBackgroundTask();
        _approvals.Ask("write_file");

        await _gate.WaitAsync(TestContext.Current.CancellationToken).WaitAsync(HangGuard, TestContext.Current.CancellationToken);

        Assert.Equal(ReplayReader.Loop, _gate.Claim);
        Assert.Equal("write_file", _gate.PendingApprovalTool);
    }

    [Fact]
    public void ShouldGiveACommandTheClaim_WhenTheLoopDoesNotHoldIt()
    {
        _approvals.Ask("write_file");

        Assert.True(_gate.WaitForCommandRead(TestContext.Current.CancellationToken));

        Assert.Equal(ReplayReader.Command, _gate.Claim);
    }

    [Fact]
    public void ShouldLetACommandRead_WhenNoApprovalWaits()
    {
        Assert.True(_gate.WaitForCommandRead(TestContext.Current.CancellationToken));

        Assert.Null(_gate.Claim);
    }

    [Fact]
    public async Task ShouldEndTheClaim_WhenTheApprovalsChange()
    {
        StartBackgroundTask();
        _approvals.Ask("write_file");
        await _gate.WaitAsync(TestContext.Current.CancellationToken).WaitAsync(HangGuard, TestContext.Current.CancellationToken);

        _approvals.Pending.Clear();
        _approvals.NotifyPendingChanged();

        Assert.Null(_gate.Claim);
        Assert.False(_gate.ApprovalPending);
    }

    [Fact]
    public async Task ShouldKeepTheCommandWaiting_UntilTheLoopReleasesItsClaim()
    {
        var ct = TestContext.Current.CancellationToken;
        StartBackgroundTask();
        _approvals.Ask("write_file");
        await _gate.WaitAsync(ct).WaitAsync(HangGuard, ct);

        var commandRead = Task.Run(() => _gate.WaitForCommandRead(ct), ct);
        Assert.True(SpinWait.SpinUntil(() => _gate.CommandReadsWaiting == 1, HangGuard));
        _gate.Release(ReplayReader.Loop);

        // The approval still waits: the command takes the claim and the line.
        Assert.True(await commandRead.WaitAsync(HangGuard, ct));
        Assert.Equal(ReplayReader.Command, _gate.Claim);
    }

    [Fact]
    public void ShouldReleaseOnlyTheReadersOwnClaim()
    {
        _approvals.Ask("write_file");
        _gate.WaitForCommandRead(TestContext.Current.CancellationToken);

        _gate.Release(ReplayReader.Loop);

        Assert.Equal(ReplayReader.Command, _gate.Claim);
    }

    [Fact]
    public async Task ShouldStopTheCommandsWait_WhenCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        StartBackgroundTask();
        _approvals.Ask("write_file");
        await _gate.WaitAsync(ct).WaitAsync(HangGuard, ct);
        await cts.CancelAsync();

        // The loop holds the claim: the command waits, and its cancelled token ends the wait without a line.
        Assert.False(_gate.WaitForCommandRead(cts.Token));
        Assert.Equal(ReplayReader.Loop, _gate.Claim);
    }

    [Fact]
    public async Task ShouldForgetTheFailureAndTheClaim_OnReset()
    {
        var ct = TestContext.Current.CancellationToken;
        _gate.TrackingLine = 1;
        var task = StartBackgroundTask();
        task.Fail("the build broke");
        _approvals.Ask("write_file");
        await _gate.WaitAsync(ct).WaitAsync(HangGuard, ct);

        _gate.Reset();

        Assert.False(_gate.HasFailure);
        Assert.Null(_gate.Claim);
        Assert.Equal(0, _gate.TrackingLine);
    }
}
