using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the Parallel process strategy.
/// Validates that independent tasks execute concurrently, results are aggregated,
/// and agents are assigned via round-robin.
/// </summary>
public sealed class ParallelProcessIntegrationTests : IDisposable
{
    #region Test Infrastructure

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

        public IEnumerable<string> GetMessages(LogLevel level) =>
            _logEntries.Where(e => e.LogLevel == level).Select(e => e.Message);

        public class LogEntry
        {
            public LogLevel LogLevel { get; init; }
            public string Message { get; init; } = string.Empty;
            public Exception? Exception { get; init; }
        }

        private class NoOpDisposable : IDisposable { public void Dispose() { } }
    }

    private class InMemoryTaskRepository : ITaskRepository
    {
        private readonly Dictionary<TaskId, DomainTask> _storage = [];

        public void Add(DomainTask task) => _storage[task.Id] = task;

        public Task<DomainTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_storage.TryGetValue(id, out var task) ? task : null);

        public Task<DomainTask> AddAsync(DomainTask entity, CancellationToken ct = default) { _storage[entity.Id] = entity; return Task.FromResult(entity); }
        Task IRepository<DomainTask, TaskId>.AddAsync(DomainTask aggregate, CancellationToken ct) { _storage[aggregate.Id] = aggregate; return Task.CompletedTask; }
        public Task UpdateAsync(DomainTask entity, CancellationToken ct = default) { _storage[entity.Id] = entity; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(TaskId id, CancellationToken ct = default) => Task.FromResult(_storage.Remove(id));
        Task IRepository<DomainTask, TaskId>.DeleteAsync(TaskId id, CancellationToken ct) { _storage.Remove(id); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(TaskId id, CancellationToken ct = default) => Task.FromResult(_storage.ContainsKey(id));
        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_storage.Count);
        public Task<IReadOnlyList<DomainTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(ids.Where(_storage.ContainsKey).Select(id => _storage[id]).ToList());
        public Task<IReadOnlyList<DomainTask>> GetByAgentAsync(AgentId agentId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(t => t.AssignedAgent == agentId).ToList());
        public Task<IReadOnlyList<DomainTask>> GetByStatusAsync(Orkeon.Domain.Task.ValueObjects.TaskStatus status, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(t => t.Status == status).ToList());
        public Task<IReadOnlyList<DomainTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(t => t.Dependencies.Contains(taskId)).ToList());
        public Task<IReadOnlyList<DomainTask>> GetReadyTasksAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(t => t.Status == Orkeon.Domain.Task.ValueObjects.TaskStatus.Pending && !t.Dependencies.Any()).ToList());
        public Task<IReadOnlyList<DomainTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(t => t.Priority == priority).ToList());
        public Task<bool> IsCompletedAsync(TaskId id, CancellationToken ct = default)
            => Task.FromResult(_storage.TryGetValue(id, out var t) && t.Status == Orkeon.Domain.Task.ValueObjects.TaskStatus.Completed);
        public Task<IReadOnlyList<DomainTask>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(t => t.CreatedAt >= start && t.CreatedAt <= end).ToList());
        // ISpecificationRepository methods
        public Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(specification.IsSatisfiedBy).ToList());
        public Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, int skip, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainTask>>(_storage.Values.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList());
        public Task<int> CountAsync(ISpecification<DomainTask> specification, CancellationToken ct = default)
            => Task.FromResult(_storage.Values.Count(specification.IsSatisfiedBy));
        public Task<bool> AnyAsync(ISpecification<DomainTask> specification, CancellationToken ct = default)
            => Task.FromResult(_storage.Values.Any(specification.IsSatisfiedBy));
    }

    private class InMemoryAgentRepository : IAgentRepository
    {
        private readonly Dictionary<AgentId, DomainAgent> _storage = [];

        public void Add(DomainAgent agent) => _storage[agent.Id] = agent;

        public Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_storage.TryGetValue(id, out var agent) ? agent : null);

        public Task AddAsync(DomainAgent entity, CancellationToken ct = default) { _storage[entity.Id] = entity; return Task.CompletedTask; }
        public Task UpdateAsync(DomainAgent entity, CancellationToken ct = default) { _storage[entity.Id] = entity; return Task.CompletedTask; }
        public Task DeleteAsync(AgentId id, CancellationToken ct = default) { _storage.Remove(id); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(AgentId id, CancellationToken ct = default) => Task.FromResult(_storage.ContainsKey(id));
        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_storage.Count);
        public Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(IEnumerable<AgentId> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>(ids.Where(_storage.ContainsKey).Select(id => _storage[id]).ToList());
        public Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(AgentRole role, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>(_storage.Values.Where(a => a.Role == role).ToList());
        public Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(AgentStatus status, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>(_storage.Values.Where(a => a.Status == status).ToList());
        public Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>(_storage.Values.Where(a => a.Status == AgentStatus.Idle).ToList());
        public Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(IEnumerable<ToolId> toolIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(CrewId crewId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>([]);
        public Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>(_storage.Values.Where(specification.IsSatisfiedBy).ToList());
        public Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, int skip, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DomainAgent>>(_storage.Values.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList());
        public Task<int> CountAsync(ISpecification<DomainAgent> specification, CancellationToken ct = default)
            => Task.FromResult(_storage.Values.Count(specification.IsSatisfiedBy));
        public Task<bool> AnyAsync(ISpecification<DomainAgent> specification, CancellationToken ct = default)
            => Task.FromResult(_storage.Values.Any(specification.IsSatisfiedBy));
    }

    #endregion

    private readonly TestLogger<ParallelProcessStrategy> _logger;
    private readonly InMemoryTaskRepository _taskRepository;
    private readonly InMemoryAgentRepository _agentRepository;
    private readonly MockAgentExecutionService _mockExecutionService = new();
    private readonly MockMemoryScope _mockMemoryScope = new();
    private readonly ParallelProcessStrategy _strategy;

    public ParallelProcessIntegrationTests()
    {
        _logger = new TestLogger<ParallelProcessStrategy>();
        _taskRepository = new InMemoryTaskRepository();
        _agentRepository = new InMemoryAgentRepository();

        // Setup execution service to return output matching old format
        _mockExecutionService.SetExecuteFunc((agent, task, ctx, ct) =>
            new TaskResult(true, $"Task {((DomainTask)task).Id} executed by {agent.Id}", null, [], TimeSpan.FromSeconds(1)));

        _strategy = new ParallelProcessStrategy(_taskRepository, _agentRepository, _mockExecutionService, _mockMemoryScope, _logger);
    }

    [Fact]
    public async Task ShouldIndependentTasksAllComplete_WhenFullParallelFlow()
    {
        // Arrange: create 3 independent agents
        var agent1 = new AgentBuilder()
            .Role("DataCollector")
            .Goal("Collect data from source A")
            .Backstory("Data specialist A")
            .Build();
        var agent2 = new AgentBuilder()
            .Role("WebScraper")
            .Goal("Scrape data from source B")
            .Backstory("Web scraping expert")
            .Build();
        var agent3 = new AgentBuilder()
            .Role("APIPoller")
            .Goal("Poll API for source C data")
            .Backstory("API integration specialist")
            .Build();
        await _agentRepository.AddAsync(agent1, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(agent2, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(agent3, TestContext.Current.CancellationToken);

        // Create 3 independent tasks
        var taskA = new CrewTaskBuilder()
            .Description("Collect data from source A")
            .ExpectedOutput("Source A data")
            .Build();
        var taskB = new CrewTaskBuilder()
            .Description("Scrape data from source B")
            .ExpectedOutput("Source B data")
            .Build();
        var taskC = new CrewTaskBuilder()
            .Description("Poll API for source C")
            .ExpectedOutput("Source C data")
            .Build();
        await _taskRepository.AddAsync(taskA, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(taskB, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(taskC, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Collect data from all sources in parallel")
            .Parallel()
            .Build();
        crew.AddAgent(agent1.Id);
        crew.AddAgent(agent2.Id);
        crew.AddAgent(agent3.Id);
        crew.AddTask(taskA.Id);
        crew.AddTask(taskB.Id);
        crew.AddTask(taskC.Id);

        var plan = CrewExecutionPlan.Create([taskA.Id, taskB.Id, taskC.Id]);

        // Act
        var result = await _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);
        Assert.All(result.TaskOutputs, output => Assert.True(output.Success));

        // Verify all task IDs are represented in the outputs
        var outputTaskIds = result.TaskOutputs
            .Where(o => o.TaskId != null)
            .Select(o => o.TaskId!.Value.ToString())
            .ToHashSet();
        Assert.Contains(taskA.Id.Value.ToString(), outputTaskIds);
        Assert.Contains(taskB.Id.Value.ToString(), outputTaskIds);
        Assert.Contains(taskC.Id.Value.ToString(), outputTaskIds);
    }

    [Fact]
    public async Task ShouldRoundRobinAgentAssignment_WhenFullParallelFlow()
    {
        // Arrange: 2 agents, 4 tasks -> each agent should get 2 tasks
        var agentAlpha = new AgentBuilder()
            .Role("Alpha")
            .Goal("Process alpha items")
            .Backstory("Alpha processor")
            .Build();
        var agentBeta = new AgentBuilder()
            .Role("Beta")
            .Goal("Process beta items")
            .Backstory("Beta processor")
            .Build();
        await _agentRepository.AddAsync(agentAlpha, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(agentBeta, TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(1, 4).Select(i =>
        {
            var t = new CrewTaskBuilder()
                .Description($"Parallel item {i}")
                .ExpectedOutput($"Result {i}")
                .Build();
            _taskRepository.Add(t);
            return t;
        }).ToArray();

        var crew = new CrewBuilder()
            .Goal("Process items with round-robin")
            .Parallel()
            .Build();
        crew.AddAgent(agentAlpha.Id);
        crew.AddAgent(agentBeta.Id);
        foreach (var t in tasks)
            crew.AddTask(t.Id);

        var plan = CrewExecutionPlan.Create(tasks.Select(t => t.Id));

        // Act
        var result = await _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.TaskOutputs.Count);

        // Verify round-robin assignment: tasks 0,2 -> agentAlpha; tasks 1,3 -> agentBeta
        Assert.Contains(agentAlpha.Id.Value.ToString(), result.TaskOutputs[0].Output);
        Assert.Contains(agentBeta.Id.Value.ToString(), result.TaskOutputs[1].Output);
        Assert.Contains(agentAlpha.Id.Value.ToString(), result.TaskOutputs[2].Output);
        Assert.Contains(agentBeta.Id.Value.ToString(), result.TaskOutputs[3].Output);
    }

    [Fact]
    public async Task ShouldSingleAgentMultipleTasksAllExecuted_WhenFullParallelFlow()
    {
        // Arrange
        var soloAgent = new AgentBuilder()
            .Role("SoloWorker")
            .Goal("Handle all tasks alone")
            .Backstory("Jack of all trades")
            .Build();
        await _agentRepository.AddAsync(soloAgent, TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(1, 5).Select(i =>
        {
            var t = new CrewTaskBuilder()
                .Description($"Solo task {i}")
                .ExpectedOutput($"Solo result {i}")
                .Build();
            _taskRepository.Add(t);
            return t;
        }).ToArray();

        var crew = new CrewBuilder()
            .Goal("One agent handles all parallel tasks")
            .Parallel()
            .Build();
        crew.AddAgent(soloAgent.Id);
        foreach (var t in tasks)
            crew.AddTask(t.Id);

        var plan = CrewExecutionPlan.Create(tasks.Select(t => t.Id));

        // Act
        var result = await _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.TaskOutputs.Count);

        // All tasks should reference the solo agent
        Assert.All(result.TaskOutputs, output =>
            Assert.Contains(soloAgent.Id.Value.ToString(), output.Output));
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenFullParallelFlowNoAgents()
    {
        // Arrange
        var task = new CrewTaskBuilder()
            .Description("Orphan parallel task")
            .ExpectedOutput("No agent to execute")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Crew without agents")
            .Parallel()
            .Build();
        crew.AddTask(task.Id);

        var plan = CrewExecutionPlan.Create([task.Id]);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No agents available", ex.Message);
    }

    [Fact]
    public async Task ShouldMissingTasksSkippedGracefully_WhenFullParallelFlow()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Resilient")
            .Goal("Handle missing tasks")
            .Backstory("Error-tolerant agent")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var validTask = new CrewTaskBuilder()
            .Description("Valid parallel task")
            .ExpectedOutput("Valid result")
            .Build();
        await _taskRepository.AddAsync(validTask, TestContext.Current.CancellationToken);

        var missingTaskId = TaskId.Create();

        var crew = new CrewBuilder()
            .Goal("Handle partial parallel execution")
            .Parallel()
            .Build();
        crew.AddAgent(agent.Id);
        crew.AddTask(validTask.Id);
        crew.AddTask(missingTaskId);

        var plan = CrewExecutionPlan.Create([validTask.Id, missingTaskId]);

        // Act
        var result = await _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.Contains(validTask.Id.Value.ToString(), result.TaskOutputs[0].Output);
    }

    [Fact]
    public async Task ShouldHandleEfficiently_WhenFullParallelFlowLargeScale()
    {
        // Arrange: 10 agents, 50 tasks
        var agents = Enumerable.Range(1, 10).Select(i =>
        {
            var a = new AgentBuilder()
                .Role($"ParallelWorker{i}")
                .Goal($"Worker {i} goal")
                .Backstory($"Worker {i}")
                .Build();
            _agentRepository.Add(a);
            return a;
        }).ToArray();

        var tasks = Enumerable.Range(1, 50).Select(i =>
        {
            var t = new CrewTaskBuilder()
                .Description($"Parallel batch item {i}")
                .ExpectedOutput($"Batch result {i}")
                .Build();
            _taskRepository.Add(t);
            return t;
        }).ToArray();

        var crew = new CrewBuilder()
            .Goal("Large-scale parallel processing")
            .Parallel()
            .Build();
        foreach (var a in agents) crew.AddAgent(a.Id);
        foreach (var t in tasks) crew.AddTask(t.Id);

        var plan = CrewExecutionPlan.Create(tasks.Select(t => t.Id));

        // Act
        var result = await _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(50, result.TaskOutputs.Count);
        Assert.All(result.TaskOutputs, output => Assert.True(output.Success));
    }

    [Fact]
    public async Task ShouldOutputsAggregatedCorrectly_WhenFullParallelFlow()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Aggregator")
            .Goal("Produce aggregatable results")
            .Backstory("Results aggregator")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var task1 = new CrewTaskBuilder()
            .Description("Compute metric A")
            .ExpectedOutput("Metric A value")
            .Build();
        var task2 = new CrewTaskBuilder()
            .Description("Compute metric B")
            .ExpectedOutput("Metric B value")
            .Build();
        await _taskRepository.AddAsync(task1, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(task2, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Compute metrics in parallel")
            .Parallel()
            .Build();
        crew.AddAgent(agent.Id);
        crew.AddTask(task1.Id);
        crew.AddTask(task2.Id);

        var plan = CrewExecutionPlan.Create([task1.Id, task2.Id]);

        // Act
        var result = await _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);

        // Verify statistics
        var stats = result.GetStatistics();
        Assert.Equal(2, stats.TotalTasks);
        Assert.Equal(2, stats.SuccessfulTasks);
        Assert.Equal(0, stats.FailedTasks);
        Assert.Equal(100.0, stats.SuccessRate);

        // Output should be from last task
        Assert.NotEmpty(result.Output);
    }

    [Fact]
    public async Task ShouldUnsupportedMethodsThrow_WhenFullParallelFlow()
    {
        // Arrange
        var crew = new CrewBuilder()
            .Goal("Test unsupported methods")
            .Parallel()
            .Build();
        var plan = CrewExecutionPlan.Create();
        var managerAgent = new AgentBuilder()
            .Role(RoleManager)
            .Goal("Manage")
            .Backstory(RoleManager)
            .Build();

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        _mockMemoryScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
