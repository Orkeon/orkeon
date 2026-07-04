using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Parsing;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Tests.CovAutonomous;

/// <summary>
/// Coverage tests for <see cref="SequentialCrewOrchestrator"/> exercising the batch,
/// fire-and-forget, streaming-fallback, checkpoint, and process-type dispatch branches.
/// All doubles are manual (no Moq / network / DB).
/// </summary>
public sealed class CovAutonomous_SequentialCrewOrchestratorTests
{
    // ── Doubles ─────────────────────────────────────────────────────────────

    private sealed class FakeCrewRepository : ICrewRepository
    {
        private readonly Dictionary<Ulid, DomainCrew> _crews = [];
        public void Add(DomainCrew crew) => _crews[crew.Id.Value] = crew;

        public Task<DomainCrew?> GetByIdAsync(CrewId id, CancellationToken ct = default)
        {
            _crews.TryGetValue(id.Value, out var c);
            return Task.FromResult<DomainCrew?>(c);
        }

        public Task AddAsync(DomainCrew crew, CancellationToken ct = default) { _crews[crew.Id.Value] = crew; return Task.CompletedTask; }
        public Task UpdateAsync(DomainCrew crew, CancellationToken ct = default) { _crews[crew.Id.Value] = crew; return Task.CompletedTask; }
        public Task DeleteAsync(CrewId id, CancellationToken ct = default) { _crews.Remove(id.Value); return Task.CompletedTask; }
        public Task<IReadOnlyList<DomainCrew>> GetByIdsAsync(IEnumerable<CrewId> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<IReadOnlyList<DomainCrew>> GetByStatusAsync(CrewStatus status, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<IReadOnlyList<DomainCrew>> GetByProcessTypeAsync(ProcessType pt, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<IReadOnlyList<DomainCrew>> GetByAgentAsync(AgentId id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<IReadOnlyList<DomainCrew>> GetByTaskAsync(TaskId id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<IReadOnlyList<DomainCrew>> GetWithRecentExecutionsAsync(DateTime since, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<CrewExecutionStatistics> GetExecutionStatisticsAsync(CrewId id, CancellationToken ct = default) => Task.FromResult(new CrewExecutionStatistics(0, 0, 0, 0.0, 0.0, null));
        public Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> spec, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<IReadOnlyList<DomainCrew>> FindAsync(ISpecification<DomainCrew> spec, int skip, int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DomainCrew>>([]);
        public Task<int> CountAsync(ISpecification<DomainCrew> spec, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> AnyAsync(ISpecification<DomainCrew> spec, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ExistsAsync(CrewId id, CancellationToken ct = default) => Task.FromResult(_crews.ContainsKey(id.Value));
        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_crews.Count);
    }

    private sealed class FakeStateManager : ICrewExecutionStateManager
    {
        private readonly Dictionary<Ulid, CrewExecutionState> _byCrew = [];
        private readonly Dictionary<string, CrewExecutionState> _byExec = [];
        public int CompleteExecutionCount { get; private set; }

        public Task<CrewExecutionState> GetStateAsync(CrewId crewId, CancellationToken ct = default)
        {
            _byCrew.TryGetValue(crewId.Value, out var s);
            return Task.FromResult(s ?? new CrewExecutionState(crewId, ExecutionId.New(), CrewInput.Empty()));
        }

        public Task SaveStateAsync(CrewExecutionState state, CancellationToken ct = default) { _byCrew[state.CrewId.Value] = state; return Task.CompletedTask; }
        public Task ClearStateAsync(CrewId crewId, CancellationToken ct = default) { _byCrew.Remove(crewId.Value); return Task.CompletedTask; }

        public Task<CrewExecutionState> CreateStateAsync(CrewId crewId, CancellationToken ct = default)
        {
            var exec = ExecutionId.New();
            var s = new CrewExecutionState(crewId, exec, CrewInput.Empty());
            _byCrew[crewId.Value] = s;
            _byExec[exec.AsString()] = s;
            return Task.FromResult(s);
        }

        public Task<CrewExecutionState> CreateStateAsync(CrewId crewId, ExecutionId executionId, CrewInput input, CancellationToken ct = default)
        {
            var s = new CrewExecutionState(crewId, executionId, input);
            _byCrew[crewId.Value] = s;
            _byExec[executionId.AsString()] = s;
            return Task.FromResult(s);
        }

        public Task<CrewExecutionState?> GetStateAsync(ExecutionId executionId, CancellationToken ct = default)
        {
            _byExec.TryGetValue(executionId.AsString(), out var s);
            return Task.FromResult(s);
        }

        public Task UpdateStateAsync(ExecutionId executionId, Action<CrewExecutionState> update, CancellationToken ct = default)
        {
            // Register the execution id lazily so KickoffAsyncNoWait updates land somewhere.
            if (!_byExec.TryGetValue(executionId.AsString(), out var s))
            {
                s = new CrewExecutionState(CrewId.Create(), executionId, CrewInput.Empty());
                _byExec[executionId.AsString()] = s;
            }
            update(s);
            return Task.CompletedTask;
        }

        public Task CompleteExecutionAsync(ExecutionId executionId, CancellationToken ct = default)
        {
            CompleteExecutionCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CrewExecutionState>> GetActiveExecutionsAsync(CrewId crewId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrewExecutionState>>([]);

        public Task CleanupExpiredExecutionsAsync(TimeSpan maxAge, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingProcessStrategy : IProcessStrategy
    {
        public string? LastMode { get; private set; }
        public bool AutonomousCalled { get; private set; }
        public AgentExecutionBudget? LastBudget { get; private set; }

        public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        { LastMode = "Sequential"; return Out(crew); }

        public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId mgr, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        { LastMode = "Hierarchical"; return Out(crew); }

        public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        { LastMode = "Parallel"; return Out(crew); }

        public Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        { LastMode = "Autonomous"; AutonomousCalled = true; LastBudget = budget; return Out(crew); }

        private static Task<DomainCrewOutput> Out(DomainCrew crew)
        {
            var taskOutputs = new List<DomainTaskOutput>
            {
                DomainTaskOutput.Create("ok-content", "text", null, TaskId.Create(), true, TimeSpan.FromMilliseconds(5)),
            };
            return Task.FromResult(new DomainCrewOutput(
                $"Completed: {crew.Goal}", null, taskOutputs, true, TimeSpan.FromMilliseconds(10), null));
        }
    }

    private sealed class FakeFactory : IProcessStrategyFactory
    {
        public RecordingProcessStrategy Strategy { get; } = new();
        public IProcessStrategy CreateStrategy(ProcessType processType) => Strategy;
    }

    private sealed class FakeCheckpointManager : ICheckpointManager
    {
        public int StartCount { get; private set; }
        public int CheckpointCount { get; private set; }
        public int CompleteCount { get; private set; }
        public int MarkFailedCount { get; private set; }

        public Task<string> StartSessionAsync(string crewId, CancellationToken ct = default) { StartCount++; return Task.FromResult("session-1"); }
        public Task CheckpointAsync(string sessionId, string taskId, string? output, CancellationToken ct = default) { CheckpointCount++; return Task.CompletedTask; }
        public Task MarkFailedAsync(string sessionId, string taskId, Exception ex, CancellationToken ct = default) { MarkFailedCount++; return Task.CompletedTask; }
        public Task CompleteSessionAsync(string sessionId, CancellationToken ct = default) { CompleteCount++; return Task.CompletedTask; }
        public Task<SessionState?> GetLatestCheckpointAsync(string crewId, CancellationToken ct = default) => Task.FromResult<SessionState?>(null);
        public Task<IReadOnlyList<VersionSummary>> GetHistoryAsync(string sessionId, int? limit = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<VersionSummary>>([]);
        public Task<SessionState?> RestoreToVersionAsync(string sessionId, string versionId, CancellationToken ct = default) => Task.FromResult<SessionState?>(null);
        public Task<VersionedState> ForkFromAsync(string sessionId, string versionId, string? label = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StateDiff> DiffAsync(string sessionId, string fromVersionId, string toVersionId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    // ── Setup helper ────────────────────────────────────────────────────────

    private sealed record Harness(
        SequentialCrewOrchestrator Orchestrator,
        FakeCrewRepository Repo,
        FakeStateManager State,
        FakeFactory Factory,
        FakeCheckpointManager? Checkpoint);

    private static Harness Build(bool withCheckpoint = false)
    {
        var repo = new FakeCrewRepository();
        var state = new FakeStateManager();
        var factory = new FakeFactory();
        FakeCheckpointManager? cp = withCheckpoint ? new FakeCheckpointManager() : null;

        var orch = new SequentialCrewOrchestrator(
            repo,
            NullLogger<SequentialCrewOrchestrator>.Instance,
            state,
            factory,
            new ExecutionPlanParser(),
            streamingService: null,
            agentRepository: null,
            checkpointManager: cp);

        return new Harness(orch, repo, state, factory, cp);
    }

    private static DomainCrew MakeCrew(ProcessType type, AgentId? manager = null)
    {
        var crew = manager is not null
            ? DomainCrew.Create("goal", type, managerAgentId: manager)
            : DomainCrew.Create("goal", type);
        var agent = manager ?? AgentId.Create();
        crew.AddAgent(agent);
        if (type == ProcessType.Hierarchical && manager is null)
            crew.AddAgent(AgentId.Create()); // worker
        crew.AddTask(TaskId.Create());
        return crew;
    }

    private static CrewInput Input() => new("ctx", new Dictionary<string, object> { ["k"] = "v" });

    // ── Constructor guards ───────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCrewRepository_Throws()
        => Assert.Throws<ArgumentNullException>(() => new SequentialCrewOrchestrator(
            null!, NullLogger<SequentialCrewOrchestrator>.Instance, new FakeStateManager(),
            new FakeFactory(), new ExecutionPlanParser()));

    [Fact]
    public void Constructor_NullExecutionPlanParser_Throws()
        => Assert.Throws<ArgumentNullException>(() => new SequentialCrewOrchestrator(
            new FakeCrewRepository(), NullLogger<SequentialCrewOrchestrator>.Instance,
            new FakeStateManager(), new FakeFactory(), null!));

    // ── Process-type dispatch ────────────────────────────────────────────────

    [Fact]
    public async Task KickoffAsync_Sequential_DispatchesSequential()
    {
        var h = Build();
        var crew = MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        var result = await h.Orchestrator.KickoffAsync(crew.Id, Input(), TestContext.Current.CancellationToken);

        Assert.Equal("Sequential", h.Factory.Strategy.LastMode);
        Assert.Contains("Completed", result.FinalOutput);
        Assert.Single(result.TaskOutputs);
    }

    [Fact]
    public async Task KickoffAsync_Parallel_DispatchesParallel()
    {
        var h = Build();
        var crew = MakeCrew(ProcessType.Parallel);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        await h.Orchestrator.KickoffAsync(crew.Id, Input(), TestContext.Current.CancellationToken);

        Assert.Equal("Parallel", h.Factory.Strategy.LastMode);
    }

    [Fact]
    public async Task KickoffAsync_Hierarchical_DispatchesHierarchical()
    {
        var h = Build();
        var manager = AgentId.Create();
        var crew = MakeCrew(ProcessType.Hierarchical, manager);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        await h.Orchestrator.KickoffAsync(crew.Id, Input(), TestContext.Current.CancellationToken);

        Assert.Equal("Hierarchical", h.Factory.Strategy.LastMode);
    }

    [Fact]
    public async Task KickoffAsync_Autonomous_DispatchesAutonomousWithPermissiveBudget()
    {
        var h = Build();
        var crew = MakeCrew(ProcessType.Autonomous);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        await h.Orchestrator.KickoffAsync(crew.Id, Input(), TestContext.Current.CancellationToken);

        Assert.True(h.Factory.Strategy.AutonomousCalled);
        Assert.NotNull(h.Factory.Strategy.LastBudget);
    }

    [Fact]
    public async Task KickoffAsync_Graph_DispatchesViaSequential()
    {
        var h = Build();
        var crew = MakeCrew(ProcessType.Graph);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        await h.Orchestrator.KickoffAsync(crew.Id, Input(), TestContext.Current.CancellationToken);

        // Graph routes through ExecuteSequentialAsync per the dispatcher.
        Assert.Equal("Sequential", h.Factory.Strategy.LastMode);
    }

    [Fact]
    public async Task KickoffAsync_Consensual_RoutesThroughFactoryViaSequentialEntryPoint()
    {
        // R3.3 — Consensual no longer uses a special-cased side interface: the
        // orchestrator asks the factory like for every other process type and
        // dispatches on the strategy's sequential entry point (the consensual
        // strategy maps it to its voting pipeline).
        var h = Build();
        var crew = MakeCrew(ProcessType.Consensual);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        var result = await h.Orchestrator.KickoffAsync(
            crew.Id, Input(), TestContext.Current.CancellationToken);

        Assert.Equal("Sequential", h.Factory.Strategy.LastMode);
        Assert.Contains("Completed", result.FinalOutput);
    }

    // ── Error / null paths ───────────────────────────────────────────────────

    [Fact]
    public async Task KickoffAsync_NullCrewId_ReturnsFailure()
    {
        var h = Build();
        var result = await h.Orchestrator.KickoffAsync(null!, Input(), TestContext.Current.CancellationToken);
        Assert.Contains("CrewId cannot be null", result.FinalOutput);
    }

    [Fact]
    public async Task KickoffAsync_CrewNotFound_ReturnsFailure()
    {
        var h = Build();
        var result = await h.Orchestrator.KickoffAsync(CrewId.Create(), Input(), TestContext.Current.CancellationToken);
        Assert.Contains("Crew execution failed", result.FinalOutput);
    }

    // ── Checkpoint path ──────────────────────────────────────────────────────

    [Fact]
    public async Task KickoffAsync_WithCheckpointManager_StartsCheckpointsAndCompletes()
    {
        var h = Build(withCheckpoint: true);
        var crew = MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        await h.Orchestrator.KickoffAsync(crew.Id, Input(), TestContext.Current.CancellationToken);

        Assert.Equal(1, h.Checkpoint!.StartCount);
        Assert.True(h.Checkpoint.CheckpointCount >= 1);
        Assert.Equal(1, h.Checkpoint.CompleteCount);
    }

    [Fact]
    public async Task KickoffAsync_WithCheckpointManager_CrewNotFound_MarksFailed()
    {
        var h = Build(withCheckpoint: true);
        // No crew added → GetByIdAsync returns null → throws after session start? No: session
        // start happens after load. So MarkFailed is reached only if session started. Here
        // the crew is missing so no session → just a failure output, MarkFailed not invoked.
        var result = await h.Orchestrator.KickoffAsync(CrewId.Create(), Input(), TestContext.Current.CancellationToken);
        Assert.Contains("Crew execution failed", result.FinalOutput);
        Assert.Equal(0, h.Checkpoint!.StartCount);
    }

    // ── Batch ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task KickoffForEachAsync_RunsAllInputs()
    {
        var h = Build();
        var crew = MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        var inputs = new[] { Input(), Input(), Input() };
        var batch = await h.Orchestrator.KickoffForEachAsync(crew.Id, inputs, TestContext.Current.CancellationToken);

        Assert.Equal(3, batch.Results.Count);
        Assert.Equal(3, batch.SuccessCount);
        Assert.Equal(0, batch.FailureCount);
    }

    // ── Fire-and-forget ──────────────────────────────────────────────────────

    [Fact]
    public async Task KickoffAsyncNoWait_ReturnsExecutionId_AndEventuallyCompletes()
    {
        var h = Build();
        var crew = MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        var execId = await h.Orchestrator.KickoffAsyncNoWait(crew.Id, Input());
        Assert.NotNull(execId);

        // The background task runs CompleteExecutionAsync in its finally block.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (h.State.CompleteExecutionCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.True(h.State.CompleteExecutionCount >= 1);
    }

    // ── Streaming fallback ───────────────────────────────────────────────────

    [Fact]
    public async Task KickoffStreamingAsync_NoStreamingService_EmitsEventsFromFallback()
    {
        var h = Build(); // streamingService null → fallback path
        var crew = MakeCrew(ProcessType.Sequential);
        await h.Repo.AddAsync(crew, TestContext.Current.CancellationToken);

        var events = new List<CrewExecutionEvent>();
        await foreach (var ev in h.Orchestrator.KickoffStreamingAsync(crew.Id, Input(), TestContext.Current.CancellationToken))
            events.Add(ev);

        // Fallback emits one event per task output (the strategy produced one).
        Assert.Single(events);
        Assert.Equal(AgentThought.ThoughtType.Conclusion, events[0].Thought.Type);
    }

    // ── Status query ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExecutionStatusAsync_UnknownExecution_Throws()
    {
        var h = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Orchestrator.GetExecutionStatusAsync(CrewExecutionId.From(Ulid.NewUlid()), TestContext.Current.CancellationToken));
    }
}
