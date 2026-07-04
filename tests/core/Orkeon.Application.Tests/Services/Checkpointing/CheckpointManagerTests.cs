using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;
using Microsoft.Extensions.Logging.Abstractions;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Application.Tests.Services.Checkpointing;

public class CheckpointManagerTests
{
    private readonly InMemoryStateStore _store;
    private readonly CheckpointManager _manager;

    public CheckpointManagerTests()
    {
        _store = new InMemoryStateStore();
        var logger = NullLogger<CheckpointManager>.Instance;
        _manager = new CheckpointManager(_store, logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task StartSession_CreatesNewState()
    {
        // Act
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(sessionId);
        Assert.NotEmpty(sessionId);

        var state = await _store.GetAsync(sessionId, TestContext.Current.CancellationToken);
        Assert.NotNull(state);
        Assert.Equal(CrewId1, state.CrewId);
        Assert.Equal(SessionPhase.Running, state.Phase);
        Assert.Equal(0, state.CompletedTaskIndex);
        Assert.Empty(state.TaskCheckpoints);
    }

    [Fact]
    public async System.Threading.Tasks.Task Checkpoint_UpdatesTaskToCompleted()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);

        // Act
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);

        // Assert
        var state = await _store.GetAsync(sessionId, TestContext.Current.CancellationToken);
        Assert.NotNull(state);
        Assert.Equal(1, state.CompletedTaskIndex);
        Assert.True(state.TaskCheckpoints.ContainsKey(TaskId1));
        Assert.Equal(CheckpointStatus.Completed, state.TaskCheckpoints[TaskId1].Status);
        Assert.Equal("output-1", state.TaskCheckpoints[TaskId1].Output);
    }

    [Fact]
    public async System.Threading.Tasks.Task MarkFailed_RecordsErrorAndIncrementsRetry()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        var ex = new InvalidOperationException("Something went wrong");

        // Act
        await _manager.MarkFailedAsync(sessionId, TaskId1, ex, TestContext.Current.CancellationToken);

        // Assert
        var state = await _store.GetAsync(sessionId, TestContext.Current.CancellationToken);
        Assert.NotNull(state);
        Assert.Equal(SessionPhase.Failed, state.Phase);
        Assert.True(state.TaskCheckpoints.ContainsKey(TaskId1));

        var checkpoint = state.TaskCheckpoints[TaskId1];
        Assert.Equal(CheckpointStatus.Failed, checkpoint.Status);
        Assert.Equal("Something went wrong", checkpoint.ErrorMessage);
        Assert.Equal(1, checkpoint.RetryCount);
        Assert.Equal("Something went wrong", state.ErrorMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task MarkFailed_IncreasesRetryCountOnSubsequentFailures()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);

        // Act
        await _manager.MarkFailedAsync(sessionId, TaskId1, new Exception("Error 1"), TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(sessionId, TaskId1, new Exception("Error 2"), TestContext.Current.CancellationToken);

        // Assert
        var state = await _store.GetAsync(sessionId, TestContext.Current.CancellationToken);
        Assert.NotNull(state);
        var checkpoint = state.TaskCheckpoints[TaskId1];
        Assert.Equal(2, checkpoint.RetryCount);
        Assert.Equal("Error 2", checkpoint.ErrorMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task CompleteSession_SetsPhaseToCompleted()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);

        // Act
        await _manager.CompleteSessionAsync(sessionId, TestContext.Current.CancellationToken);

        // Assert
        var state = await _store.GetAsync(sessionId, TestContext.Current.CancellationToken);
        Assert.NotNull(state);
        Assert.Equal(SessionPhase.Completed, state.Phase);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetLatestCheckpoint_ReturnsMostRecent()
    {
        // Arrange
        var session1 = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(session1, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.CompleteSessionAsync(session1, TestContext.Current.CancellationToken);

        var session2 = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(session2, TaskId2, "output-2", TestContext.Current.CancellationToken);

        // Act
        var latest = await _manager.GetLatestCheckpointAsync(CrewId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(session2, latest.SessionId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Checkpoint_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _manager.CheckpointAsync("non-existent", TaskId1, "output-1", TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async System.Threading.Tasks.Task CompleteSession_NonExistentSession_DoesNotThrow()
    {
        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _manager.CompleteSessionAsync("non-existent", TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }
}
