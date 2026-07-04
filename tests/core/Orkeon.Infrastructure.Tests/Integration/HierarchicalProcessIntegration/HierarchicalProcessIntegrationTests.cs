using Orkeon.Domain.Crew;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Crew.Strategies;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the Hierarchical process strategy.
/// Validates manager-worker delegation, task assignment, output review,
/// and context propagation between tasks.
/// </summary>
public sealed class HierarchicalProcessIntegrationTests : IDisposable
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

        public bool HasLogged(LogLevel level, string contains) =>
            _logEntries.Any(e => e.LogLevel == level && e.Message.Contains(contains));

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

    private class TestManagerAgent : IManagerAgent
    {
        private readonly Func<DomainTask, IReadOnlyList<DomainAgent>, SimpleExecutionContext, TaskAssignment>? _assignFunc;
        private readonly Func<ApplicationTaskOutput, DomainTask, bool>? _reviewFunc;
        public List<TaskAssignment> Assignments { get; } = [];
        public List<(ApplicationTaskOutput Output, DomainTask Task)> Reviews { get; } = [];

        public TestManagerAgent(
            Func<DomainTask, IReadOnlyList<DomainAgent>, SimpleExecutionContext, TaskAssignment>? assignFunc = null,
            Func<ApplicationTaskOutput, DomainTask, bool>? reviewFunc = null)
        {
            _assignFunc = assignFunc;
            _reviewFunc = reviewFunc;
        }

        public Task<TaskAssignment> AssignTaskAsync(DomainTask task, IReadOnlyList<DomainAgent> availableAgents, SimpleExecutionContext context)
        {
            TaskAssignment assignment;
            if (_assignFunc != null)
            {
                assignment = _assignFunc(task, availableAgents, context);
            }
            else
            {
                assignment = new TaskAssignment(
                    TaskId: task.Id,
                    AssignedAgent: availableAgents[0].Id,
                    Reason: "Default assignment",
                    AssignedAt: DateTime.UtcNow);
            }
            Assignments.Add(assignment);
            return Task.FromResult(assignment);
        }

        public Task<bool> ReviewOutputAsync(ApplicationTaskOutput output, DomainTask task)
        {
            Reviews.Add((output, task));
            return Task.FromResult(_reviewFunc?.Invoke(output, task) ?? true);
        }
    }

    private class TestAgentExecutionService : IAgentExecutionService
    {
        private readonly Func<DomainAgent, Orkeon.Domain.Task.ICrewTask, SimpleExecutionContext, CancellationToken, TaskResult>? _executeFunc;
        public List<(DomainAgent Agent, Orkeon.Domain.Task.ICrewTask Task)> Executions { get; } = [];

        public TestAgentExecutionService(
            Func<DomainAgent, Orkeon.Domain.Task.ICrewTask, SimpleExecutionContext, CancellationToken, TaskResult>? executeFunc = null)
        {
            _executeFunc = executeFunc;
        }

        public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
        {
            Executions.Add((agent, task));
            if (_executeFunc != null)
                return System.Threading.Tasks.Task.FromResult(_executeFunc(agent, task, context, cancellationToken));

            return System.Threading.Tasks.Task.FromResult(new TaskResult(
                Success: true,
                Output: $"Task {task.TaskId} executed by {agent.Id}",
                StructuredOutput: null,
                ToolsUsed: [],
                ExecutionTime: TimeSpan.FromSeconds(1)));
        }

        public System.Threading.Tasks.Task<TaskExecutionPlan> PlanTaskExecutionAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(new TaskExecutionPlan(agent.Id, [], TimeoutStandard, 0.8));

        public System.Threading.Tasks.Task<bool> CanExecuteTaskAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(true);

        public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken) where TOutput : class
            => System.Threading.Tasks.Task.FromResult(new TaskResult<TOutput>(true, $"Task {task.TaskId}", null, [], TimeSpan.FromSeconds(1)));
    }

    private class TestMemoryScope : IMemoryScope
    {
        public string AgentId { get; } = "test-agent";
        public string ScopeId { get; } = Guid.NewGuid().ToString();
        public Task<T> ExecuteInScopeAsync<T>(Func<Task<T>> operation) => operation();
        public Task ExecuteInScopeAsync(Func<Task> operation) => operation();
        public void Dispose() { }
    }

    #endregion

    private readonly TestLogger<HierarchicalProcessStrategy> _logger;
    private readonly InMemoryTaskRepository _taskRepository;
    private readonly InMemoryAgentRepository _agentRepository;
    private readonly TestMemoryScope _memoryScope;

    public HierarchicalProcessIntegrationTests()
    {
        _logger = new TestLogger<HierarchicalProcessStrategy>();
        _taskRepository = new InMemoryTaskRepository();
        _agentRepository = new InMemoryAgentRepository();
        _memoryScope = new TestMemoryScope();
    }

    private HierarchicalProcessStrategy CreateStrategy(
        TestManagerAgent? managerAgent = null,
        TestAgentExecutionService? executionService = null)
    {
        return new HierarchicalProcessStrategy(
            _taskRepository,
            _agentRepository,
            _logger,
            managerAgent ?? new TestManagerAgent(),
            executionService ?? new TestAgentExecutionService(),
            _memoryScope);
    }

    [Fact]
    public async Task ShouldManagerDelegatesToWorkersAllTasksComplete_WhenFullHierarchicalFlow()
    {
        // Arrange: create manager + 2 worker agents
        var manager = new AgentBuilder()
            .Role("ProjectManager")
            .Goal("Coordinate the team")
            .Backstory("Experienced project manager")
            .AllowDelegation()
            .Build();
        var developer = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal(GoalWriteCode)
            .Backstory("Senior developer")
            .Build();
        var tester = new AgentBuilder()
            .Role("Tester")
            .Goal("Test code")
            .Backstory("QA specialist")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(developer, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(tester, TestContext.Current.CancellationToken);

        // Create tasks
        var codeTask = new CrewTaskBuilder()
            .Description("Implement the login feature")
            .ExpectedOutput("Working login code")
            .Build();
        var testTask = new CrewTaskBuilder()
            .Description("Write tests for the login feature")
            .ExpectedOutput("Test suite for login")
            .Build();
        await _taskRepository.AddAsync(codeTask, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(testTask, TestContext.Current.CancellationToken);

        // Build hierarchical crew
        var crew = new CrewBuilder()
            .Goal("Build and test login feature")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(developer.Id);
        crew.AddAgent(tester.Id);
        crew.AddTask(codeTask.Id);
        crew.AddTask(testTask.Id);

        var testManager = new TestManagerAgent();
        var testExecution = new TestAgentExecutionService();
        var strategy = CreateStrategy(testManager, testExecution);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);
        Assert.All(result.TaskOutputs, output => Assert.True(output.Success));

        // Verify manager made assignment decisions
        Assert.Equal(2, testManager.Assignments.Count);

        // Verify manager reviewed all outputs
        Assert.Equal(2, testManager.Reviews.Count);

        // Verify execution service was called for each task
        Assert.Equal(2, testExecution.Executions.Count);

        // Verify metadata
        var metadata = result.Metadata?.ToDictionary();
        Assert.NotNull(metadata);
        Assert.Equal("hierarchical", metadata["process_type"]);
        Assert.Equal(manager.Id.ToString(), metadata["manager_agent"]);
        Assert.Equal(2, metadata["worker_count"]);
    }

    [Fact]
    public async Task ShouldManagerAssignsSpecificWorkers_WhenFullHierarchicalFlow()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("TeamLead")
            .Goal("Lead the team")
            .Backstory("Technical team lead")
            .Build();
        var frontendDev = new AgentBuilder()
            .Role("FrontendDev")
            .Goal("Build UI components")
            .Backstory("Frontend specialist")
            .Build();
        var backendDev = new AgentBuilder()
            .Role("BackendDev")
            .Goal("Build API services")
            .Backstory("Backend specialist")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(frontendDev, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(backendDev, TestContext.Current.CancellationToken);

        var uiTask = new CrewTaskBuilder()
            .Description("Build the dashboard UI")
            .ExpectedOutput("Dashboard component")
            .Build();
        var apiTask = new CrewTaskBuilder()
            .Description("Build the REST API")
            .ExpectedOutput("API endpoints")
            .Build();
        await _taskRepository.AddAsync(uiTask, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(apiTask, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Build full-stack feature")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(frontendDev.Id);
        crew.AddAgent(backendDev.Id);
        crew.AddTask(uiTask.Id);
        crew.AddTask(apiTask.Id);

        // Manager assigns frontend task to frontend dev, backend task to backend dev
        var assignmentIndex = 0;
        var targetAgents = new[] { frontendDev, backendDev };
        var testManager = new TestManagerAgent(
            assignFunc: (task, agents, context) =>
            {
                var target = targetAgents[assignmentIndex++];
                return new TaskAssignment(
                    TaskId: task.Id,
                    AssignedAgent: target.Id,
                    Reason: $"Assigned to {target.Role} based on specialization",
                    AssignedAt: DateTime.UtcNow);
            });

        var testExecution = new TestAgentExecutionService();
        var strategy = CreateStrategy(testManager, testExecution);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);

        // Verify the correct agents were assigned
        Assert.Equal(frontendDev.Id, testManager.Assignments[0].AssignedAgent);
        Assert.Equal(backendDev.Id, testManager.Assignments[1].AssignedAgent);

        // Verify execution happened with correct agents
        Assert.Equal(frontendDev.Id, testExecution.Executions[0].Agent.Id);
        Assert.Equal(backendDev.Id, testExecution.Executions[1].Agent.Id);
    }

    [Fact]
    public async Task ShouldManagerRejectsOutputMarkedAsNeedsRevision_WhenFullHierarchicalFlow()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("QAManager")
            .Goal("Ensure quality")
            .Backstory("Quality-focused manager")
            .Build();
        var worker = new AgentBuilder()
            .Role("JuniorDev")
            .Goal(GoalWriteCode)
            .Backstory("Junior developer")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker, TestContext.Current.CancellationToken);

        var task = new CrewTaskBuilder()
            .Description("Implement authentication")
            .ExpectedOutput("Secure auth code")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Build secure auth")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(worker.Id);
        crew.AddTask(task.Id);

        // Manager rejects the output
        var testManager = new TestManagerAgent(reviewFunc: (_, _) => false);
        var strategy = CreateStrategy(testManager);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success); // Overall crew succeeds
        Assert.Single(result.TaskOutputs);
        var taskOutput = result.TaskOutputs[0];
        Assert.False(taskOutput.Success); // But individual task marked as failed
        Assert.Contains("[NEEDS REVISION]", taskOutput.Output);

        // Verify warning logged
        Assert.True(_logger.HasLogged(LogLevel.Warning, "Manager rejected output"));
    }

    [Fact]
    public async Task ShouldContextPropagatesBetweenTasks_WhenFullHierarchicalFlow()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("Coordinator")
            .Goal("Coordinate work")
            .Backstory("Team coordinator")
            .Build();
        var worker = new AgentBuilder()
            .Role("Executor")
            .Goal("Execute tasks")
            .Backstory("Task executor")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker, TestContext.Current.CancellationToken);

        var task1 = new CrewTaskBuilder()
            .Description("First step of pipeline")
            .ExpectedOutput("Step 1 output")
            .Build();
        var task2 = new CrewTaskBuilder()
            .Description("Second step of pipeline")
            .ExpectedOutput("Step 2 output")
            .Build();
        var task3 = new CrewTaskBuilder()
            .Description("Third step of pipeline")
            .ExpectedOutput("Step 3 output")
            .Build();
        await _taskRepository.AddAsync(task1, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(task2, TestContext.Current.CancellationToken);
        await _taskRepository.AddAsync(task3, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Run 3-step pipeline")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(worker.Id);
        crew.AddTask(task1.Id);
        crew.AddTask(task2.Id);
        crew.AddTask(task3.Id);

        // Track how many previous outputs each execution sees
        var contextSizes = new List<int>();
        var testExecution = new TestAgentExecutionService(
            executeFunc: (agent, task, context, ct) =>
            {
                contextSizes.Add(context.PreviousOutputs.Count);
                return new TaskResult(
                    Success: true,
                    Output: $"Task {task.TaskId} sees {context.PreviousOutputs.Count} previous outputs",
                    StructuredOutput: null,
                    ToolsUsed: [],
                    ExecutionTime: TimeSpan.FromSeconds(1));
            });

        var strategy = CreateStrategy(executionService: testExecution);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);

        // Verify context grows with each task
        Assert.Equal(3, contextSizes.Count);
        Assert.Equal(0, contextSizes[0]); // First task sees no previous outputs
        Assert.Equal(1, contextSizes[1]); // Second task sees 1 previous output
        Assert.Equal(2, contextSizes[2]); // Third task sees 2 previous outputs
    }

    [Fact]
    public async Task ShouldThrowException_WhenFullHierarchicalFlowNoWorkerAgents()
    {
        // Arrange: only manager, no workers
        var manager = new AgentBuilder()
            .Role("LoneManager")
            .Goal("Manage alone")
            .Backstory("Manager without team")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);

        var task = new CrewTaskBuilder()
            .Description("Task with no workers")
            .ExpectedOutput("Should fail")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Crew with only manager")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddTask(task.Id);

        var strategy = CreateStrategy();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No worker agents available", ex.Message);
    }

    [Fact]
    public async Task ShouldTaskExecutionFailsIncludedInResults_WhenFullHierarchicalFlow()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("FailManager")
            .Goal("Handle failures")
            .Backstory("Error-handling manager")
            .Build();
        var worker = new AgentBuilder()
            .Role("FailWorker")
            .Goal("Try and fail")
            .Backstory("Unreliable worker")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker, TestContext.Current.CancellationToken);

        var task = new CrewTaskBuilder()
            .Description("Task that will fail")
            .ExpectedOutput("Should indicate failure")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Handle execution failures")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(worker.Id);
        crew.AddTask(task.Id);

        var testExecution = new TestAgentExecutionService(
            executeFunc: (agent, task, context, ct) => new TaskResult(
                Success: false,
                Output: "Execution failed: timeout",
                StructuredOutput: null,
                ToolsUsed: [],
                ExecutionTime: TimeoutQuick,
                Error: "Timeout after 30 seconds"));

        var strategy = CreateStrategy(executionService: testExecution);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success); // Crew overall still succeeds
        Assert.Single(result.TaskOutputs);
        Assert.False(result.TaskOutputs[0].Success);
        Assert.Equal("Execution failed: timeout", result.TaskOutputs[0].Output);
    }

    [Fact]
    public async Task ShouldMissingTaskSkippedGracefully_WhenFullHierarchicalFlow()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("SkipManager")
            .Goal("Handle missing tasks")
            .Backstory("Resilient manager")
            .Build();
        var worker = new AgentBuilder()
            .Role("SkipWorker")
            .Goal("Work on available tasks")
            .Backstory("Reliable worker")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker, TestContext.Current.CancellationToken);

        var validTask = new CrewTaskBuilder()
            .Description("Valid hierarchical task")
            .ExpectedOutput("Valid output")
            .Build();
        await _taskRepository.AddAsync(validTask, TestContext.Current.CancellationToken);

        var missingTaskId = TaskId.Create();

        var crew = new CrewBuilder()
            .Goal("Handle partial task list")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(worker.Id);
        crew.AddTask(validTask.Id);
        crew.AddTask(missingTaskId);

        var strategy = CreateStrategy();

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.True(_logger.HasLogged(LogLevel.Warning, missingTaskId.Value.ToString()));
    }

    [Fact]
    public async Task ShouldAssignmentToUnknownAgentTaskSkipped_WhenFullHierarchicalFlow()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("BadAssigner")
            .Goal("Assign to wrong agent")
            .Backstory("Confused manager")
            .Build();
        var worker = new AgentBuilder()
            .Role("RealWorker")
            .Goal("Available but not assigned")
            .Backstory("Available worker")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker, TestContext.Current.CancellationToken);

        var task = new CrewTaskBuilder()
            .Description("Task assigned to ghost")
            .ExpectedOutput("Should be skipped")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Test bad assignment")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(worker.Id);
        crew.AddTask(task.Id);

        var unknownAgentId = AgentId.Create();
        var testManager = new TestManagerAgent(
            assignFunc: (t, agents, ctx) => new TaskAssignment(
                TaskId: t.Id,
                AssignedAgent: unknownAgentId,
                Reason: "Bad assignment",
                AssignedAt: DateTime.UtcNow));

        var testExecution = new TestAgentExecutionService();
        var strategy = CreateStrategy(testManager, testExecution);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs); // Task was skipped
        Assert.Empty(testExecution.Executions); // No execution happened
        Assert.True(_logger.HasLogged(LogLevel.Error, "Assigned agent"));
    }

    [Fact]
    public async Task ShouldDistributeTasks_WhenFullHierarchicalFlowMultipleWorkersRoundRobin()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("RRManager")
            .Goal("Distribute work evenly")
            .Backstory("Fair task distributor")
            .Build();
        var worker1 = new AgentBuilder()
            .Role("Worker1")
            .Goal("Process items")
            .Backstory("Worker one")
            .Build();
        var worker2 = new AgentBuilder()
            .Role("Worker2")
            .Goal("Process items")
            .Backstory("Worker two")
            .Build();
        var worker3 = new AgentBuilder()
            .Role("Worker3")
            .Goal("Process items")
            .Backstory("Worker three")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker1, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker2, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker3, TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(1, 6).Select(i =>
        {
            var t = new CrewTaskBuilder()
                .Description($"Hierarchical task {i}")
                .ExpectedOutput($"Result {i}")
                .Build();
            _taskRepository.Add(t);
            return t;
        }).ToArray();

        var crew = new CrewBuilder()
            .Goal("Distribute 6 tasks across 3 workers")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(worker1.Id);
        crew.AddAgent(worker2.Id);
        crew.AddAgent(worker3.Id);
        foreach (var t in tasks) crew.AddTask(t.Id);

        // Round-robin assignment
        var workers = new[] { worker1, worker2, worker3 };
        var idx = 0;
        var testManager = new TestManagerAgent(
            assignFunc: (task, agents, ctx) =>
            {
                var target = workers[idx % workers.Length];
                idx++;
                return new TaskAssignment(
                    TaskId: task.Id,
                    AssignedAgent: target.Id,
                    Reason: $"Round-robin assignment {idx}",
                    AssignedAt: DateTime.UtcNow);
            });

        var testExecution = new TestAgentExecutionService();
        var strategy = CreateStrategy(testManager, testExecution);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(6, result.TaskOutputs.Count);
        Assert.Equal(6, testExecution.Executions.Count);

        // Verify round-robin: worker1 got tasks 0,3; worker2 got 1,4; worker3 got 2,5
        Assert.Equal(worker1.Id, testExecution.Executions[0].Agent.Id);
        Assert.Equal(worker2.Id, testExecution.Executions[1].Agent.Id);
        Assert.Equal(worker3.Id, testExecution.Executions[2].Agent.Id);
        Assert.Equal(worker1.Id, testExecution.Executions[3].Agent.Id);
        Assert.Equal(worker2.Id, testExecution.Executions[4].Agent.Id);
        Assert.Equal(worker3.Id, testExecution.Executions[5].Agent.Id);

        // Verify metadata shows 3 workers
        var metadata = result.Metadata?.ToDictionary();
        Assert.Equal(3, metadata!["worker_count"]);
    }

    [Fact]
    public async Task ShouldUnsupportedMethodsThrow_WhenFullHierarchicalFlow()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("TestManager")
            .Goal("Test")
            .Backstory("Test")
            .Build();
        var crew = new CrewBuilder()
            .Goal("Test unsupported")
            .Hierarchical(manager)
            .Build();
        var plan = CrewExecutionPlan.Create();
        var strategy = CreateStrategy();

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            () => strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldBeTracked_WhenFullHierarchicalFlowExecutionTime()
    {
        // Arrange
        var manager = new AgentBuilder()
            .Role("TimeManager")
            .Goal("Track time")
            .Backstory("Time tracker")
            .Build();
        var worker = new AgentBuilder()
            .Role("TimeWorker")
            .Goal("Do timed work")
            .Backstory("Timed worker")
            .Build();
        await _agentRepository.AddAsync(manager, TestContext.Current.CancellationToken);
        await _agentRepository.AddAsync(worker, TestContext.Current.CancellationToken);

        var task = new CrewTaskBuilder()
            .Description("Timed task")
            .ExpectedOutput("Timed result")
            .Build();
        await _taskRepository.AddAsync(task, TestContext.Current.CancellationToken);

        var crew = new CrewBuilder()
            .Goal("Track execution time")
            .Hierarchical(manager)
            .Build();
        crew.AddAgent(manager.Id);
        crew.AddAgent(worker.Id);
        crew.AddTask(task.Id);

        var strategy = CreateStrategy();

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, manager.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.ExecutionTime >= TimeSpan.Zero);

        // Verify logging mentions completion
        Assert.True(_logger.HasLogged(LogLevel.Information, "Hierarchical execution completed"));
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
