using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Checkpointing.E2E;

/// <summary>
/// End-to-end tests for time-travel checkpointing workflows.
/// Uses InMemoryStateStore for fast, deterministic testing.
/// </summary>
public class TimeTravelE2ETests
{
    [Fact]
    public async Task Workflow_CheckpointAtEachStep_ThenTimeTravel()
    {
        // Arrange: simulate a crew workflow with 4 tasks
        var store = new InMemoryStateStore();
        var sessionId = "session-e2e-1";
        var crewId = "crew-e2e-1";
        var now = DateTime.UtcNow;

        var state = new SessionState
        {
            SessionId = sessionId,
            CrewId = crewId,
            Phase = SessionPhase.Running,
            CompletedTaskIndex = 0,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(),
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveAsync(state, TestContext.Current.CancellationToken);

        // Act: simulate completing 4 tasks, saving a versioned checkpoint after each
        var versions = new List<VersionedState>();
        for (int i = 1; i <= 4; i++)
        {
            var taskId = $"task-{i}";
            var checkpoints = new Dictionary<string, TaskCheckpoint>(state.TaskCheckpoints)
            {
                [taskId] = new TaskCheckpoint
                {
                    TaskId = taskId,
                    Status = CheckpointStatus.Completed,
                    Output = $"result-{i}",
                    Timestamp = DateTime.UtcNow
                }
            };

            state = state with
            {
                CompletedTaskIndex = i,
                TaskCheckpoints = checkpoints,
                UpdatedAt = DateTime.UtcNow
            };

            var v = await store.SaveVersionedAsync(state, stepId: taskId, label: $"After task {i}", TestContext.Current.CancellationToken);
            versions.Add(v);
        }

        // Verify: full history with 4 versions
        var history = await store.GetHistoryAsync(sessionId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(4, history.Count);
        Assert.Equal(4, history[0].Version); // newest first
        Assert.Equal(1, history[3].Version);

        // Time-travel to version 2 (after task-2)
        var v2 = await store.GetVersionAsync(sessionId, versions[1].VersionId, TestContext.Current.CancellationToken);
        Assert.NotNull(v2);
        Assert.Equal(2, v2.State.CompletedTaskIndex);
        Assert.Equal(2, v2.State.TaskCheckpoints.Count);
        Assert.True(v2.State.TaskCheckpoints.ContainsKey(TaskId1));
        Assert.True(v2.State.TaskCheckpoints.ContainsKey(TaskId2));
        Assert.False(v2.State.TaskCheckpoints.ContainsKey(TaskId3));

        // Diff between v1 and v4
        var diff = await store.DiffAsync(sessionId, versions[0].VersionId, versions[3].VersionId, TestContext.Current.CancellationToken);
        Assert.Equal(3, diff.AddedTaskIds.Count); // task-2, task-3, task-4 were added
        Assert.Empty(diff.RemovedTaskIds);

        // GetByStep
        var step3Version = await store.GetByStepAsync(sessionId, TaskId3, TestContext.Current.CancellationToken);
        Assert.NotNull(step3Version);
        Assert.Equal(3, step3Version.Version);
    }

    [Fact]
    public async Task Workflow_FailsAtStep3_ForkAndRetry()
    {
        // Arrange
        var store = new InMemoryStateStore();
        var sessionId = "session-fork-1";
        var crewId = "crew-fork-1";
        var now = DateTime.UtcNow;

        var state = new SessionState
        {
            SessionId = sessionId,
            CrewId = crewId,
            Phase = SessionPhase.Running,
            CompletedTaskIndex = 0,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(),
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveAsync(state, TestContext.Current.CancellationToken);

        // Complete tasks 1 and 2
        for (int i = 1; i <= 2; i++)
        {
            var taskId = $"task-{i}";
            state = state with
            {
                CompletedTaskIndex = i,
                TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(state.TaskCheckpoints)
                {
                    [taskId] = new TaskCheckpoint
                    {
                        TaskId = taskId,
                        Status = CheckpointStatus.Completed,
                        Output = $"result-{i}"
                    }
                },
                UpdatedAt = DateTime.UtcNow
            };
            await store.SaveVersionedAsync(state, stepId: taskId, ct: TestContext.Current.CancellationToken);
        }

        // Task 3 fails
        state = state with
        {
            Phase = SessionPhase.Failed,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(state.TaskCheckpoints)
            {
                [TaskId3] = new TaskCheckpoint
                {
                    TaskId = TaskId3,
                    Status = CheckpointStatus.Failed,
                    ErrorMessage = "Something went wrong"
                }
            },
            ErrorMessage = "Something went wrong",
            UpdatedAt = DateTime.UtcNow
        };
        await store.SaveVersionedAsync(state, stepId: TaskId3, label: "Failed at task 3", TestContext.Current.CancellationToken);

        // Fork from version 2 (before the failure)
        var history = await store.GetHistoryAsync(sessionId, ct: TestContext.Current.CancellationToken);
        var v2Summary = history.FirstOrDefault(h => h.Version == 2);
        Assert.NotNull(v2Summary);

        var forked = await store.ForkAsync(sessionId, v2Summary.VersionId, label: "Retry from v2", TestContext.Current.CancellationToken);

        // Verify the forked session
        Assert.NotEqual(sessionId, forked.SessionId);
        Assert.Equal(1, forked.Version);
        Assert.Equal("Retry from v2", forked.Label);
        Assert.Equal(sessionId, forked.ForkedFromSession);
        Assert.Equal(v2Summary.VersionId, forked.ForkedFromVersion);

        // Forked state should have tasks 1 and 2 completed, no task 3
        Assert.Equal(SessionPhase.Running, forked.State.Phase);
        Assert.Equal(2, forked.State.CompletedTaskIndex);
        Assert.Equal(2, forked.State.TaskCheckpoints.Count);
        Assert.False(forked.State.TaskCheckpoints.ContainsKey(TaskId3));

        // Complete task 3 in the forked session
        var forkedState = forked.State with
        {
            CompletedTaskIndex = 3,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(forked.State.TaskCheckpoints)
            {
                [TaskId3] = new TaskCheckpoint
                {
                    TaskId = TaskId3,
                    Status = CheckpointStatus.Completed,
                    Output = "retry-result-3"
                }
            },
            UpdatedAt = DateTime.UtcNow
        };
        await store.SaveVersionedAsync(forkedState, stepId: TaskId3, ct: TestContext.Current.CancellationToken);

        // Verify the forked session has 2 versions now
        var forkedHistory = await store.GetHistoryAsync(forked.SessionId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, forkedHistory.Count);

        // Original session is unmodified
        var originalHistory = await store.GetHistoryAsync(sessionId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, originalHistory.Count);
    }

    [Fact]
    public async Task ConcurrentAccess_NoConcurrencyIssues()
    {
        // Arrange
        var store = new InMemoryStateStore();
        var concurrentSessions = 10;
        var versionsPerSession = 20;

        // Act: create many sessions concurrently, each saving multiple versions
        var tasks = Enumerable.Range(0, concurrentSessions).Select(async i =>
        {
            var sessionId = $"concurrent-{i}";
            var state = new SessionState
            {
                SessionId = sessionId,
                CrewId = "crew-concurrent",
                Phase = SessionPhase.Running,
                CompletedTaskIndex = 0,
                TaskCheckpoints = new Dictionary<string, TaskCheckpoint>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await store.SaveAsync(state);

            for (int v = 1; v <= versionsPerSession; v++)
            {
                state = state with
                {
                    CompletedTaskIndex = v,
                    UpdatedAt = DateTime.UtcNow
                };
                await store.SaveVersionedAsync(state, stepId: $"step-{v}");
            }

            return sessionId;
        });

        var sessionIds = await Task.WhenAll(tasks);

        // Assert: each session should have exactly versionsPerSession versions
        foreach (var sessionId in sessionIds)
        {
            var history = await store.GetHistoryAsync(sessionId, ct: TestContext.Current.CancellationToken);
            Assert.Equal(versionsPerSession, history.Count);
            Assert.Equal(versionsPerSession, history[0].Version); // newest first
            Assert.Equal(1, history[^1].Version);
        }

        // All sessions should be listed
        var allSessions = await store.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(concurrentSessions, allSessions.Count);
    }
}
