using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;
using Microsoft.Extensions.Logging.Abstractions;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Application.Tests.Services.Checkpointing;

public class ResumeEngineTests
{
    private readonly InMemoryStateStore _store;
    private readonly CheckpointManager _manager;
    private readonly ResumeEngine _engine;

    public ResumeEngineTests()
    {
        _store = new InMemoryStateStore();
        _manager = new CheckpointManager(_store, NullLogger<CheckpointManager>.Instance);
        _engine = new ResumeEngine(_manager, _store, NullLogger<ResumeEngine>.Instance);
    }

    [Fact]
    public async System.Threading.Tasks.Task CanResume_ReturnsTrue_ForIncompleteSession()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        // Session is still Running (not Completed)

        // Act
        var canResume = await _engine.CanResumeAsync(CrewId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(canResume);
    }

    [Fact]
    public async System.Threading.Tasks.Task CanResume_ReturnsFalse_WhenNoSession()
    {
        // Act
        var canResume = await _engine.CanResumeAsync("non-existent-crew", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(canResume);
    }

    [Fact]
    public async System.Threading.Tasks.Task CanResume_ReturnsFalse_WhenSessionCompleted()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.CompleteSessionAsync(sessionId, TestContext.Current.CancellationToken);

        // Act
        var canResume = await _engine.CanResumeAsync(CrewId1, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(canResume);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resume_ReturnsCorrectSkipCountAndOutputs()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId2, "output-2", TestContext.Current.CancellationToken);

        // Act
        var result = await _engine.ResumeAsync(CrewId1, ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(2, result.CompletedTaskIds.Count);
        Assert.Contains(TaskId1, result.CompletedTaskIds);
        Assert.Contains(TaskId2, result.CompletedTaskIds);
        Assert.Equal("output-1", result.CompletedOutputs[TaskId1]);
        Assert.Equal("output-2", result.CompletedOutputs[TaskId2]);
        Assert.Equal(2, result.ResumeFromIndex);
        Assert.Equal(0, result.SkipCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resume_WithSkipFailed_SkipsFailedTasks()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(sessionId, TaskId2, new Exception("fail"), TestContext.Current.CancellationToken);

        var options = new ResumeOptions { SkipFailed = true };

        // Act
        var result = await _engine.ResumeAsync(CrewId1, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.CompletedTaskIds);
        Assert.Equal(1, result.SkipCount);
        Assert.Equal(2, result.ResumeFromIndex);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resume_WithRetryFailed_IncludesFailedForRetry()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(sessionId, TaskId2, new Exception("fail"), TestContext.Current.CancellationToken);

        var options = new ResumeOptions { RetryFailed = true, MaxRetries = 3 };

        // Act
        var result = await _engine.ResumeAsync(CrewId1, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.CompletedTaskIds);
        Assert.Equal(0, result.SkipCount);
        // ResumeFromIndex should not advance past the failed task that will be retried
        Assert.Equal(1, result.ResumeFromIndex);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resume_WhenNoCheckpoint_ReturnsEmptyResult()
    {
        // Act
        var result = await _engine.ResumeAsync("non-existent-crew", ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("", result.SessionId);
        Assert.Empty(result.CompletedTaskIds);
        Assert.Empty(result.CompletedOutputs);
        Assert.Equal(0, result.ResumeFromIndex);
    }
}
