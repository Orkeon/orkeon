using Orkeon.Application.Interfaces.Checkpointing;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Checkpointing.TimeTravel;

/// <summary>
/// Abstract test base for time-travel IStateStore capabilities.
/// Each concrete subclass provides a specific store implementation.
/// </summary>
public abstract class TimeTravelTestBase : IAsyncLifetime
{
    /// <summary>Creates the IStateStore implementation under test.</summary>
    protected abstract IStateStore CreateStore();

    /// <summary>Called after tests complete to clean up resources.</summary>
    protected virtual Task CleanupAsync() => Task.CompletedTask;

    private IStateStore _store = null!;

    public ValueTask InitializeAsync()
    {
        _store = CreateStore();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_store is IDisposable d)
            d.Dispose();
        if (_store is IAsyncDisposable ad)
            await ad.DisposeAsync();
        await CleanupAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public virtual async Task SaveVersioned_CreatesIncrementingVersions()
    {
        var state1 = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        var state2 = state1 with { CompletedTaskIndex = 1, UpdatedAt = DateTime.UtcNow };

        var v1 = await _store.SaveVersionedAsync(state1, stepId: StepId1, label: "first", TestContext.Current.CancellationToken);
        var v2 = await _store.SaveVersionedAsync(state2, stepId: "step-2", label: "second", TestContext.Current.CancellationToken);

        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);
        Assert.Equal(SessionId1, v1.SessionId);
        Assert.Equal(StepId1, v1.StepId);
        Assert.Equal("first", v1.Label);
        Assert.NotEqual(v1.VersionId, v2.VersionId);
    }

    [Fact]
    public virtual async Task GetHistory_ReturnsAllVersionsDescending()
    {
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);

        await _store.SaveVersionedAsync(state, stepId: StepId1, ct: TestContext.Current.CancellationToken);
        await _store.SaveVersionedAsync(state with { CompletedTaskIndex = 1 }, stepId: "step-2", ct: TestContext.Current.CancellationToken);
        await _store.SaveVersionedAsync(state with { CompletedTaskIndex = 2 }, stepId: "step-3", ct: TestContext.Current.CancellationToken);

        var history = await _store.GetHistoryAsync(SessionId1, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, history.Count);
        // Newest first
        Assert.Equal(3, history[0].Version);
        Assert.Equal(2, history[1].Version);
        Assert.Equal(1, history[2].Version);
    }

    [Fact]
    public virtual async Task GetHistory_WithLimit_ReturnsRequestedCount()
    {
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);

        await _store.SaveVersionedAsync(state, stepId: StepId1, ct: TestContext.Current.CancellationToken);
        await _store.SaveVersionedAsync(state with { CompletedTaskIndex = 1 }, stepId: "step-2", ct: TestContext.Current.CancellationToken);
        await _store.SaveVersionedAsync(state with { CompletedTaskIndex = 2 }, stepId: "step-3", ct: TestContext.Current.CancellationToken);

        var history = await _store.GetHistoryAsync(SessionId1, limit: 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, history.Count);
        Assert.Equal(3, history[0].Version);
        Assert.Equal(2, history[1].Version);
    }

    [Fact]
    public virtual async Task GetAtTimestamp_ReturnsCorrectVersion()
    {
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        var beforeAll = DateTime.UtcNow.AddMilliseconds(-50);

        var v1 = await _store.SaveVersionedAsync(state, stepId: StepId1, ct: TestContext.Current.CancellationToken);
        // Introduce a tiny delay so timestamps differ
        await Task.Delay(20, TestContext.Current.CancellationToken);
        var afterV1 = DateTime.UtcNow;
        await Task.Delay(20, TestContext.Current.CancellationToken);
        var v2 = await _store.SaveVersionedAsync(state with { CompletedTaskIndex = 1 }, stepId: "step-2", ct: TestContext.Current.CancellationToken);

        // Request at a time after v1 but before v2 was created
        var found = await _store.GetAtAsync(SessionId1, afterV1, TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(v1.VersionId, found.VersionId);
    }

    [Fact]
    public virtual async Task GetByStep_ReturnsVersionForStep()
    {
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);

        await _store.SaveVersionedAsync(state, stepId: StepId1, ct: TestContext.Current.CancellationToken);
        var v2 = await _store.SaveVersionedAsync(state with { CompletedTaskIndex = 1 }, stepId: "step-2", ct: TestContext.Current.CancellationToken);
        await _store.SaveVersionedAsync(state with { CompletedTaskIndex = 2 }, stepId: "step-3", ct: TestContext.Current.CancellationToken);

        var found = await _store.GetByStepAsync(SessionId1, "step-2", TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(v2.VersionId, found.VersionId);
        Assert.Equal("step-2", found.StepId);
    }

    [Fact]
    public virtual async Task Fork_CreatesNewSession_WithCopiedState()
    {
        var checkpoints = new Dictionary<string, TaskCheckpoint>
        {
            [TaskId1] = new TaskCheckpoint
            {
                TaskId = TaskId1,
                Status = CheckpointStatus.Completed,
                Output = "result-1"
            }
        };
        var state = new SessionState
        {
            SessionId = SessionId1,
            CrewId = CrewId1,
            Phase = SessionPhase.Running,
            CompletedTaskIndex = 1,
            TaskCheckpoints = checkpoints,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var v1 = await _store.SaveVersionedAsync(state, stepId: StepId1, ct: TestContext.Current.CancellationToken);

        var forked = await _store.ForkAsync(SessionId1, v1.VersionId, label: "my-fork", TestContext.Current.CancellationToken);

        Assert.NotEqual(SessionId1, forked.SessionId);
        Assert.Equal(1, forked.Version);
        Assert.Equal("my-fork", forked.Label);
        Assert.Equal(CrewId1, forked.State.CrewId);
        Assert.Equal(SessionPhase.Running, forked.State.Phase);
        Assert.True(forked.State.TaskCheckpoints.ContainsKey(TaskId1));
        Assert.Equal("result-1", forked.State.TaskCheckpoints[TaskId1].Output);
    }

    [Fact]
    public virtual async Task Fork_SetsForkedFromMetadata()
    {
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        var v1 = await _store.SaveVersionedAsync(state, stepId: StepId1, ct: TestContext.Current.CancellationToken);

        var forked = await _store.ForkAsync(SessionId1, v1.VersionId, ct: TestContext.Current.CancellationToken);

        Assert.Equal(SessionId1, forked.ForkedFromSession);
        Assert.Equal(v1.VersionId, forked.ForkedFromVersion);
    }

    [Fact]
    public virtual async Task Fork_OriginalSessionUnmodified()
    {
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        var v1 = await _store.SaveVersionedAsync(state, stepId: StepId1, ct: TestContext.Current.CancellationToken);

        await _store.ForkAsync(SessionId1, v1.VersionId, ct: TestContext.Current.CancellationToken);

        // Original session should still exist and be unchanged
        var original = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);
        Assert.NotNull(original);
        Assert.Equal(SessionId1, original.SessionId);
        Assert.Equal(CrewId1, original.CrewId);

        // Original version history should be unchanged
        var history = await _store.GetHistoryAsync(SessionId1, ct: TestContext.Current.CancellationToken);
        Assert.Single(history);
    }

    [Fact]
    public virtual async Task Diff_DetectsTaskChanges()
    {
        var state1 = new SessionState
        {
            SessionId = SessionId1,
            CrewId = CrewId1,
            Phase = SessionPhase.Running,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [TaskId1] = new TaskCheckpoint { TaskId = TaskId1, Status = CheckpointStatus.Pending }
            }
        };
        var v1 = await _store.SaveVersionedAsync(state1, stepId: StepId1, ct: TestContext.Current.CancellationToken);

        var state2 = state1 with
        {
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [TaskId1] = new TaskCheckpoint { TaskId = TaskId1, Status = CheckpointStatus.Completed, Output = "done" }
            }
        };
        var v2 = await _store.SaveVersionedAsync(state2, stepId: "step-2", ct: TestContext.Current.CancellationToken);

        var diff = await _store.DiffAsync(SessionId1, v1.VersionId, v2.VersionId, TestContext.Current.CancellationToken);

        Assert.Single(diff.TaskDiffs);
        Assert.Equal(TaskId1, diff.TaskDiffs[0].TaskId);
        Assert.Equal(CheckpointStatus.Pending, diff.TaskDiffs[0].FromStatus);
        Assert.Equal(CheckpointStatus.Completed, diff.TaskDiffs[0].ToStatus);
        Assert.True(diff.TaskDiffs[0].OutputChanged);
    }

    [Fact]
    public virtual async Task Diff_DetectsPhaseChange()
    {
        var state1 = CreateState(SessionId1, CrewId1, SessionPhase.Running);
        var v1 = await _store.SaveVersionedAsync(state1, stepId: StepId1, ct: TestContext.Current.CancellationToken);

        var state2 = state1 with { Phase = SessionPhase.Completed };
        var v2 = await _store.SaveVersionedAsync(state2, stepId: "step-2", ct: TestContext.Current.CancellationToken);

        var diff = await _store.DiffAsync(SessionId1, v1.VersionId, v2.VersionId, TestContext.Current.CancellationToken);

        Assert.True(diff.PhaseChanged);
        Assert.Equal(SessionPhase.Running, diff.FromPhase);
        Assert.Equal(SessionPhase.Completed, diff.ToPhase);
    }

    [Fact]
    public virtual async Task Diff_DetectsNewTasks()
    {
        var state1 = new SessionState
        {
            SessionId = SessionId1,
            CrewId = CrewId1,
            Phase = SessionPhase.Running,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [TaskId1] = new TaskCheckpoint { TaskId = TaskId1, Status = CheckpointStatus.Completed }
            }
        };
        var v1 = await _store.SaveVersionedAsync(state1, stepId: StepId1, ct: TestContext.Current.CancellationToken);

        var state2 = state1 with
        {
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [TaskId1] = new TaskCheckpoint { TaskId = TaskId1, Status = CheckpointStatus.Completed },
                [TaskId2] = new TaskCheckpoint { TaskId = TaskId2, Status = CheckpointStatus.Pending }
            }
        };
        var v2 = await _store.SaveVersionedAsync(state2, stepId: "step-2", ct: TestContext.Current.CancellationToken);

        var diff = await _store.DiffAsync(SessionId1, v1.VersionId, v2.VersionId, TestContext.Current.CancellationToken);

        Assert.Contains(TaskId2, diff.AddedTaskIds);
        Assert.Empty(diff.RemovedTaskIds);
    }

    [Fact]
    public virtual async Task ResumeFromVersion_RestoresCorrectState()
    {
        // Save multiple versions with different task states
        var state1 = new SessionState
        {
            SessionId = SessionId1,
            CrewId = CrewId1,
            Phase = SessionPhase.Running,
            CompletedTaskIndex = 1,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [TaskId1] = new TaskCheckpoint { TaskId = TaskId1, Status = CheckpointStatus.Completed, Output = "v1-output" }
            }
        };
        var v1 = await _store.SaveVersionedAsync(state1, stepId: StepId1, ct: TestContext.Current.CancellationToken);

        var state2 = state1 with
        {
            CompletedTaskIndex = 2,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [TaskId1] = new TaskCheckpoint { TaskId = TaskId1, Status = CheckpointStatus.Completed, Output = "v1-output" },
                [TaskId2] = new TaskCheckpoint { TaskId = TaskId2, Status = CheckpointStatus.Completed, Output = "v2-output" }
            }
        };
        await _store.SaveVersionedAsync(state2, stepId: "step-2", ct: TestContext.Current.CancellationToken);

        // Retrieve version 1 and verify we get the correct state
        var restored = await _store.GetVersionAsync(SessionId1, v1.VersionId, TestContext.Current.CancellationToken);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.Version);
        Assert.Equal(1, restored.State.CompletedTaskIndex);
        Assert.Single(restored.State.TaskCheckpoints);
        Assert.Equal("v1-output", restored.State.TaskCheckpoints[TaskId1].Output);
    }

    [Fact]
    public virtual async Task Retrocompat_SaveAsync_StillWorks()
    {
        // Verify the original SaveAsync/GetAsync still works
        var state = CreateState(SessionId1, CrewId1, SessionPhase.Running);

        await _store.SaveAsync(state, TestContext.Current.CancellationToken);
        var retrieved = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Equal(SessionId1, retrieved.SessionId);
        Assert.Equal(CrewId1, retrieved.CrewId);
        Assert.Equal(SessionPhase.Running, retrieved.Phase);

        // Update via SaveAsync
        var updated = state with { Phase = SessionPhase.Completed };
        await _store.SaveAsync(updated, TestContext.Current.CancellationToken);

        var retrievedUpdated = await _store.GetAsync(SessionId1, TestContext.Current.CancellationToken);
        Assert.NotNull(retrievedUpdated);
        Assert.Equal(SessionPhase.Completed, retrievedUpdated.Phase);
    }

    /// <summary>Creates a minimal session state for testing.</summary>
    protected static SessionState CreateState(
        string sessionId, string crewId, SessionPhase phase,
        DateTime? updatedAt = null) =>
        new()
        {
            SessionId = sessionId,
            CrewId = crewId,
            Phase = phase,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
}
