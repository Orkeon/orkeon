using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// Tests for <see cref="GraphProcessStrategy"/> — LangGraph-style orchestration
/// with typed state graph, conditional edges, and circuit breaker protection.
/// </summary>
public sealed class GraphProcessStrategyTests : IDisposable
{
    #region Test Doubles

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _logEntries = [];

        public IReadOnlyList<LogEntry> LogEntries => _logEntries;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logEntries.Add(new LogEntry
            {
                LogLevel = logLevel,
                Message = formatter(state, exception),
                Exception = exception
            });
        }

        public bool HasLoggedInfo(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Information && e.Message.Contains(containsText));

        public bool HasLoggedDebug(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Debug && e.Message.Contains(containsText));

        public bool HasLoggedWarning(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Warning && e.Message.Contains(containsText));

        public bool HasLoggedError(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Error && e.Message.Contains(containsText));

        public class LogEntry
        {
            public LogLevel LogLevel { get; init; }
            public string Message { get; init; } = string.Empty;
            public Exception? Exception { get; init; }
        }

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    #endregion

    private readonly TestLogger<GraphProcessStrategy> _logger;
    private readonly Dictionary<TaskId, DomainTask> _tasks;
    private readonly Dictionary<AgentId, DomainAgent> _agents;
    private readonly MockAgentExecutionService _mockExecutionService = new();
    private readonly MockMemoryScope _mockMemoryScope = new();
    private readonly AgentDelegationToolsProvider _delegationProvider;
    private readonly GraphProcessStrategy _strategy;

    public GraphProcessStrategyTests()
    {
        _logger = new TestLogger<GraphProcessStrategy>();
        _tasks = [];
        _agents = [];

        var taskRepository = new MinimalTaskRepository(_tasks);
        var agentRepository = new MinimalAgentRepository(_agents);

        _mockExecutionService.SetExecuteFunc((agent, task, ctx, ct) =>
            new TaskResult(true, $"Task {((DomainTask)task).Id} executed by {agent.Id}", null, [], TimeSpan.FromSeconds(1)));

        _delegationProvider = new AgentDelegationToolsProvider(
            new MockAgentCommunicationService(), _mockExecutionService, new TestLogger<AgentDelegationToolsProvider>());

        _strategy = new GraphProcessStrategy(
            taskRepository, agentRepository, _mockExecutionService,
            _mockMemoryScope, _delegationProvider, _logger);
    }

    #region Constructor Tests

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullTaskRepository()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new GraphProcessStrategy(null!, new MinimalAgentRepository(_agents),
                _mockExecutionService, _mockMemoryScope, _delegationProvider, _logger));
        Assert.Equal("taskRepository", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullLogger()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new GraphProcessStrategy(new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents),
                _mockExecutionService, _mockMemoryScope, _delegationProvider, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void ShouldCreateStrategy_WhenConstructorWithValidParameters()
    {
        var strategy = new GraphProcessStrategy(
            new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents),
            _mockExecutionService, _mockMemoryScope, _delegationProvider, _logger);
        Assert.NotNull(strategy);
    }

    #endregion

    #region ExecuteSequentialAsync — Happy Path

    [Fact]
    public async Task ShouldReturnEmptyOutput_WhenNoTasks()
    {
        var crew = new CrewBuilder()
            .Goal("Empty crew")
            .Sequential()
            .Build();
        var plan = CrewExecutionPlan.Create();

        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
        Assert.Empty(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldExecuteSingleTask_ThroughGraph()
    {
        var agent = CreateAgent("analyst");
        var task = CreateTask("analyze");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.Contains("executed by", result.Output);
        Assert.True(_logger.HasLoggedInfo("Starting graph-based execution"));
    }

    [Fact]
    public async Task ShouldExecuteMultipleTasks_InOrder()
    {
        var agent = CreateAgent("worker");
        var task1 = CreateTask("step1");
        var task2 = CreateTask("step2");
        var task3 = CreateTask("step3");
        var crew = CreateCrewWithTasksAndAgents([task1, task2, task3], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;
        _tasks[task3.Id] = task3;

        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);
        // Final output should be from last task
        Assert.Contains($"Task {task3.Id}", result.Output);
    }

    [Fact]
    public async Task ShouldPropagateMeasuredTokenTelemetry_WhenExecutingThroughGraph()
    {
        // Arrange — two tasks, each costing 70 tokens (45 prompt / 25 completion)
        var agent = CreateAgent("metered");
        var task1 = CreateTask("metered_step1");
        var task2 = CreateTask("metered_step2");
        var crew = CreateCrewWithTasksAndAgents([task1, task2], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        _mockExecutionService.SetExecuteFunc((a, t, ctx, ct) =>
            new TaskResult(true, "metered output", null, [], TimeSpan.FromMilliseconds(10), TokensUsed: 70)
            {
                PromptTokens = 45,
                CompletionTokens = 25,
            });

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the graph's internal counting (CrewGraphState.TotalTokensUsed) now
        // surfaces in the crew metadata (R10.8); fails on the legacy code which kept
        // the count internal and returned metadata: null.
        Assert.True(result.Success);
        Assert.Equal(140, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
        Assert.Equal(90, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey));
        Assert.Equal(50, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey));
    }

    #endregion

    #region Retry Logic (Controlled Cycles)

    [Fact]
    public async Task ShouldRetryFailedTasks_UpToMaxRetryCycles()
    {
        var agent = CreateAgent("retrier");
        var task = CreateTask("flaky_task");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        var callCount = 0;
        _mockExecutionService.SetExecuteFunc((a, t, ctx, ct) =>
        {
            callCount++;
            // Fail first 2 attempts, succeed on 3rd
            var success = callCount >= 3;
            return new TaskResult(success,
                success ? "Finally succeeded" : $"Attempt {callCount} failed",
                null, [], TimeSpan.FromSeconds(1));
        });

        var retryStrategy = new GraphProcessStrategy(
            new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents),
            _mockExecutionService, _mockMemoryScope, _delegationProvider, _logger)
        {
            MaxRetryCycles = 3,
            CircuitPolicy = CircuitBreakerPolicy.Permissive
        };

        var result = await retryStrategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success || callCount >= 3);
        Assert.True(callCount >= 3, $"Expected at least 3 calls but got {callCount}");
    }

    [Fact]
    public async Task ShouldStopRetrying_WhenMaxRetriesExceeded()
    {
        var agent = CreateAgent("doomed");
        var task = CreateTask("always_fails");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        _mockExecutionService.SetExecuteFunc((a, t, ctx, ct) =>
            new TaskResult(false, "Always fails", null, [], TimeSpan.FromSeconds(1)));

        var retryStrategy = new GraphProcessStrategy(
            new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents),
            _mockExecutionService, _mockMemoryScope, _delegationProvider, _logger)
        {
            MaxRetryCycles = 2,
            CircuitPolicy = CircuitBreakerPolicy.Permissive
        };

        var result = await retryStrategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Should complete (not hang) — circuit breaker or retry limit should stop it
        Assert.NotNull(result);
        Assert.True(_logger.HasLoggedError("failed after") || _logger.HasLoggedWarning("will retry"));
    }

    #endregion

    #region Circuit Breaker Integration

    [Fact]
    public async Task ShouldReturnFailure_WhenCircuitBreakerTrips()
    {
        var agent = CreateAgent("looper");
        var task = CreateTask("infinite_retry");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        // Always fail so retries keep cycling
        _mockExecutionService.SetExecuteFunc((a, t, ctx, ct) =>
            new TaskResult(false, "Fails forever", null, [], TimeSpan.FromSeconds(0)));

        var tightPolicy = new CircuitBreakerPolicy
        {
            MaxTransitions = 8, // Very low — will trip quickly
            MaxStateVisits = 0,
            StateTimeout = TimeSpan.Zero,
            MaxTotalDuration = TimeSpan.Zero
        };

        var circuitStrategy = new GraphProcessStrategy(
            new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents),
            _mockExecutionService, _mockMemoryScope, _delegationProvider, _logger)
        {
            MaxRetryCycles = 100, // High — rely on circuit breaker to stop
            CircuitPolicy = tightPolicy
        };

        var result = await circuitStrategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("circuit breaker", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Unsupported Process Types

    [Fact]
    public async Task ShouldThrow_WhenExecuteHierarchicalAsync()
    {
        var crew = CreateSimpleCrew();
        var agentId = AgentId.Create();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _strategy.ExecuteHierarchicalAsync(crew, agentId, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenExecuteParallelAsync()
    {
        var crew = CreateSimpleCrew();
        var plan = CrewExecutionPlan.Create();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region Missing Tasks & Agents

    [Fact]
    public async Task ShouldSkipMissingTasks_GracefullyInGraph()
    {
        var agent = CreateAgent("agent1");
        var task1 = CreateTask("exists");
        var crew = new CrewBuilder()
            .Goal("Crew with missing task")
            .Sequential()
            .WithTask(task1)
            .WithAgent(agent)
            .Build();

        var missingTaskId = TaskId.Create();
        crew.AddTask(missingTaskId);

        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        // missingTaskId is NOT in _tasks

        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs); // Only 1 of 2 tasks executed
        Assert.True(_logger.HasLoggedWarning("not found"));
    }

    [Fact]
    public async Task ShouldThrow_WhenNoAgentsAvailable()
    {
        var task = CreateTask("orphan");
        var crew = new CrewBuilder()
            .Goal("No agents")
            .Sequential()
            .WithTask(task)
            .Build();
        var plan = CrewExecutionPlan.Create();

        _tasks[task.Id] = task;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No agents available", ex.Message);
    }

    #endregion

    #region Helper Methods

    private static DomainCrew CreateSimpleCrew()
    {
        return new CrewBuilder()
            .Goal("A simple test crew")
            .Sequential()
            .Build();
    }

    private static DomainCrew CreateCrewWithTasksAndAgents(DomainTask[] tasks, DomainAgent[] agents)
    {
        var builder = new CrewBuilder()
            .Goal("Crew with tasks and agents")
            .Sequential();

        foreach (var task in tasks)
            builder.WithTask(task);
        foreach (var agent in agents)
            builder.WithAgent(agent);

        return builder.Build();
    }

    private static DomainAgent CreateAgent(string name)
    {
        return new AgentBuilder()
            .Role(name)
            .Goal($"Goal for {name}")
            .Backstory($"Backstory for {name}")
            .Build();
    }

    private static DomainTask CreateTask(string name)
    {
        return new CrewTaskBuilder()
            .Description($"Description for {name}")
            .ExpectedOutput($"Expected output for {name}")
            .Build();
    }

    #endregion

    #region Minimal Repository Implementations

    private class MinimalTaskRepository : ITaskRepository
    {
        private readonly Dictionary<TaskId, DomainTask> _storage;
        public MinimalTaskRepository(Dictionary<TaskId, DomainTask> storage) => _storage = storage;

        public Task<DomainTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_storage.TryGetValue(id, out var task) ? task : null);

        public Task<DomainTask> AddAsync(DomainTask entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        Task IRepository<DomainTask, TaskId>.AddAsync(DomainTask aggregate, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateAsync(DomainTask entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(TaskId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        Task IRepository<DomainTask, TaskId>.DeleteAsync(TaskId id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(TaskId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> GetByStatusAsync(Orkeon.Domain.Task.ValueObjects.TaskStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> IsCompletedAsync(TaskId taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AnyAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class MinimalAgentRepository : IAgentRepository
    {
        private readonly Dictionary<AgentId, DomainAgent> _storage;
        public MinimalAgentRepository(Dictionary<AgentId, DomainAgent> storage) => _storage = storage;

        public Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_storage.TryGetValue(id, out var agent) ? agent : null);

        public Task AddAsync(DomainAgent entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(DomainAgent entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(AgentId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(AgentId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(IEnumerable<AgentId> ids, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(AgentStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(IEnumerable<ToolId> toolIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(CrewId crewId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AnyAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    #endregion

    public void Dispose()
    {
        _mockMemoryScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
