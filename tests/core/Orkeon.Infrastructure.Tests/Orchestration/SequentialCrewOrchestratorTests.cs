using System.Runtime.CompilerServices;
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
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;

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

        /// <summary>When set, this output is returned verbatim instead of the default one.</summary>
        public DomainCrewOutput? ConfiguredOutput { get; set; }

        public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            return CreateOutput(crew);
        }

        public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            return CreateOutput(crew);
        }

        public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, ExecutionPlan plan, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
        {
            LastReceivedVariables = inputVariables;
            return CreateOutput(crew);
        }

        public Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Autonomous execution is not covered by this test double.");

        private Task<DomainCrewOutput> CreateOutput(DomainCrew crew)
        {
            if (ShouldFail)
                throw new InvalidOperationException("Process strategy failed");

            if (ConfiguredOutput is not null)
                return Task.FromResult(ConfiguredOutput);

            return Task.FromResult(new DomainCrewOutput(
                $"Completed crew goal: {crew.Goal}",
                null,
                [],
                true,
                TimeSpan.FromMilliseconds(100),
                null));
        }
    }

    private sealed class TestProcessStrategyFactory : IProcessStrategyFactory
    {
        public TestProcessStrategy Strategy { get; } = new();
        public IProcessStrategy CreateStrategy(ProcessType processType) => Strategy;
    }

    /// <summary>
    /// Streaming service that emits the full AgentThought granularity (reasoning + tool selection +
    /// tool execution + conclusion) an <see cref="IStreamingAgentExecutionService"/> is expected to
    /// surface — used to prove the orchestrator relays it instead of collapsing to a single output.
    /// </summary>
    private sealed class FakeStreamingAgentExecutionService : IStreamingAgentExecutionService
    {
        public async IAsyncEnumerable<AgentThought> StreamExecutionAsync(
            DomainAgent agent,
            CrewTask task,
            SimpleExecutionContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AgentThought("Thinking about the task", AgentThought.ThoughtType.Reasoning, null, DateTime.UtcNow);
            yield return new AgentThought("Calling tool: search", AgentThought.ThoughtType.ToolSelection, null, DateTime.UtcNow);
            yield return new AgentThought("search returned 3 rows", AgentThought.ThoughtType.ToolExecution, null, DateTime.UtcNow);
            yield return new AgentThought("Final answer", AgentThought.ThoughtType.Conclusion, null, DateTime.UtcNow);
            await System.Threading.Tasks.Task.CompletedTask;
        }
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
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(),
            memoryProviderRegistry: registry);

        var crew = DomainCrew.Create(new CrewCreateOptions
        {
            Goal = "Test crew",
            ProcessType = ProcessType.Sequential,
            MemoryProvider = "redis"
        });
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        // Act
        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("redis", registry.GetProvider(crew.Id));
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
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(),
            memoryProviderRegistry: registry);

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);

        Assert.Null(registry.GetProvider(crew.Id));
    }

    #endregion

    #region KickoffStreamingAsync Tests (P2-O-03)

    [Fact]
    public async Task KickoffStreamingAsync_ShouldYieldAgentThoughtLevelEvents_WhenStreamingServiceRegistered()
    {
        // Arrange — orchestrator wired with the streaming service + agent repository (the default
        // AddOrkeonInfrastructure registration path).
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();

        var agent = new AgentBuilder().Role("Researcher").Goal("Find data").Build();
        var agentRepository = new InMemoryAgentRepository(new NullUnitOfWork());
        await agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser(),
            new FakeStreamingAgentExecutionService(), agentRepository);

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(agent.Id);
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        var input = new CrewInput("ctx", new Dictionary<string, object>());

        // Act
        var events = new List<CrewExecutionEvent>();
        await foreach (var ev in orchestrator.KickoffStreamingAsync(crew.Id, input, TestContext.Current.CancellationToken))
            events.Add(ev);

        // Assert — tool-call granularity is preserved (not collapsed to a single conclusion),
        // and no degradation warning is emitted.
        Assert.Contains(events, e => e.Thought.Type == AgentThought.ThoughtType.ToolSelection);
        Assert.Contains(events, e => e.Thought.Type == AgentThought.ThoughtType.ToolExecution);
        Assert.Contains(events, e => e.Thought.Type == AgentThought.ThoughtType.Conclusion);
        Assert.DoesNotContain(logger.Logs, l => l.Contains("degrading to per-task replay"));
    }

    [Fact]
    public async Task KickoffStreamingAsync_ShouldLogLoudFallbackWarning_WhenStreamingServiceMissing()
    {
        // Arrange — no streaming service and no agent repository (optional ctor args default to null).
        var repository = new TestCrewRepository();
        var logger = new TestLogger();
        var stateManager = new TestStateManager();
        var strategyFactory = new TestProcessStrategyFactory();
        var orchestrator = new SequentialCrewOrchestrator(
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crew = DomainCrew.Create("Test crew", ProcessType.Sequential);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        repository.AddCrew(crew);

        var input = new CrewInput("ctx", new Dictionary<string, object>());

        // Act — the warning fires lazily when the stream is enumerated.
        await foreach (var _ in orchestrator.KickoffStreamingAsync(crew.Id, input, TestContext.Current.CancellationToken))
        {
            // drain
        }

        // Assert — the fallback is loud and names the missing registration.
        Assert.Contains(logger.Logs, l =>
            l.Contains("[Warning]") &&
            l.Contains("degrading to per-task replay") &&
            l.Contains("IStreamingAgentExecutionService"));
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
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

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
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

        var crewId = CrewId.Create();
        var input = new CrewInput("Test", new Dictionary<string, object>());

        // Act
        var result = await orchestrator.KickoffAsync(crewId, input, TestContext.Current.CancellationToken);

        // Assert — the orchestrator catches exceptions and returns a failure output
        Assert.NotNull(result);
        Assert.Contains("Crew execution failed", result.FinalOutput);
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
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

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
            repository, logger, stateManager, strategyFactory, new ExecutionPlanParser());

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
}
