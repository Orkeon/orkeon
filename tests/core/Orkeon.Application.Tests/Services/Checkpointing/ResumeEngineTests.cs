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
        var sessionId = await _manager.StartSessionAsync(Orkeon.Application.Interfaces.Services.ExecutionId.New(), CrewId1, TestContext.Current.CancellationToken);
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
        var sessionId = await _manager.StartSessionAsync(Orkeon.Application.Interfaces.Services.ExecutionId.New(), CrewId1, TestContext.Current.CancellationToken);
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
        var sessionId = await _manager.StartSessionAsync(Orkeon.Application.Interfaces.Services.ExecutionId.New(), CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId2, "output-2", TestContext.Current.CancellationToken);

        // Act
        // The session is still Running (a killed process leaves it so): it is taken over by its id.
        var result = await _engine.ResumeAsync(CrewId1, new ResumeOptions { ExecutionId = sessionId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(2, result.CompletedTaskIds.Count);
        Assert.Contains(TaskId1, result.CompletedTaskIds);
        Assert.Contains(TaskId2, result.CompletedTaskIds);
        Assert.Equal("output-1", result.CompletedOutputs[TaskId1]);
        Assert.Equal("output-2", result.CompletedOutputs[TaskId2]);
        Assert.Equal(0, result.SkipCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resume_WithSkipFailed_SkipsFailedTasks()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(Orkeon.Application.Interfaces.Services.ExecutionId.New(), CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(sessionId, TaskId2, new Exception("fail"), TestContext.Current.CancellationToken);

        var options = new ResumeOptions { SkipFailed = true };

        // Act
        var result = await _engine.ResumeAsync(CrewId1, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.CompletedTaskIds);
        Assert.Equal(1, result.SkipCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resume_WithRetryFailed_IncludesFailedForRetry()
    {
        // Arrange
        var sessionId = await _manager.StartSessionAsync(Orkeon.Application.Interfaces.Services.ExecutionId.New(), CrewId1, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(sessionId, TaskId1, "output-1", TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(sessionId, TaskId2, new Exception("fail"), TestContext.Current.CancellationToken);

        var options = new ResumeOptions { RetryFailed = true, MaxRetries = 3 };

        // Act
        var result = await _engine.ResumeAsync(CrewId1, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.CompletedTaskIds);
        Assert.Equal(0, result.SkipCount);
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
    }

    private static string Crew => "crew@/crew/crew.yaml";

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task Resume_WithAnExecutionId_ResumesThatSessionAndNotTheLatest()
    {
        var older = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(older, Crew, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(older.ToString(), "a#1", "older-a", TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Delay(5, TestContext.Current.CancellationToken);
        var newer = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(newer, Crew, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(newer.ToString(), "a#1", "newer-a", TestContext.Current.CancellationToken);

        var result = await _engine.ResumeAsync(Crew, new ResumeOptions { ExecutionId = older.ToString() }, TestContext.Current.CancellationToken);

        Assert.Equal(older.ToString(), result.SessionId);
        Assert.Equal("older-a", result.CompletedOutputs["a#1"]);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task Resume_WithAnExecutionIdOfAnotherCrew_ResumesNothing()
    {
        var id = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(id, "other@/crew/other.yaml", TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(id.ToString(), "a#1", "x", TestContext.Current.CancellationToken);

        var result = await _engine.ResumeAsync(Crew, new ResumeOptions { ExecutionId = id.ToString() }, TestContext.Current.CancellationToken);

        Assert.Equal("", result.SessionId);
        Assert.Empty(result.CompletedOutputs);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task Resume_Latest_SkipsANewerCompletedRun_ToTheOlderFailedOne()
    {
        var failed = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(failed, Crew, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(failed.ToString(), "a#1", "kept", TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(failed.ToString(), "b#1", new InvalidOperationException("boom"), TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Delay(5, TestContext.Current.CancellationToken);
        var completed = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(completed, Crew, TestContext.Current.CancellationToken);
        await _manager.CompleteSessionAsync(completed.ToString(), TestContext.Current.CancellationToken);

        var result = await _engine.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);

        Assert.Equal(failed.ToString(), result.SessionId);
        Assert.Equal("kept", result.CompletedOutputs["a#1"]);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task Resume_Latest_RefusesARunStillMarkedRunning_AndSaysHowToTakeItOver()
    {
        var failed = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(failed, Crew, TestContext.Current.CancellationToken);
        await _manager.MarkFailedAsync(failed.ToString(), "b#1", new InvalidOperationException("boom"), TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Delay(5, TestContext.Current.CancellationToken);
        var running = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(running, Crew, TestContext.Current.CancellationToken);

        var latest = await _engine.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);
        var byId = await _engine.ResumeAsync(Crew, new ResumeOptions { ExecutionId = running.ToString() }, TestContext.Current.CancellationToken);

        Assert.Equal("", latest.SessionId);
        Assert.Contains(running.ToString(), latest.Refusal, StringComparison.Ordinal);
        Assert.Contains("Running", latest.Refusal, StringComparison.Ordinal);
        Assert.Contains($"--resume {running}", latest.Refusal, StringComparison.Ordinal);
        Assert.Equal(running.ToString(), byId.SessionId);
        Assert.Null(byId.Refusal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task Resume_OfACompletedSession_ResumesNothing()
    {
        var id = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(id, Crew, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(id.ToString(), "a#1", "x", TestContext.Current.CancellationToken);
        await _manager.CompleteSessionAsync(id.ToString(), TestContext.Current.CancellationToken);

        var byId = await _engine.ResumeAsync(Crew, new ResumeOptions { ExecutionId = id.ToString() }, TestContext.Current.CancellationToken);
        var latest = await _engine.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);

        Assert.Equal("", byId.SessionId);
        Assert.Equal("", latest.SessionId);
    }
}
