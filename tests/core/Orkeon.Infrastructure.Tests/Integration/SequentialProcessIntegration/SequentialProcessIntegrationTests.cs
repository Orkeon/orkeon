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
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the Sequential process strategy.
/// Validates the full flow: create agents, create tasks, execute crew sequentially,
/// verify ordering and output propagation.
/// </summary>
public sealed class SequentialProcessIntegrationTests : IDisposable
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

    private readonly TestLogger<SequentialProcessStrategy> _logger;
    private readonly InMemoryTaskRepository _taskRepository;
    private readonly InMemoryAgentRepository _agentRepository;
    private readonly MockAgentExecutionService _mockExecutionService = new();
    private readonly MockMemoryScope _mockMemoryScope = new();
    private readonly SequentialProcessStrategy _strategy;

    public SequentialProcessIntegrationTests()
    {
        _logger = new TestLogger<SequentialProcessStrategy>();
        _taskRepository = new InMemoryTaskRepository();
        _agentRepository = new InMemoryAgentRepository();

        // Setup execution service to return output matching old format
        _mockExecutionService.SetExecuteFunc((agent, task, ctx, ct) =>
            new TaskResult(true, $"Task {((DomainTask)task).Id} executed by {agent.Id}", null, [], TimeSpan.FromSeconds(1)));

        var delegationProvider = new AgentDelegationToolsProvider(
            new MockAgentCommunicationService(), _mockExecutionService, new TestLogger<AgentDelegationToolsProvider>());

        _strategy = new SequentialProcessStrategy(_taskRepository, _agentRepository, _mockExecutionService, _mockMemoryScope, delegationProvider, _logger);
    }

    [Fact]
    public async Task ShouldExecuteInOrder_WhenFullSequentialFlowSingleAgentThreeTasks()
    {
        // Arrange: create a researcher agent
        var researcher = new AgentBuilder()
            .Role("Researcher")
            .Goal("Research the topic thoroughly")
            .Backstory("An experienced researcher")
            .Build();
        await _agentRepository.AddAsync(researcher, TestContext.Current.CancellationToken);

        // Create 3 sequential tasks
        var task1 = new CrewTaskBuilder()
            .Description("Gather data from public sources")
            .ExpectedOutput("Raw data report")
            .Build();
        var task2 = new CrewTaskBuilder()
            .Description("Analyze the gathered data")
            .ExpectedOutput("Analysis document")
            .Build();
        var task3 = new CrewTaskBuilder()
            .Description("Write final summary report")
            .ExpectedOutput("Final report")
            .Build();
        await _taskRepository.AddAsync(task1, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(task2, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(task3, TestContext.Current.CancellationToken);

        // Build the crew
        var crew = new CrewBuilder()
            .Goal("Complete research pipeline")
            .Sequential()
            .Build();
        crew.AddAgent(researcher.Id);
        crew.AddTask(task1.Id);
        crew.AddTask(task2.Id);
        crew.AddTask(task3.Id);

        var plan = CrewExecutionPlan.Create([task1.Id, task2.Id, task3.Id]);

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);

        // Verify tasks were executed in order (each output references the correct task ID)
        Assert.Contains(task1.Id.Value.ToString(), result.TaskOutputs[0].Output);
        Assert.Contains(task2.Id.Value.ToString(), result.TaskOutputs[1].Output);
        Assert.Contains(task3.Id.Value.ToString(), result.TaskOutputs[2].Output);

        // Verify final output is from the last task
        Assert.Contains(task3.Id.Value.ToString(), result.Output);

        // Verify all tasks reference the same agent
        foreach (var taskOutput in result.TaskOutputs)
        {
            Assert.Contains(researcher.Id.Value.ToString(), taskOutput.Output);
        }
    }

    [Fact]
    public async Task ShouldUseFirstAvailableAgent_WhenFullSequentialFlowMultipleAgents()
    {
        // Arrange: create multiple agents
        var writer = new AgentBuilder()
            .Role("Writer")
            .Goal("Write compelling content")
            .Backstory("A skilled content writer")
            .Build();
        var editor = new AgentBuilder()
            .Role("Editor")
            .Goal("Edit and proofread content")
            .Backstory("An experienced editor")
            .Build();
        await _agentRepository.AddAsync(writer, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(editor, TestContext.Current.CancellationToken);

        var task1 = new CrewTaskBuilder()
            .Description("Write a blog post")
            .ExpectedOutput("Blog post draft")
            .Build();
        var task2 = new CrewTaskBuilder()
            .Description("Edit the blog post")
            .ExpectedOutput("Edited blog post")
            .Build();
        await _taskRepository.AddAsync(task1, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(task2, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Create a polished blog post")
            .Sequential()
            .Build();
        crew.AddAgent(writer.Id);
        crew.AddAgent(editor.Id);
        crew.AddTask(task1.Id);
        crew.AddTask(task2.Id);

        var plan = CrewExecutionPlan.Create([task1.Id, task2.Id]);

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);

        // Sequential strategy assigns agents round-robin
        Assert.Contains(writer.Id.Value.ToString(), result.TaskOutputs[0].Output);
        Assert.Contains(editor.Id.Value.ToString(), result.TaskOutputs[1].Output);
    }

    [Fact]
    public async Task ShouldOutputsPassedBetweenTasksFinalOutputIsFromLastTask_WhenFullSequentialFlow()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleAnalyst)
            .Goal(GoalAnalyzeData)
            .Backstory("Data analyst")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(1, 5).Select(i =>
        {
            var t = new CrewTaskBuilder()
                .Description($"Step {i} of analysis pipeline")
                .ExpectedOutput($"Step {i} output")
                .Build();
            _taskRepository.Add(t);
            return t;
        }).ToArray();

        var crew = new CrewBuilder()
            .Goal("Run 5-step analysis")
            .Sequential()
            .Build();
        crew.AddAgent(agent.Id);
        foreach (var t in tasks)
            crew.AddTask(t.Id);

        var plan = CrewExecutionPlan.Create(tasks.Select(t => t.Id));

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.TaskOutputs.Count);

        // Final output should reference the last task
        var lastTask = tasks.Last();
        Assert.Contains(lastTask.Id.Value.ToString(), result.Output);

        // Each task output should be marked as successful
        Assert.All(result.TaskOutputs, output => Assert.True(output.Success));
    }

    [Fact]
    public async Task ShouldWithMissingTasksGracefullySkips_WhenFullSequentialFlow()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleWorker)
            .Goal("Do work")
            .Backstory("A worker")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var validTask = new CrewTaskBuilder()
            .Description("Valid task to execute")
            .ExpectedOutput("Valid output")
            .Build();
        await _taskRepository.AddAsync(validTask, TestContext.Current.CancellationToken);

        var missingTaskId = TaskId.Create();

        var crew = new CrewBuilder()
            .Goal("Handle partial task availability")
            .Sequential()
            .Build();
        crew.AddAgent(agent.Id);
        crew.AddTask(validTask.Id);
        crew.AddTask(missingTaskId);

        var plan = CrewExecutionPlan.Create([validTask.Id, missingTaskId]);

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.Contains(validTask.Id.Value.ToString(), result.TaskOutputs[0].Output);

        // Verify warning was logged
        var warnings = _logger.GetMessages(LogLevel.Warning).ToList();
        Assert.Contains(warnings, w => w.Contains(missingTaskId.Value.ToString()));
    }

    [Fact]
    public async Task ShouldReturnEmptySuccess_WhenFullSequentialFlowEmptyCrew()
    {
        // Arrange: crew with no tasks
        var crew = new CrewBuilder()
            .Goal("Empty crew")
            .Sequential()
            .Build();
        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async Task ShouldExecuteAllInCrewOrder_WhenFullSequentialFlowWithTaskDependencies()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Build software")
            .Backstory("Full-stack developer")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var designTask = new CrewTaskBuilder()
            .Description("Design the API schema")
            .ExpectedOutput("API schema document")
            .Build();
        var implementTask = new CrewTaskBuilder()
            .Description("Implement the API endpoints")
            .ExpectedOutput("Working API code")
            .Build();
        var testTask = new CrewTaskBuilder()
            .Description("Write tests for the API")
            .ExpectedOutput("Test suite")
            .Build();

        // Add task dependency: implement depends on design, test depends on implement
        implementTask.AddDependency(designTask.Id);
        testTask.AddDependency(implementTask.Id);

        await _taskRepository.AddAsync(designTask, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(implementTask, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(testTask, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Build and test API")
            .Sequential()
            .Build();
        crew.AddAgent(agent.Id);
        crew.AddTask(designTask.Id);
        crew.AddTask(implementTask.Id);
        crew.AddTask(testTask.Id);

        var plan = CrewExecutionPlan.Create([designTask.Id, implementTask.Id, testTask.Id]);

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);

        // Verify order maintained
        Assert.Contains(designTask.Id.Value.ToString(), result.TaskOutputs[0].Output);
        Assert.Contains(implementTask.Id.Value.ToString(), result.TaskOutputs[1].Output);
        Assert.Contains(testTask.Id.Value.ToString(), result.TaskOutputs[2].Output);
    }

    [Fact]
    public async Task ShouldCompleteSuccessfully_WhenFullSequentialFlowLargeNumberOfTasks()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("BatchProcessor")
            .Goal("Process batch operations")
            .Backstory("Batch processing specialist")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var taskCount = 100;
        var tasks = new List<DomainTask>();
        var crew = new CrewBuilder()
            .Goal("Process large batch")
            .Sequential()
            .Build();
        crew.AddAgent(agent.Id);

        for (int i = 0; i < taskCount; i++)
        {
            var task = new CrewTaskBuilder()
                .Description($"Batch item {i}")
                .ExpectedOutput($"Processed item {i}")
                .Build();
            await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);
            crew.AddTask(task.Id);
            tasks.Add(task);
        }

        var plan = CrewExecutionPlan.Create(tasks.Select(t => t.Id));

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(taskCount, result.TaskOutputs.Count);
        Assert.All(result.TaskOutputs, output => Assert.True(output.Success));
    }

    [Fact]
    public async Task ShouldThrowOnExecution_WhenFullSequentialFlowNoAgentInRepository()
    {
        // Arrange: agent added to crew but not in repository
        var agent = new AgentBuilder()
            .Role("Ghost")
            .Goal("Invisible agent")
            .Backstory("Does not exist in repository")
            .Build();
        // Intentionally NOT adding to repository

        var task = new CrewTaskBuilder()
            .Description("Task for ghost agent")
            .ExpectedOutput("This should fail")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Crew with missing agent")
            .Sequential()
            .Build();
        crew.AddAgent(agent.Id);
        crew.AddTask(task.Id);

        var plan = CrewExecutionPlan.Create([task.Id]);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No agents available", ex.Message);
    }

    [Fact]
    public async Task ShouldBeCorrect_WhenFullSequentialFlowExecutionStatistics()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("StatsAgent")
            .Goal("Generate stats")
            .Backstory("Statistics gatherer")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var task1 = new CrewTaskBuilder()
            .Description("First stat task")
            .ExpectedOutput("Stat 1")
            .Build();
        var task2 = new CrewTaskBuilder()
            .Description("Second stat task")
            .ExpectedOutput("Stat 2")
            .Build();
        await _taskRepository.AddAsync(task1, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(task2, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Gather statistics")
            .Sequential()
            .Build();
        crew.AddAgent(agent.Id);
        crew.AddTask(task1.Id);
        crew.AddTask(task2.Id);

        var plan = CrewExecutionPlan.Create([task1.Id, task2.Id]);

        // Act
        var result = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var stats = result.GetStatistics();
        Assert.Equal(2, stats.TotalTasks);
        Assert.Equal(2, stats.SuccessfulTasks);
        Assert.Equal(0, stats.FailedTasks);
        Assert.Equal(100.0, stats.SuccessRate);
    }

    [Fact]
    public async Task ShouldProduceConsistentResults_WhenFullSequentialFlowRerunSameCrew()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Repeater")
            .Goal("Repeat work")
            .Backstory("Consistent worker")
            .Build();
        await _agentRepository.AddAsync(agent, TestContext.Current.CancellationToken);

        var task = new CrewTaskBuilder()
            .Description("Repeatable task")
            .ExpectedOutput("Same result each time")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Verify idempotency")
            .Sequential()
            .Build();
        crew.AddAgent(agent.Id);
        crew.AddTask(task.Id);

        var plan = CrewExecutionPlan.Create([task.Id]);

        // Act
        var result1 = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);
        var result2 = await _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(result1.TaskOutputs.Count, result2.TaskOutputs.Count);
        Assert.Equal(result1.Success, result2.Success);
        Assert.Equal(result1.Output, result2.Output);
    }

    public void Dispose()
    {
        _mockMemoryScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
