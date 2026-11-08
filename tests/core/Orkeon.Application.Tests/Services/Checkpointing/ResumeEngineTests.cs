#pragma warning disable ORKEXP007 // The run history is experimental.
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Tests.Doubles;
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
        await _manager.FailSessionAsync(sessionId, new Exception("fail"), TestContext.Current.CancellationToken);

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
        await _manager.FailSessionAsync(sessionId, new Exception("fail"), TestContext.Current.CancellationToken);

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

    // Orders two sessions by an explicit timestamp, never by a wait: the session is moved into the past.
    private async System.Threading.Tasks.Task AgeAsync(Orkeon.Application.Interfaces.Services.ExecutionId id)
    {
        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        await _store.SaveAsync(state! with { UpdatedAt = DateTime.UtcNow.AddHours(-1) }, TestContext.Current.CancellationToken);
    }

    private ResumeEngine EngineWith(MockRunHistoryReader history) =>
        new(_manager, _store, NullLogger<ResumeEngine>.Instance, history, new StubRunProcessProbe { Alive = history.ProcessAlive });

    private async System.Threading.Tasks.Task<Orkeon.Application.Interfaces.Services.ExecutionId> WaitingSessionAsync()
    {
        var id = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(id, Crew, TestContext.Current.CancellationToken);
        await _manager.AskAsync(id.ToString(), new PendingInputRequest { RequestId = "a#1.1", Kind = PendingInputKind.Text, Prompt = "?", TaskKey = "a#1" }, TestContext.Current.CancellationToken);
        return id;
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task Resume_WithAnExecutionId_ResumesThatSessionAndNotTheLatest()
    {
        var older = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(older, Crew, TestContext.Current.CancellationToken);
        await _manager.CheckpointAsync(older.ToString(), "a#1", "older-a", TestContext.Current.CancellationToken);
        await AgeAsync(older);
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
        await _manager.FailSessionAsync(failed.ToString(), new InvalidOperationException("boom"), TestContext.Current.CancellationToken);
        await AgeAsync(failed);
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
        await _manager.FailSessionAsync(failed.ToString(), new InvalidOperationException("boom"), TestContext.Current.CancellationToken);
        await AgeAsync(failed);
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

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task Resume_Latest_RefusesAWaitingSessionHeldByALiveProcess_AndByIdToo()
    {
        var id = await WaitingSessionAsync();
        var history = new MockRunHistoryReader { Summary = MockRunHistoryReader.Running(id.ToString()), ProcessAlive = true };
        var engine = EngineWith(history);

        var latest = await engine.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);
        var byId = await engine.ResumeAsync(Crew, new ResumeOptions { ExecutionId = id.ToString() }, TestContext.Current.CancellationToken);

        Assert.Equal("", latest.SessionId);
        Assert.Contains(id.ToString(), latest.Refusal, StringComparison.Ordinal);
        Assert.Equal("", byId.SessionId);
        Assert.NotNull(byId.Refusal);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task Resume_Latest_TakesAWaitingSessionWhoseProcessIsDead_OrWhoseRunEnded()
    {
        var id = await WaitingSessionAsync();
        var dead = EngineWith(new MockRunHistoryReader { Summary = MockRunHistoryReader.Running(id.ToString()), ProcessAlive = false });
        var ended = EngineWith(new MockRunHistoryReader { Summary = MockRunHistoryReader.Running(id.ToString()) with { Status = Orkeon.Application.Runs.RunStatus.WaitingForInput }, ProcessAlive = true });

        var afterDeath = await dead.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);
        var afterExit = await ended.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);

        Assert.Equal(id.ToString(), afterDeath.SessionId);
        Assert.Equal(id.ToString(), afterExit.SessionId);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task Resume_Latest_TakesAWaitingSession_WhenTheHistoryHoldsNoRecordOfIt()
    {
        // A run that stopped on a question and exited leaves a session and, with the history disabled, nothing else.
        var id = await WaitingSessionAsync();

        var latest = await _engine.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);

        Assert.Equal(id.ToString(), latest.SessionId);
        Assert.Null(latest.Refusal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task Resume_RefusesAFailedSessionWhoseRunIsStillAlive_ByLatestAndById()
    {
        // A session marked Failed while its process still runs (a task failed, the others go on): never taken over.
        var id = Orkeon.Application.Interfaces.Services.ExecutionId.New();
        await _manager.StartSessionAsync(id, Crew, TestContext.Current.CancellationToken);
        await _manager.FailSessionAsync(id.ToString(), new InvalidOperationException("boom"), TestContext.Current.CancellationToken);
        var engine = EngineWith(new MockRunHistoryReader { Summary = MockRunHistoryReader.Running(id.ToString()), ProcessAlive = true });

        var latest = await engine.ResumeAsync(Crew, ct: TestContext.Current.CancellationToken);
        var byId = await engine.ResumeAsync(Crew, new ResumeOptions { ExecutionId = id.ToString() }, TestContext.Current.CancellationToken);

        Assert.Equal("", latest.SessionId);
        Assert.NotNull(latest.Refusal);
        Assert.Equal("", byId.SessionId);
        Assert.NotNull(byId.Refusal);
    }
}
