using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Durable persistence and crash-recovery tests for
/// <see cref="ScopedCrewExecutionStateManager"/> (R3.8).
/// A "crash" is simulated by disposing the manager and creating a brand new instance
/// over the same <c>IStateStore</c>: any state surviving the recreation necessarily
/// came from the store, not from memory.
/// </summary>
public sealed class ScopedCrewExecutionStateManagerPersistenceTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
        _disposables.Clear();
    }

    private ScopedCrewExecutionStateManager CreateManager(
        Orkeon.Application.Interfaces.Checkpointing.IStateStore? store,
        CrewExecutionStatePersistenceOptions? options)
    {
        var manager = new ScopedCrewExecutionStateManager(
            new StubStateStoreScopeFactory(store),
            NullLogger<ScopedCrewExecutionStateManager>.Instance,
            options == null ? null : Options.Create(options));
        _disposables.Add(manager);
        return manager;
    }

    private ScopedCrewExecutionStateManager CreatePersistentManager(
        Orkeon.Application.Interfaces.Checkpointing.IStateStore store)
        => CreateManager(store, new CrewExecutionStatePersistenceOptions { Enabled = true });

    private static CrewInput CreateInput()
        => new("Persisted context", new Dictionary<string, object> { ["topic"] = "resilience" });

    // ── Persistance + rechargement (DoD R3.8) ───────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ShouldReloadPersistedState_WhenManagerIsRecreated_InMemoryStore()
    {
        // Arrange — the store outlives the manager (simulated crash + restart)
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();

        var managerBeforeCrash = CreatePersistentManager(store);
        await managerBeforeCrash.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);
        await managerBeforeCrash.UpdateStateAsync(executionId, s =>
        {
            s.Status = ExecutionState.Running;
            s.Progress = 0.4;
            s.CurrentTask = "task-2";
        }, TestContext.Current.CancellationToken);
        managerBeforeCrash.Dispose();

        // Act — brand new manager, same store
        var managerAfterRestart = CreatePersistentManager(store);
        var reloaded = await managerAfterRestart.GetStateAsync(executionId, TestContext.Current.CancellationToken);

        // Assert — the state was rehydrated from the store, not from memory
        Assert.NotNull(reloaded);
        Assert.Equal(executionId, reloaded.Id);
        Assert.Equal(crewId, reloaded.CrewId);
        Assert.Equal(ExecutionState.Running, reloaded.Status);
        Assert.Equal(0.4, reloaded.Progress);
        Assert.Equal("task-2", reloaded.CurrentTask);
        Assert.Equal("Persisted context", reloaded.Input.InitialContext);
        Assert.Equal("resilience", reloaded.Input.GetStringVariables()["topic"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReloadPersistedState_WhenManagerIsRecreated_SqliteStore()
    {
        // Arrange — SQLite store (single in-memory connection owned by the store,
        // which survives the manager recreation)
        using var store = new SqliteStateStore("Data Source=:memory:", new FakeFileSystemService());
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();

        var managerBeforeCrash = CreatePersistentManager(store);
        await managerBeforeCrash.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);
        await managerBeforeCrash.UpdateStateAsync(executionId, s =>
        {
            s.Status = ExecutionState.Running;
            s.Progress = 0.75;
        }, TestContext.Current.CancellationToken);
        managerBeforeCrash.Dispose();

        // Act
        var managerAfterRestart = CreatePersistentManager(store);
        var reloaded = await managerAfterRestart.GetStateAsync(executionId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(reloaded);
        Assert.Equal(ExecutionState.Running, reloaded.Status);
        Assert.Equal(0.75, reloaded.Progress);
        Assert.Equal(crewId, reloaded.CrewId);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPreserveTerminalStatusErrorAndOutput_WhenReloading()
    {
        // Arrange
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();

        var manager = CreatePersistentManager(store);
        await manager.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);
        await manager.UpdateStateAsync(executionId, s =>
        {
            s.Output = new CrewOutput(
                FinalOutput: "the answer",
                TaskOutputs:
                [
                    new Orkeon.Application.Execution.TaskOutput(
                        TaskId: "t-1",
                        AgentId: "agent-1",
                        Content: "partial",
                        CompletedAt: DateTime.UtcNow,
                        Success: true,
                        ExecutionTime: TimeSpan.FromSeconds(3))
                ],
                Duration: TimeSpan.FromSeconds(12),
                TokensUsed: new TokenUsage(10, 20, 30));
            s.Error = "boom";
            s.Status = ExecutionState.Failed;
        }, TestContext.Current.CancellationToken);
        manager.Dispose();

        // Act
        var managerAfterRestart = CreatePersistentManager(store);
        var reloaded = await managerAfterRestart.GetStateAsync(executionId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(reloaded);
        Assert.Equal(ExecutionState.Failed, reloaded.Status);
        Assert.Equal("boom", reloaded.Error);
        Assert.NotNull(reloaded.EndTime);
        Assert.NotNull(reloaded.Output);
        Assert.Equal("the answer", reloaded.Output.FinalOutput);
        var taskOutput = Assert.Single(reloaded.Output.TaskOutputs);
        Assert.Equal("t-1", taskOutput.TaskId);
        Assert.Equal("agent-1", taskOutput.AgentId);
        Assert.True(taskOutput.Success);
        Assert.NotNull(reloaded.Output.TokensUsed);
        Assert.Equal(30, reloaded.Output.TokensUsed.TotalTokens);
    }

    // ── Reprise (create-or-resume) ──────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ShouldResumePersistedState_WhenCreatingWithSameExecutionId()
    {
        // Arrange — execution interrupted mid-flight
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();

        var managerBeforeCrash = CreatePersistentManager(store);
        await managerBeforeCrash.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);
        await managerBeforeCrash.UpdateStateAsync(executionId, s =>
        {
            s.Status = ExecutionState.Running;
            s.Progress = 0.6;
        }, TestContext.Current.CancellationToken);
        managerBeforeCrash.Dispose();

        // Act — restarting the execution with the same resume identifier
        var managerAfterRestart = CreatePersistentManager(store);
        var resumed = await managerAfterRestart.CreateStateAsync(crewId, executionId, CrewInput.Empty(), TestContext.Current.CancellationToken);

        // Assert — the persisted state is resumed, not recreated from scratch
        Assert.Equal(executionId, resumed.Id);
        Assert.Equal(ExecutionState.Running, resumed.Status);
        Assert.Equal(0.6, resumed.Progress);
        Assert.Equal("Persisted context", resumed.Input.InitialContext);

        // And the resumed state is live: further updates work on it
        await managerAfterRestart.UpdateStateAsync(executionId, s => s.Progress = 0.7, TestContext.Current.CancellationToken);
        var after = await managerAfterRestart.GetStateAsync(executionId, TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal(0.7, after.Progress);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillThrow_WhenCreatingDuplicateLiveExecutionId()
    {
        // Arrange — persistence enabled; the state is alive in the same manager
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();
        var manager = CreatePersistentManager(store);
        await manager.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);

        // Act & Assert — live in-memory duplicates keep the historical contract
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldListPersistedActiveExecutions_AfterRestart()
    {
        // Arrange — two active executions and a completed one
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var running = ExecutionId.New();
        var pending = ExecutionId.New();
        var completed = ExecutionId.New();

        var managerBeforeCrash = CreatePersistentManager(store);
        await managerBeforeCrash.CreateStateAsync(crewId, running, CreateInput(), TestContext.Current.CancellationToken);
        await managerBeforeCrash.UpdateStateAsync(running, s => s.Status = ExecutionState.Running, TestContext.Current.CancellationToken);
        await managerBeforeCrash.CreateStateAsync(crewId, pending, CreateInput(), TestContext.Current.CancellationToken);
        await managerBeforeCrash.CreateStateAsync(crewId, completed, CreateInput(), TestContext.Current.CancellationToken);
        await managerBeforeCrash.UpdateStateAsync(completed, s => s.Status = ExecutionState.Completed, TestContext.Current.CancellationToken);
        managerBeforeCrash.Dispose();

        // Act
        var managerAfterRestart = CreatePersistentManager(store);
        var active = await managerAfterRestart.GetActiveExecutionsAsync(crewId, TestContext.Current.CancellationToken);

        // Assert — only non-terminal executions are listed for resumption
        Assert.Equal(2, active.Count);
        Assert.Contains(active, s => s.Id == running && s.Status == ExecutionState.Running);
        Assert.Contains(active, s => s.Id == pending && s.Status == ExecutionState.Pending);
        Assert.DoesNotContain(active, s => s.Id == completed);
    }

    // ── Défaut rétro-compatible ─────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotPersist_WhenPersistenceNotConfigured()
    {
        // Arrange — no options at all (historical constructor shape): even with a
        // store reachable through the scope, nothing must be persisted.
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();

        var manager = CreateManager(store, options: null);
        await manager.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);
        manager.Dispose();

        // Act
        var managerAfterRestart = CreateManager(store, options: null);
        var reloaded = await managerAfterRestart.GetStateAsync(executionId, TestContext.Current.CancellationToken);

        // Assert — in-memory only by default (no crash recovery): nothing survives
        Assert.Null(reloaded);
        var sessions = await store.ListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(sessions);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotPersist_WhenExplicitlyDisabled()
    {
        // Arrange
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();

        var manager = CreateManager(store, new CrewExecutionStatePersistenceOptions { Enabled = false });
        await manager.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);

        // Assert — the store is never touched
        var sessions = await store.ListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(sessions);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDegradeToInMemory_WhenEnabledWithoutStore()
    {
        // Arrange — persistence enabled but no IStateStore resolvable from the scope
        var crewId = CrewId.Create();
        var executionId = ExecutionId.New();

        var manager = CreateManager(store: null, new CrewExecutionStatePersistenceOptions { Enabled = true });

        // Act — in-memory operation keeps working
        await manager.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);
        await manager.UpdateStateAsync(executionId, s => s.Status = ExecutionState.Running, TestContext.Current.CancellationToken);
        var live = await manager.GetStateAsync(executionId, TestContext.Current.CancellationToken);
        manager.Dispose();

        var managerAfterRestart = CreateManager(store: null, new CrewExecutionStatePersistenceOptions { Enabled = true });
        var reloaded = await managerAfterRestart.GetStateAsync(executionId, TestContext.Current.CancellationToken);

        // Assert — graceful degradation: live state works, nothing survives restart
        Assert.NotNull(live);
        Assert.Equal(ExecutionState.Running, live.Status);
        Assert.Null(reloaded);
    }

    // ── Cohabitation avec le checkpointing de tâches ────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ShouldNamespaceSessions_SoTaskCheckpointsAreUntouched()
    {
        // Arrange — a task-level checkpoint session already lives in the shared store
        var store = new InMemoryStateStore();
        var crewId = CrewId.Create();
        var checkpointSession = new Orkeon.Application.Interfaces.Checkpointing.SessionState
        {
            SessionId = "session-from-checkpoint-manager",
            CrewId = crewId.AsString(),
            Phase = Orkeon.Application.Interfaces.Checkpointing.SessionPhase.Running
        };
        await store.SaveAsync(checkpointSession, TestContext.Current.CancellationToken);

        var manager = CreatePersistentManager(store);

        // Act
        var executionId = ExecutionId.New();
        await manager.CreateStateAsync(crewId, executionId, CreateInput(), TestContext.Current.CancellationToken);
        var active = await manager.GetActiveExecutionsAsync(crewId, TestContext.Current.CancellationToken);

        // Assert — execution-state sessions are namespaced: the checkpoint session is
        // neither rehydrated as an execution nor overwritten.
        var single = Assert.Single(active);
        Assert.Equal(executionId, single.Id);

        var untouched = await store.GetAsync("session-from-checkpoint-manager", TestContext.Current.CancellationToken);
        Assert.NotNull(untouched);
        Assert.Equal(crewId.AsString(), untouched.CrewId);
    }
}
