using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Application.Tests.Services.Checkpointing;

/// <summary>
/// FWK-07: starting the session of a run that already has one continues it. A resumed run keeps its
/// execution id, so its session is the one it left, with the checkpoints it had written.
/// </summary>
public sealed class CheckpointManagerSessionContinuationTests
{
    private readonly InMemoryStateStore _store = new();
    private readonly CheckpointManager _manager;

    public CheckpointManagerSessionContinuationTests() =>
        _manager = new CheckpointManager(_store, NullLogger<CheckpointManager>.Instance);

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldKeepTheCheckpoints_WhenTheSessionIsStartedAgain()
    {
        var id = ExecutionId.New();
        await _manager.StartSessionAsync(id, "crew", TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(id.ToString(), "a#1", "done-a", TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(id.ToString(), "b#1", new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

        await _manager.StartSessionAsync(id, "crew", TestContext.Current.CancellationToken);

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.Contains("a#1", state!.TaskCheckpoints.Keys);
        Assert.Equal("done-a", state.TaskCheckpoints["a#1"].Output);
        Assert.Equal(SessionPhase.Running, state.Phase);
        Assert.Null(state.ErrorMessage);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldHoldNoLockForASessionThatIsOver()
    {
        var id = ExecutionId.New();
        await _manager.StartSessionAsync(id, "crew", TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(id.ToString(), "a#1", "x", TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(id.ToString(), "b#1", new InvalidOperationException("boom"), TestContext.Current.CancellationToken);
        await _manager.CompleteSessionAsync(id.ToString(), TestContext.Current.CancellationToken);

        Assert.Equal(0, _manager.LockedSessions);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldNotLoseACheckpoint_WhenTasksCheckpointAtTheSameTime()
    {
        var slow = new FakeSlowReadStateStore();
        var manager = new CheckpointManager(slow, NullLogger<CheckpointManager>.Instance);
        var id = ExecutionId.New();
        await manager.StartSessionAsync(id, "crew", TestContext.Current.CancellationToken);

        await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 40).Select(n =>
            System.Threading.Tasks.Task.Run(() =>
                manager.CheckpointAsync(id.ToString(), $"t{n}#1", $"o{n}", TestContext.Current.CancellationToken))));

        var state = await slow.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(40, state!.TaskCheckpoints.Count);
    }
}
