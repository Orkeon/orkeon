using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Memory;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Parsing;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Application.Interfaces.Services;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// Tests for SequentialCrewOrchestrator — validates that only async KickoffAsync is
/// available (sync Kickoff removed per AUDIT-P1-08) and behaves correctly.
/// </summary>
public class SequentialCrewOrchestratorTests
{
    #region Test Doubles

    private sealed class TestLogger : ILogger<SequentialCrewOrchestrator>
    {
        public List<string> Logs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Logs.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    private sealed class TestCrewRepository : ICrewRepository
    {
        private readonly Dictionary<Ulid, DomainCrew?> _crews = [];
        public bool ShouldThrowOnGet { get; set; }

        public void AddCrew(DomainCrew crew) => _crews[crew.Id.Value] = crew;

        public Task<DomainCrew?> GetByIdAsync(CrewId id, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowOnGet)
                throw new InvalidOperationException("Crew not found");
            _crews.TryGetValue(id.Value, out var crew);
            return Task.FromResult(crew);
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

    private sealed class TestStateManager : ICrewExecutionStateManager
    {
        private readonly Dictionary<Ulid, CrewExecutionState> _states = [];
        private readonly Dictionary<string, CrewExecutionState> _executionStates = [];

        public Task<CrewExecutionState> GetStateAsync(CrewId crewId, CancellationToken ct = default)
        {
            _states.TryGetValue(crewId.Value, out var state);
            return Task.FromResult(state ?? new CrewExecutionState(crewId, ExecutionId.New(), CrewInput.Empty()));
        }

        public Task SaveStateAsync(CrewExecutionState state, CancellationToken ct = default) { _states[state.CrewId.Value] = state; return Task.CompletedTask; }
        public Task ClearStateAsync(CrewId crewId, CancellationToken ct = default) { _states.Remove(crewId.Value); return Task.CompletedTask; }

        public Task<CrewExecutionState> CreateStateAsync(CrewId crewId, CancellationToken ct = default)
        {
            var state = new CrewExecutionState(crewId, ExecutionId.New(), CrewInput.Empty());
            _states[crewId.Value] = state;
            return Task.FromResult(state);
        }

        public Task<CrewExecutionState> CreateStateAsync(CrewId crewId, ExecutionId executionId, CrewInput input, CancellationToken ct = default)
        {
            var state = new CrewExecutionState(crewId, executionId, input);
            _states[crewId.Value] = state;
            _executionStates[executionId.AsString()] = state;
            return Task.FromResult(state);
        }

        public Task<CrewExecutionState?> GetStateAsync(ExecutionId executionId, CancellationToken ct = default)
        {
            _executionStates.TryGetValue(executionId.AsString(), out var state);
            return Task.FromResult(state);
        }

        public Task UpdateStateAsync(ExecutionId executionId, Action<CrewExecutionState> updateAction, CancellationToken ct = default)
        {
            if (_executionStates.TryGetValue(executionId.AsString(), out var state)) updateAction(state);
            return Task.CompletedTask;
        }

        public Task CompleteExecutionAsync(ExecutionId executionId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CrewExecutionState>> GetActiveExecutionsAsync(CrewId crewId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CrewExecutionState>>(_states.Values.Where(s => s.CrewId == crewId).ToList());

        public Task CleanupExpiredExecutionsAsync(TimeSpan maxAge, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestProcessStrategy : IProcessStrategy
    {
        public bool ShouldFail { get; set; }
        public IReadOnlyDictionary<string, string>? LastReceivedVariables { get; private set; }

        /// <summary>Runs at execution time, where an ambient context can be observed.</summary>
        public Action? OnExecute { get; set; }

        /// <summary>Async variant, to force a real suspension inside the strategy.</summary>
        public Func<System.Threading.Tasks.Task>? OnExecuteAsync { get; set; }

        /// <summary>When set, this output is returned verbatim instead of the default one.</summary>
        public DomainCrewOutput? ConfiguredOutput { get; set; }

        public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            return CreateOutput(crew);
        }

        public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId? managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            return CreateOutput(crew);
        }

        public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            return CreateOutput(crew);
        }

        public Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Autonomous execution is not covered by this test double.");

        private async Task<DomainCrewOutput> CreateOutput(DomainCrew crew)
        {
            OnExecute?.Invoke();
            if (OnExecuteAsync is not null)
                await OnExecuteAsync().ConfigureAwait(false);

            if (ShouldFail)
                throw new InvalidOperationException("Process strategy failed");

            if (ConfiguredOutput is not null)
                return ConfiguredOutput;

            return new DomainCrewOutput(
                $"Completed crew goal: {crew.Goal}",
                null,
                [],
                true,
                TimeSpan.FromMilliseconds(100),
                null);
        }
    }

    private sealed class TestProcessStrategyFactory : IProcessStrategyFactory
    {
        public TestProcessStrategy Strategy { get; } = new();
        public IProcessStrategy CreateStrategy(ProcessType processType) => Strategy;
    }

    #endregion

    #region Memory Provider Recording (P2-O-02)

    [Fact]
    public async Task KickoffAsync_ShouldRecordCrewMemoryProvider_AtKickoff()
    {
        // Arrange — a crew declaring a memory provider; the orchestrator must record it so the
        // memory subsystem can resolve it for this run.
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var registry = new CrewMemoryProviderRegistry();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            memoryProviderRegistry: registry);

        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "Test crew",
            ProcessType = ProcessType.Sequential,
            MemoryEnabled = true,
            MemoryProvider = "redis"
        });
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        // Act
        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("redis", registry.GetProvider(crew.Id));
        Assert.True(registry.IsMemoryEnabled(crew.Id));
    }

    [Fact]
    public async Task KickoffAsync_ShouldRecordTheCrewName_AsTheScopeOfItsMemory()
    {
        // GAP-20: the name travels with the type, so the crew reads its earlier runs' memory.
        var repository = new TestCrewRepository();
        var registry = new CrewMemoryProviderRegistry();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), new TestProcessStrategyFactory(),
            new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            memoryProviderRegistry: registry);

        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "Test crew",
            Name = "legal-watch",
            ProcessType = ProcessType.Sequential,
            MemoryEnabled = true,
            MemoryProvider = "sqlite"
        });
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.Equal("sqlite", registry.GetProvider(crew.Id));
        Assert.Equal("legal-watch", registry.GetScope(crew.Id));
    }

    [Fact]
    public async Task KickoffAsync_ShouldLeaveRegistryEmpty_WhenCrewDeclaresNoProvider()
    {
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var registry = new CrewMemoryProviderRegistry();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            memoryProviderRegistry: registry);

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.Null(registry.GetProvider(crew.Id));
        Assert.False(registry.IsMemoryEnabled(crew.Id));
    }

    [Fact]
    public async Task KickoffAsync_records_that_a_crew_without_memory_does_not_remember()
    {
        // GAP-30: memory: decides; a name alone stores nothing.
        var repository = new TestCrewRepository();
        var registry = new CrewMemoryProviderRegistry();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), new TestProcessStrategyFactory(),
            new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            memoryProviderRegistry: registry);
        var crew = DomainCrew.Create(new CrewCreateOptions { Goal = "Test crew", Name = "legal-watch", ProcessType = ProcessType.Sequential });
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.False(registry.IsMemoryEnabled(crew.Id));
        Assert.Equal("legal-watch", registry.GetScope(crew.Id));
    }

    [Fact]
    public async Task A_crew_whose_memory_cannot_work_fails_before_its_first_task_with_the_cause()
    {
        // GAP-30: no embedder, a refused key, an unreachable store — the run is refused before
        // anything is asked of a model, saying why.
        var repository = new TestCrewRepository();
        var strategyFactory = new TestProcessStrategyFactory();
        var executed = false;
        strategyFactory.Strategy.OnExecute = () => executed = true;
        var memory = new MockMemoryCoordinator
        {
            NotReady = new InvalidOperationException("Crew 'legal-watch' has memory: true, but its memory cannot be used: no embedding provider is registered."),
        };
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), strategyFactory,
            new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            memoryProviderRegistry: new CrewMemoryProviderRegistry(), memoryCoordinator: memory);
        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "Test crew", Name = "legal-watch", ProcessType = ProcessType.Sequential, MemoryEnabled = true,
        });
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        var output = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.False(output.Succeeded);
        Assert.Contains("no embedding provider is registered", output.Error, StringComparison.Ordinal);
        Assert.Equal(crew.Id, Assert.Single(memory.ReadinessChecks));
        Assert.False(executed);
    }

    [Fact]
    public async Task A_crew_without_memory_is_not_checked()
    {
        var repository = new TestCrewRepository();
        var memory = new MockMemoryCoordinator { NotReady = new InvalidOperationException("never asked") };
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), new TestProcessStrategyFactory(),
            new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            memoryProviderRegistry: new CrewMemoryProviderRegistry(), memoryCoordinator: memory);
        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        var output = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.True(output.Succeeded, output.Error);
        Assert.Empty(memory.ReadinessChecks);
    }

    #endregion

    #region KickoffStreamingAsync Tests (P2-O-03, GAP-32)

    private static async Task<List<CrewExecutionEvent>> StreamAsync(SequentialCrewOrchestrator orchestrator, DomainCrew crew)
    {
        var events = new List<CrewExecutionEvent>();
        await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(
            crew.Id, new CrewInput("ctx", new Dictionary<string, object> { ["topic"] = "markets" }), TestContext.Current.CancellationToken))
        {
            events.Add(executionEvent);
        }

        return events;
    }

    [Fact]
    public async Task KickoffStreamingAsync_runs_the_crews_strategy_and_ends_on_the_output_KickoffAsync_returns()
    {
        // GAP-32: the streamed kickoff is the real run — the strategy of the crew's mode, given the
        // run's variables — and its last event carries the run's CrewOutput. It used to run a fake
        // task per id (the GUID as its description) on agents taken in turn, and said no result.
        var repository = new TestCrewRepository();
        var strategies = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), strategies, new ExecutionPlanParser(), new RecordingDomainEventDispatcher());
        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        var events = await StreamAsync(orchestrator, crew);
        var streamedVariables = strategies.Strategy.LastReceivedVariables;
        var kicked = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object> { ["topic"] = "markets" }), TestContext.Current.CancellationToken);

        Assert.Equal("markets", streamedVariables?["topic"]);
        var finished = Assert.Single(events);
        Assert.Equal(Orkeon.Constants.Protocol.RunEventKinds.RunFinished, finished.Kind);
        Assert.NotNull(finished.Output);
        Assert.True(finished.Output.Succeeded);
        Assert.Equal(kicked.FinalOutput, finished.Output.FinalOutput);
    }

    [Fact]
    public async Task KickoffStreamingAsync_warns_of_nothing_it_does_what_KickoffAsync_does()
    {
        // The degraded fallback, its warning and the memory and planning warnings are gone with the
        // second path they described: there is one run.
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, new TestStateManager(), new TestProcessStrategyFactory(), new ExecutionPlanParser(), new RecordingDomainEventDispatcher());
        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        await StreamAsync(orchestrator, crew);

        Assert.DoesNotContain(logger.Logs, l => l.StartsWith("[Warning]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KickoffStreamingAsync_checks_the_crews_memory_before_its_first_task_like_KickoffAsync()
    {
        // GAP-30 + GAP-32: a streamed crew with memory: true is the real run — its memory is checked,
        // recalled and stored —, not a run that warns it forgets. A memory that cannot work refuses it
        // before its first task, and the stream says so before it finishes.
        var repository = new TestCrewRepository();
        var strategies = new TestProcessStrategyFactory();
        var executed = false;
        strategies.Strategy.OnExecute = () => executed = true;
        var memory = new MockMemoryCoordinator
        {
            NotReady = new InvalidOperationException("Crew 'legal-watch' has memory: true, but its memory cannot be used: no embedding provider is registered."),
        };
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), strategies, new ExecutionPlanParser(),
            new RecordingDomainEventDispatcher(), memoryProviderRegistry: new CrewMemoryProviderRegistry(), memoryCoordinator: memory);
        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "Test crew", Name = "legal-watch", ProcessType = ProcessType.Sequential, MemoryEnabled = true,
        });
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        var events = await StreamAsync(orchestrator, crew);

        Assert.Equal(crew.Id, Assert.Single(memory.ReadinessChecks));
        Assert.False(executed);
        Assert.Equal(
            [Orkeon.Constants.Protocol.RunEventKinds.Error, Orkeon.Constants.Protocol.RunEventKinds.RunFinished],
            events.Select(e => e.Kind));
        Assert.Equal(Orkeon.Constants.Protocol.RunEventErrorCodes.CrewFailed, events[0].Code);
        Assert.Contains("no embedding provider is registered", events[0].Message, StringComparison.Ordinal);
        Assert.False(events[1].Output!.Succeeded);
        Assert.Equal(events[0].Message, events[1].Output!.Error);
    }

    #endregion

    #region KickoffAsync Tests (AUDIT-P1-08)

    [Fact]
    public async Task KickoffAsync_ShouldCompleteSuccessfully_WhenCalledWithValidCrew()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        var input = new CrewInput("Test context", new Dictionary<string, object>());

        // Act
        var result = await orchestrator.KickoffAsync(crew.Id, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Completed crew goal", result.FinalOutput);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task KickoffAsync_ShouldPropagateException_WhenCrewNotFound()
    {
        // Arrange
        var repository = new TestCrewRepository();
        repository.ShouldThrowOnGet = true;
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher());

        var crewId = CrewId.Create();
        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var result = await orchestrator.KickoffAsync(crewId, input, TestContext.Current.CancellationToken);

        // Assert — the orchestrator catches exceptions and returns a failure output
        Assert.NotNull(result);
        Assert.Contains("Crew execution failed", result.FinalOutput);
        // The machine-readable half: KickoffAsync never throws, so Succeeded is the only way
        // a host can tell "the crew answered" from "the crew died and here is the apology".
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task KickoffAsync_ShouldReturnFailure_WhenNullCrewIdProvided()
    {
        // Arrange
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher());

        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var result = await orchestrator.KickoffAsync(null!, input, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("CrewId cannot be null", result.FinalOutput);
    }

    [Fact]
    public async Task KickoffAsync_ShouldPropagateRealTokenUsage_FromDomainMetadata()
    {
        // Arrange — strategy reports 700 total tokens via crew metadata
        var metadata = CrewMetadata.CreateBuilder().AddTotalTokens(700).Build();
        var domainOutput = DomainCrewOutput.CreateSuccess(
            output: "done",
            structuredOutput: null,
            taskOutputs: [],
            executionTime: TimeSpan.FromMilliseconds(50),
            metadata: metadata);

        var orchestrator = BuildOrchestrator(out var crew, out var strategyFactory);
        strategyFactory.Strategy.ConfiguredOutput = domainOutput;

        // Act
        var result = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert — token telemetry is no longer hardcoded to zero
        Assert.NotNull(result.TokensUsed);
        Assert.Equal(700, result.TokensUsed.TotalTokens);
    }

    [Fact]
    public async Task KickoffAsync_ShouldHonorPromptCompletionSplit_FromDomainMetadata()
    {
        // Arrange — strategy reports the provider-supplied prompt/completion split (R10.8)
        var metadata = CrewMetadata.CreateBuilder()
            .AddTotalTokens(900)
            .AddPromptTokens(600)
            .AddCompletionTokens(300)
            .Build();
        var domainOutput = DomainCrewOutput.CreateSuccess(
            output: "done",
            structuredOutput: null,
            taskOutputs: [],
            executionTime: TimeSpan.FromMilliseconds(50),
            metadata: metadata);

        var orchestrator = BuildOrchestrator(out var crew, out var strategyFactory);
        strategyFactory.Strategy.ConfiguredOutput = domainOutput;

        // Act
        var result = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert — split no longer frozen at 0 when the strategy propagated it
        Assert.NotNull(result.TokensUsed);
        Assert.Equal(600, result.TokensUsed.PromptTokens);
        Assert.Equal(300, result.TokensUsed.CompletionTokens);
        Assert.Equal(900, result.TokensUsed.TotalTokens);
    }

    [Fact]
    public async Task KickoffAsync_ShouldReportNullTokenUsage_WhenStrategyMeasuredNothing()
    {
        // Arrange — domain output without any token telemetry (e.g. custom strategy)
        var domainOutput = DomainCrewOutput.CreateSuccess(
            output: "done",
            structuredOutput: null,
            taskOutputs: [],
            executionTime: TimeSpan.FromMilliseconds(10),
            metadata: null);

        var orchestrator = BuildOrchestrator(out var crew, out var strategyFactory);
        strategyFactory.Strategy.ConfiguredOutput = domainOutput;

        // Act
        var result = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert — "not measured" stays null, never a fabricated TokenUsage(0,0,0)
        Assert.Null(result.TokensUsed);
    }

    [Fact]
    public async Task KickoffAsync_ShouldReportNullTokenUsage_WhenExecutionFails()
    {
        // Arrange — the strategy throws before any telemetry can be collected
        var orchestrator = BuildOrchestrator(out var crew, out var strategyFactory);
        strategyFactory.Strategy.ShouldFail = true;

        // Act
        var result = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert — failure output carries no fabricated zero usage
        Assert.Contains("Crew execution failed", result.FinalOutput);
        // The machine-readable half: KickoffAsync never throws, so Succeeded is the only way
        // a host can tell "the crew answered" from "the crew died and here is the apology".
        Assert.False(result.Succeeded);
        Assert.Null(result.TokensUsed);
    }

    [Fact]
    public async Task KickoffAsync_ShouldReportRealSuccessAndExecutionTimeAndAgentId_PerTask()
    {
        // Arrange — a failed task with a real agent id and execution time
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var failedTask = Orkeon.Domain.Task.ValueObjects.TaskOutput.Create(
            rawOutput: "task failed output",
            format: "text",
            taskId: taskId,
            success: false,
            executionTime: TimeSpan.FromSeconds(3),
            agentId: agentId.ToString());

        var domainOutput = DomainCrewOutput.CreateSuccess(
            output: "done",
            structuredOutput: null,
            taskOutputs: [failedTask],
            executionTime: TimeSpan.FromSeconds(3),
            metadata: CrewMetadata.CreateBuilder().AddTotalTokens(0).Build());

        var orchestrator = BuildOrchestrator(out var crew, out var strategyFactory);
        strategyFactory.Strategy.ConfiguredOutput = domainOutput;

        // Act
        var result = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert
        var taskOutput = Assert.Single(result.TaskOutputs);
        Assert.False(taskOutput.Success); // not hardcoded true
        Assert.Equal(TimeSpan.FromSeconds(3), taskOutput.ExecutionTime); // not TimeSpan.Zero
        Assert.Equal(agentId.ToString(), taskOutput.AgentId); // not "unknown"
    }

    [Fact]
    public async Task KickoffAsync_ShouldLeaveAgentIdNull_WhenDomainOutputHasNone()
    {
        // Arrange — domain task output without an agent id (genuinely unavailable)
        var task = Orkeon.Domain.Task.ValueObjects.TaskOutput.Create(
            rawOutput: "output",
            taskId: TaskId.Create(),
            success: true,
            executionTime: TimeSpan.FromMilliseconds(10));

        var domainOutput = DomainCrewOutput.CreateSuccess(
            output: "done",
            structuredOutput: null,
            taskOutputs: [task],
            executionTime: TimeSpan.FromMilliseconds(10),
            metadata: null);

        var orchestrator = BuildOrchestrator(out var crew, out var strategyFactory);
        strategyFactory.Strategy.ConfiguredOutput = domainOutput;

        // Act
        var result = await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert — null, never a fabricated "unknown"
        var taskOutput = Assert.Single(result.TaskOutputs);
        Assert.Null(taskOutput.AgentId);
    }

    private static SequentialCrewOrchestrator BuildOrchestrator(
        out DomainCrew crew,
        out TestProcessStrategyFactory strategyFactory)
    {
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher());

        crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);
        return orchestrator;
    }

    [Fact]
    public void SyncKickoff_ShouldNotExist_OnInterface()
    {
        // Verify that the synchronous Kickoff method has been removed from the interface
        var interfaceType = typeof(ICrewOrchestrationService);
        var syncMethod = interfaceType.GetMethod("Kickoff",
            [typeof(CrewId), typeof(CrewInput), typeof(CancellationToken)]);

        Assert.Null(syncMethod);
    }

    [Fact]
    public void SyncKickoff_ShouldNotExist_OnImplementation()
    {
        // Verify that the synchronous Kickoff method has been removed from the implementation
        var implType = typeof(SequentialCrewOrchestrator);
        var syncMethod = implType.GetMethod("Kickoff",
            [typeof(CrewId), typeof(CrewInput), typeof(CancellationToken)]);

        Assert.Null(syncMethod);
    }

    #endregion

    [Fact]
    public async Task KickoffAsync_PushesTheCrewIdentityOnTheHubCallerContext()
    {
        // HUB-03's ACL reads Message.SourceCrewId, and the hub stamps it from this ambient
        // context. Nothing else in production pushes it — if the orchestrator stops doing so,
        // every sender degrades back to CrewId.System and the ACL is blind again.
        var repository = new TestCrewRepository();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var callerContext = new Orkeon.Infrastructure.EventHub.DefaultEventHubCallerContext();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            hubCallerContext: callerContext);

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        Orkeon.Domain.Common.CrewId? observed = null;
        strategyFactory.Strategy.OnExecute = () => observed = callerContext.Current.CrewId;

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.Equal(crew.Id, observed);
        // And popped afterwards: the identity belongs to the run, not to the thread.
        Assert.Equal(Orkeon.Domain.Common.CrewId.System, callerContext.Current.CrewId);
    }

    [Fact]
    public async Task The_pushed_identity_never_leaks_into_the_callers_flow()
    {
        // The subtle half of the push: an AsyncLocal mutated in an async method's
        // synchronous prefix mutates the CALLER's execution context, and the pop then runs
        // on the callee's resumed copy where it cannot undo it. With a strategy that truly
        // suspends — every real run does — the caller's flow after KickoffAsync must still
        // be System, or the host's next hub call wears the previous crew's badge.
        var repository = new TestCrewRepository();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var callerContext = new Orkeon.Infrastructure.EventHub.DefaultEventHubCallerContext();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), stateManager, strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            hubCallerContext: callerContext);

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        strategyFactory.Strategy.OnExecuteAsync = async () => await System.Threading.Tasks.Task.Yield();

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.Equal(Orkeon.Domain.Common.CrewId.System, callerContext.Current.CrewId);
    }

    #region LLM usage attribution (STUDIO-42)

    [Fact]
    public async Task The_planning_call_is_metered_as_planning_work_for_the_crew()
    {
        var repository = new TestCrewRepository();
        // The planner reads the crew's tasks and agents (GAP-31): the orchestrator is given both.
        var unitOfWork = new NullUnitOfWork();
        var tasks = new Orkeon.Infrastructure.Persistence.Task.InMemoryTaskRepository(unitOfWork);
        var agents = new InMemoryAgentRepository(unitOfWork);
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), new TestProcessStrategyFactory(), new ExecutionPlanParser(), new RecordingDomainEventDispatcher(),
            agentRepository: agents, taskRepository: tasks);
        var agent = new AgentBuilder().Role("Writer").Goal("Write the report").Build();
        var task = new CrewTaskBuilder().Description("Write the report").ExpectedOutput("A report").AssignTo(agent).Build();
        await agents.AddAsync(agent, TestContext.Current.CancellationToken);
        await tasks.AddAsync(task, TestContext.Current.CancellationToken);
        var agentId = agent.Id;
        var taskId = task.Id;
        var provider = new MockLlmProvider();
        provider.SetGenerateFunc((_, _) => new LlmResponse
        {
            Content = """{"plans":[{"task":1,"plan":"1. Write the report."}]}""",
            PromptTokens = 80,
            CompletionTokens = 20,
            TokensUsed = 100,
        });
        var sink = new MockLlmUsageSink();
        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "Planned crew",
            ProcessType = ProcessType.Sequential,
            Planning = true,
            PlanningLlm = Orkeon.Infrastructure.LLMs.MeteredLlmProvider.Wrap(provider, sink),
        });
        crew.AddAgent(agentId);
        crew.AddTask(taskId);
        repository.AddCrew(crew);

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.GenerateCallCount);
        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(Orkeon.Application.Interfaces.Ports.LlmUsageOperations.Planning, usage.OperationType);
        Assert.Equal(crew.Id.ToString(), usage.CrewId);
        Assert.Equal(100, usage.PromptTokens + usage.CompletionTokens);
    }

    [Fact]
    public async Task A_call_nothing_narrower_claims_is_still_metered_for_the_run_it_belongs_to()
    {
        var repository = new TestCrewRepository();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), strategyFactory, new ExecutionPlanParser(), new RecordingDomainEventDispatcher());
        var provider = new MockLlmProvider();
        provider.SetGenerateResult(new LlmResponse { Content = "orphan", PromptTokens = 1, CompletionTokens = 1, TokensUsed = 2 });
        var sink = new MockLlmUsageSink();
        var metered = Orkeon.Infrastructure.LLMs.MeteredLlmProvider.Wrap(provider, sink);
        strategyFactory.Strategy.OnExecuteAsync = () => metered.GenerateAsync("a call no scope claims");
        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(Orkeon.Application.Interfaces.Ports.LlmUsageOperations.Unattributed, usage.OperationType);
        Assert.Equal(crew.Id.ToString(), usage.CrewId);
    }

    #endregion
    #region Domain events of the run (GAP-06)

    private static (SequentialCrewOrchestrator Orchestrator, TestCrewRepository Repository, TestProcessStrategyFactory Strategies, RecordingDomainEventDispatcher Dispatcher) BuildWithRecordingDispatcher()
    {
        var repository = new TestCrewRepository();
        var strategies = new TestProcessStrategyFactory();
        var dispatcher = new RecordingDomainEventDispatcher();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), strategies, new ExecutionPlanParser(), dispatcher);
        return (orchestrator, repository, strategies, dispatcher);
    }

    private static DomainCrew RunnableCrew(TestCrewRepository repository)
    {
        var crew = DomainCrew.Create("Event crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);
        return crew;
    }

    private static List<Type> RunEventTypes(RecordingDomainEventDispatcher dispatcher)
        => dispatcher.Dispatched
            .Where(e => e is Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent
                or Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent
                or Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent)
            .Select(e => e.GetType())
            .ToList();

    private static CrewInput EventInput() => new("ctx", new Dictionary<string, object>());

    [Fact]
    public async Task A_successful_kickoff_dispatches_started_then_completed_and_empties_the_crew()
    {
        var (orchestrator, repository, _, dispatcher) = BuildWithRecordingDispatcher();
        var crew = RunnableCrew(repository);

        var output = await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.True(output.Succeeded);
        Assert.Equal(
            [typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent), typeof(Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent)],
            RunEventTypes(dispatcher));
        Assert.Empty(crew.DomainEvents);
    }

    [Fact]
    public async Task A_failed_kickoff_dispatches_started_then_failed_and_empties_the_crew()
    {
        var (orchestrator, repository, strategies, dispatcher) = BuildWithRecordingDispatcher();
        strategies.Strategy.ShouldFail = true;
        var crew = RunnableCrew(repository);

        var output = await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.False(output.Succeeded);
        Assert.Equal(
            [typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent), typeof(Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent)],
            RunEventTypes(dispatcher));
        Assert.Empty(crew.DomainEvents);
    }

    [Fact]
    public async Task A_cancelled_kickoff_still_dispatches_the_failure()
    {
        var (orchestrator, repository, strategies, dispatcher) = BuildWithRecordingDispatcher();
        strategies.Strategy.OnExecuteAsync = () => throw new OperationCanceledException();
        var crew = RunnableCrew(repository);

        await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.Equal(
            [typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent), typeof(Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent)],
            RunEventTypes(dispatcher));
        Assert.Empty(crew.DomainEvents);
    }

    [Fact]
    public async Task Two_kickoffs_dispatch_each_event_once()
    {
        var (orchestrator, repository, _, dispatcher) = BuildWithRecordingDispatcher();
        var crew = RunnableCrew(repository);

        await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);
        await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.Equal(2, RunEventTypes(dispatcher).Count(t => t == typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent)));
        Assert.Equal(2, RunEventTypes(dispatcher).Count(t => t == typeof(Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent)));
        // Events raised while the crew was built are queued before the run and go out with
        // the first kickoff, once.
        Assert.Equal(dispatcher.Dispatched.Count, dispatcher.Dispatched.Distinct().Count());
        Assert.Single(dispatcher.Dispatched.OfType<Orkeon.Domain.Crew.Events.CrewCreatedEvent>());
    }

    [Fact]
    public async Task A_throwing_handler_changes_neither_a_successful_output_nor_a_failed_one()
    {
        var (orchestrator, repository, _, dispatcher) = BuildWithRecordingDispatcher();
        dispatcher.ThrowOn = typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent);
        var crew = RunnableCrew(repository);

        var success = await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.True(success.Succeeded);
        Assert.Contains("Completed crew goal", success.FinalOutput);
        // The failing handler does not stop the events after it.
        Assert.Contains(dispatcher.Dispatched, e => e is Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent);

        var (failingOrchestrator, failingRepository, failingStrategies, failingDispatcher) = BuildWithRecordingDispatcher();
        failingDispatcher.ThrowOn = typeof(Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent);
        failingStrategies.Strategy.ShouldFail = true;
        var failingCrew = RunnableCrew(failingRepository);

        var failure = await failingOrchestrator.KickoffAsync(failingCrew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.False(failure.Succeeded);
        Assert.Equal("Process strategy failed", failure.Error);
    }

    [Fact]
    public async Task A_streamed_kickoff_dispatches_through_the_same_point()
    {
        // GAP-32: the streamed kickoff is the same run, so it starts and ends the crew like
        // KickoffAsync — the granular path used to skip StartExecution, leaving the crew Idle and
        // dispatching the construction events alone.
        var (orchestrator, repository, _, dispatcher) = BuildWithRecordingDispatcher();
        var crew = RunnableCrew(repository);

        await foreach (var _ in orchestrator.KickoffStreamingAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken))
        {
            // drain
        }

        Assert.Equal(
            [typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent), typeof(Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent)],
            RunEventTypes(dispatcher));
        Assert.Contains(dispatcher.Dispatched, e => e is Orkeon.Domain.Crew.Events.CrewCreatedEvent);
        Assert.Single(crew.Executions);
        Assert.Empty(crew.DomainEvents);
    }

    [Fact]
    public async Task A_failed_streamed_kickoff_fails_the_crew_and_says_why_before_it_finishes()
    {
        var (orchestrator, repository, strategies, dispatcher) = BuildWithRecordingDispatcher();
        strategies.Strategy.ShouldFail = true;
        var crew = RunnableCrew(repository);

        var events = new List<CrewExecutionEvent>();
        await foreach (var executionEvent in orchestrator.KickoffStreamingAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken))
            events.Add(executionEvent);

        Assert.Equal(
            [typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent), typeof(Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent)],
            RunEventTypes(dispatcher));
        Assert.Equal(
            [Orkeon.Constants.Protocol.RunEventKinds.Error, Orkeon.Constants.Protocol.RunEventKinds.RunFinished],
            events.Select(e => e.Kind));
        Assert.Equal("Process strategy failed", events[0].Message);
        Assert.False(events[1].Output!.Succeeded);
    }

    [Fact]
    public async Task A_run_whose_strategy_returns_a_failed_output_fails_the_crew_with_the_outputs_error()
    {
        // GAP-32 decision 4.1: a task that failed fails the crew (the rule of every strategy since
        // GAP-03), so the domain says so too — CrewExecutionFailedEvent with the output's error, the
        // crew and its execution Failed — where it used to say Completed with FailedTasks = 1.
        var (orchestrator, repository, strategies, dispatcher) = BuildWithRecordingDispatcher();
        const string reason = "Task 01JGAP32 (Writer) failed: no final answer";
        strategies.Strategy.ConfiguredOutput = DomainCrewOutput.CreateFailure(
            reason, [], TimeSpan.FromMilliseconds(5), output: "the draft it did produce");
        var crew = RunnableCrew(repository);

        var output = await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.False(output.Succeeded);
        Assert.Equal(reason, output.Error);
        Assert.Equal(
            [typeof(Orkeon.Domain.Crew.Events.CrewExecutionStartedEvent), typeof(Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent)],
            RunEventTypes(dispatcher));
        var failed = Assert.Single(dispatcher.Dispatched.OfType<Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent>());
        Assert.Equal(reason, failed.Reason);
        Assert.Null(failed.Exception);
        Assert.Equal(CrewStatus.Failed, crew.Status);
        var execution = crew.Executions[^1];
        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        Assert.Equal(reason, execution.FailureReason);
    }

    [Fact]
    public async Task A_failed_output_without_an_error_still_fails_the_crew_with_a_readable_reason()
    {
        var (orchestrator, repository, strategies, dispatcher) = BuildWithRecordingDispatcher();
        strategies.Strategy.ConfiguredOutput = DomainCrewOutput.CreateFailure(string.Empty, [], TimeSpan.FromMilliseconds(5));
        var crew = RunnableCrew(repository);

        var output = await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.False(output.Succeeded);
        var failed = Assert.Single(dispatcher.Dispatched.OfType<Orkeon.Domain.Crew.Events.CrewExecutionFailedEvent>());
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
        Assert.Contains("failed", failed.Reason, StringComparison.Ordinal);
        Assert.Equal(failed.Reason, output.Error);
        Assert.DoesNotContain(dispatcher.Dispatched, e => e is Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent);
    }

    [Fact]
    public async Task A_successful_run_counts_each_task_once_by_its_final_outcome()
    {
        // GAP-32 decision 4.2: a graph keeps one output per attempt — a task that failed and then
        // succeeded on its retry is one completed task, and the run is a success, not a partial one.
        var (orchestrator, repository, strategies, dispatcher) = BuildWithRecordingDispatcher();
        var draft = TaskId.Create();
        var review = TaskId.Create();
        strategies.Strategy.ConfiguredOutput = DomainCrewOutput.CreateSuccess(
            "reviewed",
            null,
            [
                Orkeon.Domain.Task.ValueObjects.TaskOutput.Create("Task failed: flaky", "text", null, draft, false, TimeSpan.Zero),
                Orkeon.Domain.Task.ValueObjects.TaskOutput.Create("drafted", "text", null, draft, true, TimeSpan.Zero),
                Orkeon.Domain.Task.ValueObjects.TaskOutput.Create("reviewed", "text", null, review, true, TimeSpan.Zero),
            ],
            TimeSpan.FromMilliseconds(5));
        var crew = RunnableCrew(repository);

        var output = await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.True(output.Succeeded, output.Error);
        var completed = Assert.Single(dispatcher.Dispatched.OfType<Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent>());
        Assert.Equal(2, completed.CompletedTasks);
        Assert.Equal(ExecutionStatus.Succeeded, crew.Executions[^1].Status);
        Assert.Equal(2, crew.Executions[^1].CompletedTasks);
        Assert.Equal(CrewStatus.Idle, crew.Status);
    }

    private sealed class CountingCompletedHandler : Orkeon.Domain.SharedKernel.Events.IDomainEventHandler<Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent>
    {
        public int Calls { get; private set; }

        public System.Threading.Tasks.Task HandleAsync(Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Calls++;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_handler_registered_in_DI_is_called_once_per_successful_kickoff()
    {
        var handler = new CountingCompletedHandler();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<
            Orkeon.Domain.SharedKernel.Events.IDomainEventHandler<Orkeon.Domain.Crew.Events.CrewExecutionCompletedEvent>>(services, _ => handler);
        await using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
            services, new Microsoft.Extensions.DependencyInjection.ServiceProviderOptions { ValidateScopes = true });
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(provider);
        var dispatcher = new Orkeon.Infrastructure.DomainEvents.DomainEventDispatcher(
            scope.ServiceProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<Orkeon.Infrastructure.DomainEvents.DomainEventDispatcher>.Instance);
        var repository = new TestCrewRepository();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, new TestLogger(), new TestStateManager(), new TestProcessStrategyFactory(), new ExecutionPlanParser(), dispatcher);
        var crew = RunnableCrew(repository);

        await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);
        await orchestrator.KickoffAsync(crew.Id, EventInput(), TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Calls);
    }

    #endregion
}
