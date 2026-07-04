using Orkeon.Domain.Crew;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Domain.Task;
using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Crew.Strategies;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class HierarchicalProcessStrategyTests : IDisposable
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

    private class TestManagerAgent : IManagerAgent
    {
        private readonly Func<DomainTask, IReadOnlyList<DomainAgent>, SimpleExecutionContext, TaskAssignment>? _assignTaskFunc;
        private readonly Func<ApplicationTaskOutput, DomainTask, bool>? _reviewOutputFunc;

        public TestManagerAgent(
            Func<DomainTask, IReadOnlyList<DomainAgent>, SimpleExecutionContext, TaskAssignment>? assignTaskFunc = null,
            Func<ApplicationTaskOutput, DomainTask, bool>? reviewOutputFunc = null)
        {
            _assignTaskFunc = assignTaskFunc;
            _reviewOutputFunc = reviewOutputFunc;
        }

        public Task<TaskAssignment> AssignTaskAsync(DomainTask task, IReadOnlyList<DomainAgent> availableAgents, SimpleExecutionContext context)
        {
            if (_assignTaskFunc != null)
            {
                return Task.FromResult(_assignTaskFunc(task, availableAgents, context));
            }

            // Default behavior: assign to first available agent
            var assignment = new TaskAssignment(
                TaskId: task.Id,
                AssignedAgent: availableAgents[0].Id,
                Reason: "Default assignment to first agent",
                AssignedAt: DateTime.UtcNow
            );
            return Task.FromResult(assignment);
        }

        public Task<bool> ReviewOutputAsync(ApplicationTaskOutput output, DomainTask task)
        {
            if (_reviewOutputFunc != null)
            {
                return Task.FromResult(_reviewOutputFunc(output, task));
            }

            // Default behavior: approve all outputs
            return Task.FromResult(true);
        }
    }

    private class TestAgentExecutionService : IAgentExecutionService
    {
        private readonly Func<DomainAgent, Orkeon.Domain.Task.ICrewTask, SimpleExecutionContext, CancellationToken, TaskResult>? _executeFunc;

        public TestAgentExecutionService(
            Func<DomainAgent, Orkeon.Domain.Task.ICrewTask, SimpleExecutionContext, CancellationToken, TaskResult>? executeFunc = null)
        {
            _executeFunc = executeFunc;
        }

        public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
        {
            if (_executeFunc != null)
            {
                return System.Threading.Tasks.Task.FromResult(_executeFunc(agent, task, context, cancellationToken));
            }

            // Default behavior: successful execution
            var result = new TaskResult(
                Success: true,
                Output: $"Task {task.TaskId} executed by {agent.Id}",
                StructuredOutput: null,
                ToolsUsed: [],
                ExecutionTime: TimeSpan.FromSeconds(1),
                Error: null
            );
            return System.Threading.Tasks.Task.FromResult(result);
        }

        public System.Threading.Tasks.Task<TaskExecutionPlan> PlanTaskExecutionAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
        {
            var plan = new TaskExecutionPlan(
                AssignedAgent: agent.Id,
                Steps: [],
                EstimatedDuration: TimeoutStandard,
                ConfidenceScore: 0.8
            );
            return System.Threading.Tasks.Task.FromResult(plan);
        }

        public System.Threading.Tasks.Task<bool> CanExecuteTaskAsync(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.FromResult(true);
        }

        public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(DomainAgent agent, Orkeon.Domain.Task.ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken) where TOutput : class
        {
            var result = new TaskResult<TOutput>(
                Success: true,
                RawOutput: $"Task {task.TaskId} executed by {agent.Id}",
                StructuredOutput: null,
                ToolsUsed: [],
                ExecutionTime: TimeSpan.FromSeconds(1),
                Error: null
            );
            return System.Threading.Tasks.Task.FromResult(result);
        }
    }

    private class TestMemoryScope : IMemoryScope
    {
        public string AgentId { get; }
        public string ScopeId { get; }

        public TestMemoryScope(string agentId = "test-agent")
        {
            AgentId = agentId;
            ScopeId = Guid.NewGuid().ToString();
        }

        public Task<T> ExecuteInScopeAsync<T>(Func<Task<T>> operation)
        {
            return operation();
        }

        public Task ExecuteInScopeAsync(Func<Task> operation)
        {
            return operation();
        }

        public void Dispose() { }
    }

    #endregion

    private readonly TestLogger<HierarchicalProcessStrategy> _logger;
    private readonly Dictionary<TaskId, DomainTask> _tasks;
    private readonly Dictionary<AgentId, DomainAgent> _agents;
    private readonly TestManagerAgent _managerAgent;
    private readonly TestAgentExecutionService _executionService;
    private readonly TestMemoryScope _memoryScope;
    private readonly HierarchicalProcessStrategy _strategy;

    public HierarchicalProcessStrategyTests()
    {
        _logger = new TestLogger<HierarchicalProcessStrategy>();
        _tasks = [];
        _agents = [];
        _managerAgent = new TestManagerAgent();
        _executionService = new TestAgentExecutionService();
        _memoryScope = new TestMemoryScope();

        // Create minimal mocks that only implement required methods
        var taskRepository = new MinimalTaskRepository(_tasks);
        var agentRepository = new MinimalAgentRepository(_agents);

        _strategy = new HierarchicalProcessStrategy(
            taskRepository,
            agentRepository,
            _logger,
            _managerAgent,
            _executionService,
            _memoryScope);
    }

    #region Constructor Tests

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullTaskRepository()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new HierarchicalProcessStrategy(null!, new MinimalAgentRepository(_agents), _logger, _managerAgent, _executionService, _memoryScope));
        Assert.Equal("taskRepository", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullAgentRepository()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new HierarchicalProcessStrategy(new MinimalTaskRepository(_tasks), null!, _logger, _managerAgent, _executionService, _memoryScope));
        Assert.Equal("agentRepository", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullLogger()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new HierarchicalProcessStrategy(new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents), null!, _managerAgent, _executionService, _memoryScope));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullManagerAgent()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new HierarchicalProcessStrategy(new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents), _logger, null!, _executionService, _memoryScope));
        Assert.Equal("managerAgent", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullExecutionService()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new HierarchicalProcessStrategy(new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents), _logger, _managerAgent, null!, _memoryScope));
        Assert.Equal("executionService", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullMemoryScope()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new HierarchicalProcessStrategy(new MinimalTaskRepository(_tasks), new MinimalAgentRepository(_agents), _logger, _managerAgent, _executionService, null!));
        Assert.Equal("memoryScope", ex.ParamName);
    }

    [Fact]
    public void ShouldCreateStrategy_WhenConstructorWithValidParameters()
    {
        // Act
        var strategy = new HierarchicalProcessStrategy(
            new MinimalTaskRepository(_tasks),
            new MinimalAgentRepository(_agents),
            _logger,
            _managerAgent,
            _executionService,
            _memoryScope);

        // Assert
        Assert.NotNull(strategy);
    }

    #endregion

    #region ExecuteHierarchicalAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenExecuteHierarchicalAsyncWithNullCrew()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _strategy.ExecuteHierarchicalAsync(null!, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenExecuteHierarchicalAsyncWithNullManagerAgent()
    {
        // Arrange
        var crew = CreateSimpleCrew();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _strategy.ExecuteHierarchicalAsync(crew, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenExecuteHierarchicalAsyncWithNoWorkerAgents()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var crew = new CrewBuilder()
            .Goal("A crew with only manager")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .Build();

        _agents[managerAgent.Id] = managerAgent;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No worker agents available", ex.Message);
    }

    [Fact]
    public async Task ShouldExecute_WhenExecuteHierarchicalAsyncWithSingleWorker()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var workerAgent = CreateAgent("worker1");
        var task = CreateTask("task1");

        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(workerAgent)
            .WithTask(task)
            .Build();

        _agents[managerAgent.Id] = managerAgent;
        _agents[workerAgent.Id] = workerAgent;
        _tasks[task.Id] = task;

        // Act
        var result = await _strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.Contains($"Task {task.Id}", result.Output);
        Assert.Contains($"executed by {workerAgent.Id}", result.Output);
        Assert.True(_logger.HasLoggedInfo("Starting hierarchical execution"));
        Assert.True(_logger.HasLoggedInfo("Manager assigned task"));
        Assert.True(_logger.HasLoggedInfo("Hierarchical execution completed"));
    }

    [Fact]
    public async Task ShouldDistributeTasks_WhenExecuteHierarchicalAsyncWithMultipleWorkers()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var worker1 = CreateAgent("worker1");
        var worker2 = CreateAgent("worker2");
        var task1 = CreateTask("task1");
        var task2 = CreateTask("task2");

        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(worker1)
            .WithAgent(worker2)
            .WithTask(task1)
            .WithTask(task2)
            .Build();

        _agents[managerAgent.Id] = managerAgent;
        _agents[worker1.Id] = worker1;
        _agents[worker2.Id] = worker2;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        // Custom manager that alternates workers
        var assignmentCount = 0;
        var customManager = new TestManagerAgent(
            assignTaskFunc: (task, agents, context) =>
            {
                var selectedAgent = agents.ToList()[assignmentCount % agents.Count];
                assignmentCount++;
                return new TaskAssignment(
                    TaskId: task.Id,
                    AssignedAgent: selectedAgent.Id,
                    Reason: $"Assigned to worker {assignmentCount}",
                    AssignedAt: DateTime.UtcNow
                );
            }
        );

        var strategy = new HierarchicalProcessStrategy(
            new MinimalTaskRepository(_tasks),
            new MinimalAgentRepository(_agents),
            _logger,
            customManager,
            _executionService,
            _memoryScope);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);
        var metadata = result.Metadata?.ToDictionary();
        Assert.NotNull(metadata);
        Assert.Equal("hierarchical", metadata["process_type"]);
        Assert.Equal(managerAgent.Id.ToString(), metadata["manager_agent"]);
        Assert.Equal(2, metadata["worker_count"]);
    }

    [Fact]
    public async Task ShouldPropagateMeasuredTokenTelemetry_WhenExecuteHierarchicalAsync()
    {
        // Arrange — two tasks delegated to a worker, each costing 80 tokens (50/30)
        var managerAgent = CreateAgent("manager");
        var worker = CreateAgent("worker1");
        var task1 = CreateTask("metered-task-1");
        var task2 = CreateTask("metered-task-2");

        var crew = new CrewBuilder()
            .Goal("Metered hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(worker)
            .WithTask(task1)
            .WithTask(task2)
            .Build();

        _agents[managerAgent.Id] = managerAgent;
        _agents[worker.Id] = worker;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        var meteredExecution = new TestAgentExecutionService(
            executeFunc: (a, t, ctx, ct) =>
                new TaskResult(
                    Success: true,
                    Output: "metered output",
                    StructuredOutput: null,
                    ToolsUsed: [],
                    ExecutionTime: TimeSpan.FromMilliseconds(10),
                    Error: null,
                    TokensUsed: 80)
                {
                    PromptTokens = 50,
                    CompletionTokens = 30,
                });

        var strategy = new HierarchicalProcessStrategy(
            new MinimalTaskRepository(_tasks),
            new MinimalAgentRepository(_agents),
            _logger,
            _managerAgent,
            meteredExecution,
            _memoryScope);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — measured sums reach the crew metadata under the canonical keys (R10.8);
        // fails on the legacy code, whose hierarchical metadata carried no token entry.
        Assert.True(result.Success);
        Assert.Equal(160, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
        Assert.Equal(100, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey));
        Assert.Equal(60, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey));
    }

    [Fact]
    public async Task ShouldMarkAsNeedsRevision_WhenExecuteHierarchicalAsyncWithRejectedOutput()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var workerAgent = CreateAgent("worker1");
        var task = CreateTask("task1");

        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(workerAgent)
            .WithTask(task)
            .Build();

        _agents[managerAgent.Id] = managerAgent;
        _agents[workerAgent.Id] = workerAgent;
        _tasks[task.Id] = task;

        // Custom manager that rejects all outputs
        var customManager = new TestManagerAgent(
            reviewOutputFunc: (output, task) => false
        );

        var strategy = new HierarchicalProcessStrategy(
            new MinimalTaskRepository(_tasks),
            new MinimalAgentRepository(_agents),
            _logger,
            customManager,
            _executionService,
            _memoryScope);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success); // Overall crew execution is successful
        Assert.Single(result.TaskOutputs);
        var taskOutput = result.TaskOutputs[0];
        Assert.False(taskOutput.Success); // But the task output is marked as failed
        Assert.Contains("[NEEDS REVISION]", taskOutput.Output);
        Assert.True(_logger.HasLoggedWarning("Manager rejected output"));
    }

    [Fact]
    public async Task ShouldSkipAndContinue_WhenExecuteHierarchicalAsyncWithMissingTask()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var workerAgent = CreateAgent("worker1");
        var task1 = CreateTask("task1");
        var missingTaskId = TaskId.Create();

        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(workerAgent)
            .WithTask(task1)
            .Build();
        crew.AddTask(missingTaskId); // This task won't exist in repository

        _agents[managerAgent.Id] = managerAgent;
        _agents[workerAgent.Id] = workerAgent;
        _tasks[task1.Id] = task1;

        // Act
        var result = await _strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs); // Only one task executed
        Assert.True(_logger.HasLoggedWarning($"Task {missingTaskId} not found, skipping"));
    }

    [Fact]
    public async Task ShouldSkipTask_WhenExecuteHierarchicalAsyncWithAssignmentToUnknownAgent()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var workerAgent = CreateAgent("worker1");
        var task = CreateTask("task1");
        var unknownAgentId = AgentId.Create();

        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(workerAgent)
            .WithTask(task)
            .Build();

        _agents[managerAgent.Id] = managerAgent;
        _agents[workerAgent.Id] = workerAgent;
        _tasks[task.Id] = task;

        // Custom manager that assigns to unknown agent
        var customManager = new TestManagerAgent(
            assignTaskFunc: (t, agents, context) => new TaskAssignment(
                TaskId: t.Id,
                AssignedAgent: unknownAgentId,
                Reason: "Assignment to non-existent agent",
                AssignedAt: DateTime.UtcNow
            )
        );

        var strategy = new HierarchicalProcessStrategy(
            new MinimalTaskRepository(_tasks),
            new MinimalAgentRepository(_agents),
            _logger,
            customManager,
            _executionService,
            _memoryScope);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs); // No tasks executed
        Assert.True(_logger.HasLoggedError($"Assigned agent {unknownAgentId} not found"));
    }

    [Fact]
    public async Task ShouldIncludeFailedTask_WhenExecuteHierarchicalAsyncWithExecutionFailure()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var workerAgent = CreateAgent("worker1");
        var task = CreateTask("task1");

        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(workerAgent)
            .WithTask(task)
            .Build();

        _agents[managerAgent.Id] = managerAgent;
        _agents[workerAgent.Id] = workerAgent;
        _tasks[task.Id] = task;

        // Custom execution service that fails
        var customExecutionService = new TestAgentExecutionService(
            executeFunc: (agent, task, context, ct) => new TaskResult(
                Success: false,
                Output: "Execution failed due to error",
                StructuredOutput: null,
                ToolsUsed: [],
                ExecutionTime: TimeSpan.FromSeconds(0.5),
                Error: "Execution error"
            )
        );

        var strategy = new HierarchicalProcessStrategy(
            new MinimalTaskRepository(_tasks),
            new MinimalAgentRepository(_agents),
            _logger,
            _managerAgent,
            customExecutionService,
            _memoryScope);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success); // Overall crew execution is successful
        Assert.Single(result.TaskOutputs);
        var taskOutput = result.TaskOutputs[0];
        Assert.False(taskOutput.Success); // But the task output shows failure
        Assert.Equal("Execution failed due to error", taskOutput.Output);
    }

    [Fact]
    public async Task ShouldUpdateContextBetweenTasks_WhenExecuteHierarchicalAsyncWithMultipleTasks()
    {
        // Arrange
        var managerAgent = CreateAgent("manager");
        var workerAgent = CreateAgent("worker1");
        var task1 = CreateTask("task1");
        var task2 = CreateTask("task2");

        var crew = new CrewBuilder()
            .Goal("Hierarchical crew")
            .Hierarchical(managerAgent)
            .WithAgent(managerAgent)
            .WithAgent(workerAgent)
            .WithTask(task1)
            .WithTask(task2)
            .Build();

        _agents[managerAgent.Id] = managerAgent;
        _agents[workerAgent.Id] = workerAgent;
        _tasks[task1.Id] = task1;
        _tasks[task2.Id] = task2;

        var contextOutputsCount = new List<int>();

        // Custom execution service that checks context
        var customExecutionService = new TestAgentExecutionService(
            executeFunc: (agent, task, context, ct) =>
            {
                // Record how many outputs are in context
                contextOutputsCount.Add(context.PreviousOutputs.Count);

                return new TaskResult(
                    Success: true,
                    Output: $"Task {task.TaskId} executed with {context.PreviousOutputs.Count} previous outputs",
                    StructuredOutput: null,
                    ToolsUsed: [],
                    ExecutionTime: TimeSpan.FromSeconds(1),
                    Error: null
                );
            }
        );

        var strategy = new HierarchicalProcessStrategy(
            new MinimalTaskRepository(_tasks),
            new MinimalAgentRepository(_agents),
            _logger,
            _managerAgent,
            customExecutionService,
            _memoryScope);

        // Act
        var result = await strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);
        Assert.Equal(2, contextOutputsCount.Count);
        Assert.Equal(0, contextOutputsCount[0]); // First task sees 0 previous outputs
        Assert.Equal(1, contextOutputsCount[1]); // Second task sees 1 previous output
    }

    #endregion

    #region ExecuteSequentialAsync Tests

    [Fact]
    public async Task ShouldThrowNotSupported_WhenExecuteSequentialAsync()
    {
        // Arrange
        var crew = CreateSimpleCrew();
        var plan = CrewExecutionPlan.Create();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Sequential execution is not supported", ex.Message);
        Assert.Contains("Use SequentialProcessStrategy", ex.Message);
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

    #region Helper Methods

    private DomainCrew CreateSimpleCrew()
    {
        var defaultManager = CreateAgent("default-manager");
        _agents[defaultManager.Id] = defaultManager;
        return new CrewBuilder()
            .Goal("A simple test crew")
            .Hierarchical(defaultManager)
            .Build();
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

    // Minimal implementation that only implements the methods used by HierarchicalProcessStrategy
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
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
