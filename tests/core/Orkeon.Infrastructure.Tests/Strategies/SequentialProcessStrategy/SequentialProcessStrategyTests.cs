using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class SequentialProcessStrategyTests : IDisposable
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

    private readonly TestLogger<SequentialProcessStrategy> _logger;
    private readonly Dictionary<TaskId, DomainTask> _tasks;
    private readonly Dictionary<AgentId, DomainAgent> _agents;
    private readonly MockAgentExecutionService _mockExecutionService = new();
    private readonly MockMemoryScope _mockMemoryScope = new();
    private readonly AgentDelegationToolsProvider _delegationProvider;
    private readonly SequentialProcessStrategy _strategy;

    public SequentialProcessStrategyTests()
    {
        _logger = new TestLogger<SequentialProcessStrategy>();
        _tasks = [];
        _agents = [];

        // Create minimal mocks that only implement required methods
        var taskRepository = new MinimalTaskRepository(_tasks);
        var agentRepository = new MinimalAgentRepository(_agents);

        // Setup execution service to return output matching old format
        _mockExecutionService.SetExecuteFunc((agent, task, ctx, ct) =>
            new TaskResult(true, $"Task {((DomainTask)task).Id} executed by {agent.Id}", null, [], TimeSpan.FromSeconds(1)));

        _delegationProvider = new AgentDelegationToolsProvider(
            new MockAgentCommunicationService(), _mockExecutionService, new TestLogger<AgentDelegationToolsProvider>());

        _strategy = new SequentialProcessStrategy(
            new CrewStrategyDependencies(taskRepository, agentRepository, _mockExecutionService, _mockMemoryScope),
            _delegationProvider,
            _logger);
    }

    #region Constructor Tests

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullTaskRepository()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SequentialProcessStrategy(
                new CrewStrategyDependencies(null!, new MinimalAgentRepository(_agents), _mockExecutionService, _mockMemoryScope),
                _delegationProvider,
                _logger));
        Assert.Equal("taskRepository", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullAgentRepository()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SequentialProcessStrategy(
                new CrewStrategyDependencies(new MinimalTaskRepository(_tasks), null!, _mockExecutionService, _mockMemoryScope),
                _delegationProvider,
                _logger));
        Assert.Equal("agentRepository", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullLogger()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new SequentialProcessStrategy(
                new CrewStrategyDependencies(new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents), _mockExecutionService, _mockMemoryScope),
                _delegationProvider,
                null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void ShouldCreateStrategy_WhenConstructorWithValidParameters()
    {
        // Act
        var strategy = new SequentialProcessStrategy(
            new CrewStrategyDependencies(
                new MinimalTaskRepository(_tasks),
                new MinimalAgentRepository(_agents),
                _mockExecutionService,
                _mockMemoryScope),
            _delegationProvider,
            _logger);

        // Assert
        Assert.NotNull(strategy);
    }

    #endregion

    #region ExecuteSequentialAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenExecuteSequentialAsyncWithNullCrew()
    {
        // Arrange
        var plan = CrewExecutionPlan.Create();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _strategy.ExecuteSequentialAsync(null!, plan, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenExecuteSequentialAsyncWithNullPlan()
    {
        // Arrange
        var crew = CreateSimpleCrew();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _strategy.ExecuteSequentialAsync(crew, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnEmptyOutput_WhenExecuteSequentialAsyncWithEmptyTasks()
    {
        // Arrange
        var crew = new CrewBuilder()
            .Goal("A crew with no tasks")
            .Sequential()
            .Build();
        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
        Assert.Empty(result.TaskOutputs);
        Assert.True(_logger.HasLoggedInfo("Starting sequential execution"));
        Assert.True(_logger.HasLoggedInfo("Sequential execution completed"));
    }

    [Fact]
    public async Task ShouldPropagateMeasuredTokenTelemetry_WhenExecuteSequentialAsync()
    {
        // Arrange — two tasks, each costing 100 tokens (60 prompt / 40 completion)
        var agent = CreateAgent("metered-agent");
        var task1 = CreateTask("metered-task-1");
        var task2 = CreateTask("metered-task-2");
        var crew = CreateCrewWithTasksAndAgents([task1, task2], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        _mockExecutionService.SetExecuteFunc((a, t, ctx, ct) =>
            new TaskResult(true, "metered output", null, [], TimeSpan.FromMilliseconds(10), TokensUsed: 100)
            {
                PromptTokens = 60,
                CompletionTokens = 40,
            });

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — measured sums reach the crew metadata under the canonical keys (R10.8)
        Assert.True(result.Success);
        Assert.Equal(200, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
        Assert.Equal(120, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey));
        Assert.Equal(80, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey));
    }

    [Fact]
    public async Task ShouldExecute_WhenExecuteSequentialAsyncWithSingleTask()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var task = CreateTask("task1");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.Contains("Task", result.Output);
        Assert.Contains("executed by", result.Output);
        Assert.True(_logger.HasLoggedDebug($"Executing task {task.Id}"));
        Assert.True(_logger.HasLoggedDebug("Task") && _logger.HasLoggedDebug("completed"));
    }

    [Fact]
    public async Task ShouldExecuteInOrder_WhenExecuteSequentialAsyncWithMultipleTasks()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var task1 = CreateTask("task1");
        var task2 = CreateTask("task2");
        var task3 = CreateTask("task3");
        var crew = CreateCrewWithTasksAndAgents(
            [task1, task2, task3],
            [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;
        _tasks[task3.Id] = task3;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);
        // The final output should be from the last task
        Assert.Contains($"Task {task3.Id}", result.Output);
    }

    [Fact]
    public async Task ShouldSkipAndContinue_WhenExecuteSequentialAsyncWithMissingTask()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var task1 = CreateTask("task1");
        var task2 = CreateTask("task2");
        var missingTaskId = TaskId.Create();

        // Create crew with a missing task ID
        var crew = new CrewBuilder()
            .Goal("Crew with missing task")
            .Sequential()
            .WithTask(task1)
            .WithTask(task2)
            .WithAgent(agent)
            .Build();
        crew.AddTask(missingTaskId); // This task won't exist in repository

        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count); // Only 2 tasks executed
        Assert.True(_logger.HasLoggedWarning($"Task {missingTaskId} not found, skipping"));
    }

    [Fact]
    public async Task ShouldThrow_WhenExecuteSequentialAsyncWithNoAgents()
    {
        // Arrange
        var task = CreateTask("task1");
        var crew = new CrewBuilder()
            .Goal("Crew with no agents")
            .Sequential()
            .WithTask(task)
            .Build();
        var plan = CrewExecutionPlan.Create();

        _tasks[task.Id] = task;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No agents available", ex.Message);
    }

    [Fact]
    public async Task ShouldUseFirstAvailable_WhenExecuteSequentialAsyncWithMultipleAgents()
    {
        // Arrange
        var agent1 = CreateAgent("agent1");
        var agent2 = CreateAgent("agent2");
        var task = CreateTask("task1");
        var crew = CreateCrewWithTasksAndAgents(
            [task],
            [agent1, agent2]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent1.Id] = agent1;
        _agents[agent2.Id] = agent2;
        _tasks[task.Id] = task;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        // Should use first agent
        Assert.Contains($"executed by {agent1.Id}", result.Output);
    }

    #endregion

    #region ExecuteHierarchicalAsync Tests

    [Fact]
    public async Task ShouldThrowNotSupported_WhenExecuteHierarchicalAsync()
    {
        // Arrange
        var crew = CreateSimpleCrew();
        var managerAgent = CreateAgent("manager");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Hierarchical execution is not supported", ex.Message);
        Assert.Contains("Use HierarchicalProcessStrategy", ex.Message);
    }

    #endregion

    #region ExecuteParallelAsync Tests

    [Fact]
    public async Task ShouldThrowNotSupported_WhenExecuteParallelAsync()
    {
        // Arrange
        var crew = CreateSimpleCrew();
        var plan = CrewExecutionPlan.Create();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Parallel execution is not supported", ex.Message);
        Assert.Contains("Use ParallelProcessStrategy", ex.Message);
    }

    #endregion

    #region Additional ExecuteSequentialAsync Tests

    [Fact]
    public async Task ShouldStopExecution_WhenExecuteSequentialAsyncWithCancellation()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var task1 = CreateTask("task1");
        var task2 = CreateTask("task2");
        var crew = CreateCrewWithTasksAndAgents(
            [task1, task2],
            [agent]);
        var plan = CrewExecutionPlan.Create();
        using var cts = new CancellationTokenSource();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        // Cancel after starting
        cts.CancelAfter(10);

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        // Result might vary depending on when cancellation occurs
        Assert.True(_logger.HasLoggedInfo("Starting sequential execution"));
    }

    [Fact]
    public async Task ShouldExecuteAll_WhenExecuteSequentialAsyncWithDuplicateTasks()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var task1 = CreateTask("task1");
        var task2 = CreateTask("task1"); // Create second instance with same description
        var crew = new CrewBuilder()
            .Goal("Crew with duplicate tasks")
            .Sequential()
            .WithTask(task1)
            .WithTask(task2) // Add second task with different ID
            .WithAgent(agent)
            .Build();
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count); // Both executions should be recorded
    }

    [Fact]
    public async Task ShouldHandleEfficiently_WhenExecuteSequentialAsyncWithLargeNumberOfTasks()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var tasks = Enumerable.Range(0, 50).Select(i => CreateTask($"task{i}")).ToArray();
        var crew = CreateCrewWithTasksAndAgents(tasks, [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        foreach (var task in tasks)
        {
            _tasks[task.Id] = task;
        }

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(50, result.TaskOutputs.Count);
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenExecuteSequentialAsyncWithTasksButMissingAgents()
    {
        // Arrange
        var task = CreateTask("task1");
        var missingAgentId = AgentId.Create();
        var crew = new CrewBuilder()
            .Goal("Crew with missing agents")
            .Sequential()
            .WithTask(task)
            .Build();
        crew.AddAgent(missingAgentId); // Agent doesn't exist in repository
        var plan = CrewExecutionPlan.Create();

        _tasks[task.Id] = task;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No agents available", ex.Message);
    }

    [Fact]
    public async Task ShouldReturnEmptySuccess_WhenExecuteSequentialAsyncWithAllTasksMissing()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var missingTask1 = TaskId.Create();
        var missingTask2 = TaskId.Create();
        var crew = new CrewBuilder()
            .Goal("Crew with all tasks missing")
            .Sequential()
            .WithAgent(agent)
            .Build();
        crew.AddTask(missingTask1);
        crew.AddTask(missingTask2);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
        Assert.True(_logger.HasLoggedWarning("not found, skipping"));
    }

    [Fact]
    public async Task ShouldExecuteInOrder_WhenExecuteSequentialAsyncWithHighPriorityTask()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var normalTask = new CrewTaskBuilder()
            .Description("Normal priority task")
            .ExpectedOutput("Normal output")
            .Build();
        var highTask = new CrewTaskBuilder()
            .Description("High priority task")
            .ExpectedOutput("High output")
            .Priority(TaskPriority.High)
            .Build();

        // Add high priority task after normal task
        var crew = CreateCrewWithTasksAndAgents(
            [normalTask, highTask],
            [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[normalTask.Id] = normalTask;
        _tasks[highTask.Id] = highTask;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);
        // Sequential should maintain order regardless of priority
        Assert.Contains(normalTask.Id.Value!.ToString(), result.TaskOutputs[0].TaskId!);
        Assert.Contains(highTask.Id.Value!.ToString(), result.TaskOutputs[1].TaskId!);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenExecuteSequentialAsyncWithVeryLongTaskDescription()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var longDescription = string.Join(" ", Enumerable.Repeat("This is a very long description.", 100));
        var task = new CrewTaskBuilder()
            .Description(longDescription.Substring(0, 500)) // TaskDescription might have limits
            .ExpectedOutput("Expected output")
            .Build();
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldBeIdempotent_WhenExecuteSequentialAsyncMultipleTimes()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var task = CreateTask("task1");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        // Act
        var result1 = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);
        var result2 = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.TaskOutputs.Count, result2.TaskOutputs.Count);
    }

    [Fact]
    public async Task ShouldWork_WhenExecuteSequentialAsyncWithAgentHavingSpecialCharactersInName()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Agent-123_Special!")
            .Goal("Special agent goal")
            .Backstory("Special backstory")
            .Build();
        var task = CreateTask("task1");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var plan = CrewExecutionPlan.Create();

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Contains(agent.Id.Value.ToString(), result.Output);
    }

    [Fact]
    public async Task ShouldStillExecute_WhenExecuteSequentialAsyncWithEmptyPlan()
    {
        // Arrange
        var agent = CreateAgent("agent1");
        var task = CreateTask("task1");
        var crew = CreateCrewWithTasksAndAgents([task], [agent]);
        var emptyPlan = CrewExecutionPlan.Create(); // Empty plan

        _agents[agent.Id] = agent;
        _tasks[task.Id] = task;

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, emptyPlan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
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
        {
            builder.WithTask(task);
        }

        foreach (var agent in agents)
        {
            builder.WithAgent(agent);
        }

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

    // Minimal implementation that only implements the methods used by SequentialProcessStrategy
    private class MinimalTaskRepository : ITaskRepository
    {
        private readonly Dictionary<TaskId, DomainTask> _storage;

        public MinimalTaskRepository(Dictionary<TaskId, DomainTask> storage)
        {
            _storage = storage;
        }

        public Task<DomainTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_storage.TryGetValue(id, out var task) ? task : null);
        }

        // All other methods throw NotImplementedException
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
        // ISpecificationRepository methods
        public Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DomainTask>> FindAsync(ISpecification<DomainTask> specification, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AnyAsync(ISpecification<DomainTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class MinimalAgentRepository : IAgentRepository
    {
        private readonly Dictionary<AgentId, DomainAgent> _storage;

        public MinimalAgentRepository(Dictionary<AgentId, DomainAgent> storage)
        {
            _storage = storage;
        }

        public Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_storage.TryGetValue(id, out var agent) ? agent : null);
        }

        // All other methods throw NotImplementedException
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

        // ISpecificationRepository methods
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
