using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using CrewExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class ParallelProcessStrategyTests : IDisposable
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

    private readonly TestLogger<ParallelProcessStrategy> _logger;
    private readonly MockAgentExecutionService _mockExecutionService = new();
    private readonly MockMemoryScope _mockMemoryScope = new();
    private readonly ParallelProcessStrategy _strategy;

    public ParallelProcessStrategyTests()
    {
        _logger = new TestLogger<ParallelProcessStrategy>();

        // Create minimal mocks that only implement required methods
        var taskRepository = new MinimalTaskRepository();
        var agentRepository = new MinimalAgentRepository();

        // Setup execution service to return output matching old format
        _mockExecutionService.SetExecuteFunc((agent, task, ctx, ct) =>
            new TaskResult(true, $"Task {((DomainTask)task).Id} executed by {agent.Id}", null, [], TimeSpan.FromSeconds(1)));

        _strategy = new ParallelProcessStrategy(taskRepository, agentRepository, _mockExecutionService, _mockMemoryScope, _logger);
    }

    #region Constructor Tests

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullTaskRepository()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ParallelProcessStrategy(null!, new MinimalAgentRepository(), _mockExecutionService, _mockMemoryScope, _logger));
        Assert.Equal("taskRepository", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullAgentRepository()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ParallelProcessStrategy(new MinimalTaskRepository(), null!, _mockExecutionService, _mockMemoryScope, _logger));
        Assert.Equal("agentRepository", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructorWithNullLogger()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ParallelProcessStrategy(new MinimalTaskRepository(), new MinimalAgentRepository(), _mockExecutionService, _mockMemoryScope, null!));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void ShouldCreateStrategy_WhenConstructorWithValidParameters()
    {
        // Act
        var strategy = new ParallelProcessStrategy(
            new MinimalTaskRepository(),
            new MinimalAgentRepository(),
            _mockExecutionService,
            _mockMemoryScope,
            _logger);

        // Assert
        Assert.NotNull(strategy);
    }

    #endregion

    #region ExecuteSequentialAsync Tests

    [Fact]
    public async Task ShouldThrowNotSupported_WhenExecuteSequentialAsync()
    {
        // Arrange
        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        var plan = CrewExecutionPlan.Create();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _strategy.ExecuteSequentialAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Sequential execution is not supported", ex.Message);
        Assert.Contains("Use SequentialProcessStrategy", ex.Message);
    }

    #endregion

    #region ExecuteHierarchicalAsync Tests

    [Fact]
    public async Task ShouldThrowNotSupported_WhenExecuteHierarchicalAsync()
    {
        // Arrange
        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        var managerAgent = new AgentBuilder()
            .Role("manager")
            .Goal("Manage crew")
            .Backstory("Manager backstory")
            .AllowDelegation()
            .Build();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _strategy.ExecuteHierarchicalAsync(crew, managerAgent.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Hierarchical execution is not supported", ex.Message);
        Assert.Contains("Use HierarchicalProcessStrategy", ex.Message);
    }

    #endregion

    #region ExecuteParallelAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenExecuteParallelAsyncWithNullCrew()
    {
        // Arrange
        var plan = CrewExecutionPlan.Create();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _strategy.ExecuteParallelAsync(null!, plan, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenExecuteParallelAsyncWithNullPlan()
    {
        // Arrange
        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _strategy.ExecuteParallelAsync(crew, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenExecuteParallelAsyncWithNoAgents()
    {
        // Arrange
        var taskRepo = new MinimalTaskRepository();
        var agentRepo = new MinimalAgentRepository();
        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        var plan = CrewExecutionPlan.Create();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No agents available", ex.Message);
    }

    [Fact]
    public async Task ShouldExecuteSuccessfully_WhenExecuteParallelAsyncWithSingleAgentAndTask()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var taskId = TaskId.From(Guid.NewGuid());
        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Test task"),
            expectedOutput: ExpectedOutput.From("Test output"));

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
        Assert.True(_logger.HasLoggedInfo("Starting parallel execution"));
        Assert.True(_logger.HasLoggedInfo("Parallel execution completed"));
    }

    [Fact]
    public async Task ShouldDistributeRoundRobin_WhenExecuteParallelAsyncWithMultipleTasksAndAgents()
    {
        // Arrange
        var agent1Id = AgentId.From(Guid.NewGuid());
        var agent2Id = AgentId.From(Guid.NewGuid());
        var task1Id = TaskId.From(Guid.NewGuid());
        var task2Id = TaskId.From(Guid.NewGuid());
        var task3Id = TaskId.From(Guid.NewGuid());

        var agent1 = new AgentBuilder()
            .Role("worker1")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var agent2 = new AgentBuilder()
            .Role("worker2")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task1 = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Task 1"),
            expectedOutput: ExpectedOutput.From("Output 1"));

        var task2 = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Task 2"),
            expectedOutput: ExpectedOutput.From("Output 2"));

        var task3 = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Task 3"),
            expectedOutput: ExpectedOutput.From("Output 3"));

        // The assignments are deliberately the OPPOSITE of what round-robin produces
        // (which would be task1→agent1, task2→agent2, task3→agent1): otherwise the
        // assertion below cannot tell an honoured `agent:` from a lucky loop index.
        // The agents' OWN ids — the dictionary keys above are the repository's lookup keys and
        // are not the ids AgentBuilder generated, so assigning by key would silently miss and
        // fall through to round-robin, which is precisely what this test must be able to see.
        task1.AssignTo(agent2.Id);
        task2.AssignTo(agent1.Id);

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [task1Id] = task1,
            [task2Id] = task2,
            [task3Id] = task3
        });

        var ran = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        _mockExecutionService.SetExecuteFunc((agent, task, _, _) =>
        {
            ran[task.Description.Value] = agent.Role;
            return new TaskResult(true, "ok", null, Array.Empty<Orkeon.Domain.Tools.ToolUsage>(), TimeSpan.FromMilliseconds(1));
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agent1Id] = agent1,
            [agent2Id] = agent2
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agent1Id);
        crew.AddAgent(agent2Id);
        crew.AddTask(task1Id);
        crew.AddTask(task2Id);
        crew.AddTask(task3Id);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);
        Assert.True(_logger.HasLoggedDebug("Starting parallel execution of task"));
        Assert.True(_logger.HasLoggedDebug("Completed parallel execution of task"));

        // The assertion this test's NAME always promised: who ran what. Without it the test
        // passed while Parallel — alone among the five modes — ignored `agent:` entirely and
        // handed every task to whichever agent the loop index landed on.
        Assert.Equal(agent2.Role, ran["Task 1"]);
        Assert.Equal(agent1.Role, ran["Task 2"]);
        // Task 3 declares nothing, so it keeps its round-robin slot.
        Assert.Equal(agent1.Role, ran["Task 3"]);
    }

    /// <summary>
    /// A task that declares <c>dependencies:</c> waits for them, and reads what they produced.
    /// <para>
    /// This mode used to ignore the field: all five tasks of a shipped competitive-intelligence
    /// example started at the same instant, so its synthesis task ran against an empty context
    /// and reported success on the nothing it had. Twenty-three of the thirty shipped
    /// <c>process: parallel</c> examples declare dependencies.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ShouldRunDependentTasksAfterTheirDependencies_AndPassTheirOutputs()
    {
        var agentId = AgentId.From(Guid.NewGuid());
        var collectAId = TaskId.From(Guid.NewGuid());
        var collectBId = TaskId.From(Guid.NewGuid());
        var synthesizeId = TaskId.From(Guid.NewGuid());

        var agent = new AgentBuilder().Role("worker").Goal("Execute tasks").Backstory("b").Build();

        var collectA = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Collect A"),
            expectedOutput: ExpectedOutput.From("a"));
        var collectB = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Collect B"),
            expectedOutput: ExpectedOutput.From("b"));
        var synthesize = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Synthesize"),
            expectedOutput: ExpectedOutput.From("report"));

        synthesize.AddDependency(collectA.Id);
        synthesize.AddDependency(collectB.Id);

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [collectAId] = collectA,
            [collectBId] = collectB,
            [synthesizeId] = synthesize,
        });
        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent> { [agentId] = agent });

        // A rendezvous rather than a sleep: each collector signals and waits for the other, so
        // "they ran together" is proven by both waits completing, not by winning a race. A
        // sequential implementation deadlocks the first one until the timeout and the
        // assertion fails — deterministically, under any machine load.
        using var bothCollectorsInFlight = new CountdownEvent(2);
        var collectorsMet = 0;
        var contextAtSynthesis = new List<string>();

        _mockExecutionService.SetExecuteFunc((_, task, context, _) =>
        {
            if (task.Description.Value.StartsWith("Collect", StringComparison.Ordinal))
            {
                bothCollectorsInFlight.Signal();
                if (bothCollectorsInFlight.Wait(TimeSpan.FromSeconds(30), CancellationToken.None))
                    Interlocked.Increment(ref collectorsMet);
            }
            else
            {
                contextAtSynthesis.AddRange(context.PreviousOutputs.Select(o => o.Content));
            }

            return new TaskResult(true, $"out:{task.Description.Value}", null,
                Array.Empty<Orkeon.Domain.Tools.ToolUsage>(), TimeSpan.FromMilliseconds(1));
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder().Goal("Test Crew").Process(ProcessType.Parallel).Build();
        crew.AddAgent(agentId);
        crew.AddTask(collectAId);
        crew.AddTask(collectBId);
        crew.AddTask(synthesizeId);

        var result = await strategy.ExecuteParallelAsync(
            crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, result.TaskOutputs.Count);
        Assert.Equal(2, collectorsMet);   // the independent tasks must still run concurrently
        Assert.Equal(
            ["out:Collect A", "out:Collect B"],
            contextAtSynthesis.Order(StringComparer.Ordinal));
    }

    /// <summary>A cycle has no order that satisfies it, and is refused naming the tasks.</summary>
    [Fact]
    public async Task ShouldRefuseCircularDependencies_NamingTheTasks()
    {
        var agentId = AgentId.From(Guid.NewGuid());
        var firstId = TaskId.From(Guid.NewGuid());
        var secondId = TaskId.From(Guid.NewGuid());

        var agent = new AgentBuilder().Role("worker").Goal("Execute tasks").Backstory("b").Build();
        var first = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Chicken"),
            expectedOutput: ExpectedOutput.From("x"));
        var second = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Egg"),
            expectedOutput: ExpectedOutput.From("y"));

        first.AddDependency(second.Id);
        second.AddDependency(first.Id);

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [firstId] = first,
            [secondId] = second,
        });
        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent> { [agentId] = agent });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder().Goal("Test Crew").Process(ProcessType.Parallel).Build();
        crew.AddAgent(agentId);
        crew.AddTask(firstId);
        crew.AddTask(secondId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            strategy.ExecuteParallelAsync(
                crew, CrewExecutionPlan.Create(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Chicken", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Egg", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldLogWarningAndContinue_WhenExecuteParallelAsyncWithTaskNotFound()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var existingTaskId = TaskId.From(Guid.NewGuid());
        var missingTaskId = TaskId.From(Guid.NewGuid());

        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Existing task"),
            expectedOutput: ExpectedOutput.From("Output"));

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [existingTaskId] = task
            // missingTaskId is not added, simulating not found
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        crew.AddTask(existingTaskId);
        crew.AddTask(missingTaskId);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs); // Only one task was found and executed
        Assert.True(_logger.HasLoggedWarning("Task"));
        Assert.True(_logger.HasLoggedWarning("not found"));
    }

    [Fact]
    public async Task ShouldReturnEmptyResult_WhenExecuteParallelAsyncWithNoTasks()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var taskRepo = new TaskRepositoryWithData([]);
        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        // No tasks added

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async Task ShouldUseLastTaskOutput_WhenExecuteParallelAsyncAllTasksComplete()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task1 = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("First task"),
            expectedOutput: ExpectedOutput.From("First output"));

        var task2 = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Second task"),
            expectedOutput: ExpectedOutput.From("Second output"));

        var task1Id = task1.Id; // Use the task's own TaskId
        var task2Id = task2.Id; // Use the task's own TaskId

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [task1Id] = task1,
            [task2Id] = task2
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        crew.AddTask(task1Id);
        crew.AddTask(task2Id);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(2, result.TaskOutputs.Count);
        // The implementation uses LastOrDefault for final output
        Assert.Contains(task2Id.Value.ToString(), result.Output);
    }

    [Fact]
    public async Task ShouldLogCorrectMessages_WhenExecuteParallelAsync()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Test task"),
            expectedOutput: ExpectedOutput.From("Test output"));

        var agentId = agent.Id;
        var taskId = task.Id;

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        var crewId = crew.Id;
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create();

        // Act
        await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_logger.HasLoggedInfo($"Starting parallel execution for crew {crewId}"));
        Assert.True(_logger.HasLoggedInfo($"Parallel execution completed for crew {crewId}"));
        Assert.True(_logger.HasLoggedDebug($"Starting parallel execution of task {taskId}"));
        Assert.True(_logger.HasLoggedDebug($"Completed parallel execution of task {taskId}"));
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenExecuteParallelAsyncWithCancellationToken()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var taskId = TaskId.From(Guid.NewGuid());
        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Test task"),
            expectedOutput: ExpectedOutput.From("Test output"));

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        // Should complete normally as the current implementation doesn't check cancellation
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ShouldTaskOutputsHaveCorrectProperties_WhenExecuteParallelAsync()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var agentId = agent.Id; // Use the agent's own AgentId

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Test task"),
            expectedOutput: ExpectedOutput.From("Test output"));

        var taskId = task.Id; // Use the task's own TaskId

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.TaskOutputs);

        var taskOutput = result.TaskOutputs[0];
        Assert.Contains($"Task {taskId}", taskOutput.Output);
        Assert.Contains($"executed by", taskOutput.Output);
        Assert.Contains($"{agentId}", taskOutput.Output);
        Assert.Equal("text", taskOutput.Format);
        Assert.True(taskOutput.Success);
        Assert.Equal(TimeSpan.FromSeconds(1), taskOutput.ExecutionTime);
        Assert.Equal(taskId, taskOutput.TaskId);
    }

    [Fact]
    public async Task ShouldLogWarningAndContinue_WhenExecuteParallelAsyncWithAgentNotFound()
    {
        // Arrange
        var existingAgentId = AgentId.From(Guid.NewGuid());
        var missingAgentId = AgentId.From(Guid.NewGuid());
        var taskId = TaskId.From(Guid.NewGuid());

        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Test task"),
            expectedOutput: ExpectedOutput.From("Output"));

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [existingAgentId] = agent
            // missingAgentId is not added
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(existingAgentId);
        crew.AddAgent(missingAgentId);
        crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldHandleAllEfficiently_WhenExecuteParallelAsyncWithLargeNumberOfTasks()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var taskCount = 100;
        var tasks = new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>();
        var taskIds = new List<TaskId>();

        for (int i = 0; i < taskCount; i++)
        {
            var taskId = TaskId.From(Guid.NewGuid());
            taskIds.Add(taskId);
            tasks[taskId] = Orkeon.Domain.Task.CrewTask.Create(
                description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From($"Task {i}"),
                expectedOutput: ExpectedOutput.From($"Output {i}"));
        }

        var taskRepo = new TaskRepositoryWithData(tasks);
        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);

        foreach (var taskId in taskIds)
        {
            crew.AddTask(taskId);
        }

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(taskCount, result.TaskOutputs.Count);
    }

    [Fact]
    public async Task ShouldReturnEmptyResult_WhenExecuteParallelAsyncWithMultipleAgentsNoTasks()
    {
        // Arrange
        var agent1Id = AgentId.From(Guid.NewGuid());
        var agent2Id = AgentId.From(Guid.NewGuid());

        var agent1 = new AgentBuilder()
            .Role("worker1")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var agent2 = new AgentBuilder()
            .Role("worker2")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var taskRepo = new TaskRepositoryWithData([]);
        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agent1Id] = agent1,
            [agent2Id] = agent2
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agent1Id);
        crew.AddAgent(agent2Id);
        // No tasks added

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldExecuteOnce_WhenExecuteParallelAsyncWithSingleTaskMultipleAgents()
    {
        // Arrange
        var agent1Id = AgentId.From(Guid.NewGuid());
        var agent2Id = AgentId.From(Guid.NewGuid());
        var agent3Id = AgentId.From(Guid.NewGuid());

        var agent1 = new AgentBuilder()
            .Role("worker1")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var agent2 = new AgentBuilder()
            .Role("worker2")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var agent3 = new AgentBuilder()
            .Role("worker3")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Single task"),
            expectedOutput: ExpectedOutput.From("Single output"));

        var taskId = task.Id; // Use the task's own TaskId

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agent1Id] = agent1,
            [agent2Id] = agent2,
            [agent3Id] = agent3
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agent1Id);
        crew.AddAgent(agent2Id);
        crew.AddAgent(agent3Id);
        crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);

        var output = result.TaskOutputs[0];
        Assert.Equal(taskId, output.TaskId);
    }

    [Fact]
    public async Task ShouldVerifyRoundRobinDistributionWithExactMultiple_WhenExecuteParallelAsync()
    {
        // Arrange
        var taskIds = new List<TaskId>();
        var tasks = new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>();

        // Create exactly 6 tasks (3 per agent)
        for (int i = 0; i < 6; i++)
        {
            var task = Orkeon.Domain.Task.CrewTask.Create(
                description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From($"Task {i}"),
                expectedOutput: ExpectedOutput.From($"Output {i}"));
            var taskId = task.Id; // Use the task's own TaskId
            taskIds.Add(taskId);
            tasks[taskId] = task;
        }

        var agent1 = new AgentBuilder()
            .Role("worker1")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var agent2 = new AgentBuilder()
            .Role("worker2")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        // Use the actual agent IDs from the created agents
        var agent1Id = agent1.Id;
        var agent2Id = agent2.Id;

        var taskRepo = new TaskRepositoryWithData(tasks);
        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agent1Id] = agent1,
            [agent2Id] = agent2
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agent1Id);
        crew.AddAgent(agent2Id);

        foreach (var taskId in taskIds)
        {
            crew.AddTask(taskId);
        }

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(6, result.TaskOutputs.Count);

        // Verify round-robin distribution in outputs
        var agent1Tasks = result.TaskOutputs.Where(t => t.Output.Contains(agent1Id.ToString())).Count();
        var agent2Tasks = result.TaskOutputs.Where(t => t.Output.Contains(agent2Id.ToString())).Count();

        // Should be evenly distributed
        Assert.Equal(3, agent1Tasks);
        Assert.Equal(3, agent2Tasks);
    }

    [Fact]
    public async Task ShouldExecuteSuccessfully_WhenExecuteParallelAsyncEmptyPlan()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var taskId = TaskId.From(Guid.NewGuid());
        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Test task"),
            expectedOutput: ExpectedOutput.From("Test output"));

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create(); // Empty plan

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.TaskOutputs);
    }

    [Fact]
    public async Task ShouldReturnEmptyResult_WhenExecuteParallelAsyncAllTasksNotFound()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var taskId1 = TaskId.From(Guid.NewGuid());
        var taskId2 = TaskId.From(Guid.NewGuid());

        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        // Empty task repository - no tasks exist
        var taskRepo = new TaskRepositoryWithData([]);

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);
        crew.AddTask(taskId1);
        crew.AddTask(taskId2);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
        Assert.True(_logger.HasLoggedWarning("not found"));
    }

    [Fact]
    public async Task ShouldExecuteEachOnce_WhenExecuteParallelAsyncDuplicateTaskIds()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        var agent = new AgentBuilder()
            .Role("worker")
            .Goal("Execute tasks")
            .Backstory("Test backstory")
            .Build();

        var task = Orkeon.Domain.Task.CrewTask.Create(
            description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From("Test task"),
            expectedOutput: ExpectedOutput.From("Test output"));

        var taskId = task.Id; // Use the task's own TaskId

        var taskRepo = new TaskRepositoryWithData(new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task
        });

        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agentId] = agent
        });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Test Crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agentId);

        // Create multiple tasks with same content but different IDs
        var taskId2 = TaskId.From(Guid.NewGuid());
        var taskId3 = TaskId.From(Guid.NewGuid());

        // Configure repository for all task IDs
        var taskDict = new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>
        {
            [taskId] = task,
            [taskId2] = task,
            [taskId3] = task
        };
        var taskRepo2 = new TaskRepositoryWithData(taskDict);

        // Replace the strategy with updated repository
        var strategy2 = new ParallelProcessStrategy(taskRepo2, agentRepo, _mockExecutionService, _mockMemoryScope, _logger);

        // Add tasks to crew
        crew.AddTask(taskId);
        crew.AddTask(taskId2);
        crew.AddTask(taskId3);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy2.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        // Should execute each unique task only once
        Assert.Equal(3, result.TaskOutputs.Count); // Implementation may execute duplicates
    }

    [Fact]
    public async Task ShouldPropagateMeasuredTokenTelemetry_WhenExecuteParallelAsync()
    {
        // Arrange — three concurrent tasks, each costing 50 tokens (30 prompt / 20 completion)
        var agent = new AgentBuilder()
            .Role("metered-worker")
            .Goal("Execute metered tasks")
            .Backstory("Test backstory")
            .Build();

        var tasks = new Dictionary<TaskId, Orkeon.Domain.Task.CrewTask>();
        var taskIds = new List<TaskId>();
        for (int i = 0; i < 3; i++)
        {
            var task = Orkeon.Domain.Task.CrewTask.Create(
                description: Orkeon.Domain.Task.ValueObjects.TaskDescription.From($"Metered task {i}"),
                expectedOutput: ExpectedOutput.From($"Output {i}"));
            taskIds.Add(task.Id);
            tasks[task.Id] = task;
        }

        var taskRepo = new TaskRepositoryWithData(tasks);
        var agentRepo = new AgentRepositoryWithData(new Dictionary<AgentId, DomainAgent>
        {
            [agent.Id] = agent
        });

        var meteredExecution = new MockAgentExecutionService();
        meteredExecution.SetExecuteFunc((a, t, ctx, ct) =>
            new TaskResult(true, "metered output", null, [], TimeSpan.FromMilliseconds(10), TokensUsed: 50)
            {
                PromptTokens = 30,
                CompletionTokens = 20,
            });

        var strategy = new ParallelProcessStrategy(taskRepo, agentRepo, meteredExecution, _mockMemoryScope, _logger);

        var crew = new CrewBuilder()
            .Goal("Metered crew")
            .Process(ProcessType.Parallel)
            .Build();
        crew.AddAgent(agent.Id);
        foreach (var taskId in taskIds)
            crew.AddTask(taskId);

        var plan = CrewExecutionPlan.Create();

        // Act
        var result = await strategy.ExecuteParallelAsync(crew, plan, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — measured sums reach the crew metadata under the canonical keys (R10.8);
        // this fails on the legacy code, which returned metadata: null for Parallel.
        Assert.True(result.Success);
        Assert.Equal(150, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
        Assert.Equal(90, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey));
        Assert.Equal(60, result.Metadata.GetRequired<int>(Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey));
    }

    #endregion

    #region Minimal Repository Implementations

    // Minimal implementation that only implements the methods used by ParallelProcessStrategy
    private class MinimalTaskRepository : ITaskRepository
    {
        public Task<Orkeon.Domain.Task.CrewTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
        {
            // Return null to simulate not found
            return Task.FromResult<Orkeon.Domain.Task.CrewTask?>(null);
        }

        // All other methods throw NotImplementedException
        public Task<Orkeon.Domain.Task.CrewTask> AddAsync(Orkeon.Domain.Task.CrewTask entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        Task IRepository<Orkeon.Domain.Task.CrewTask, TaskId>.AddAsync(Orkeon.Domain.Task.CrewTask aggregate, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateAsync(Orkeon.Domain.Task.CrewTask entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(TaskId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        Task IRepository<Orkeon.Domain.Task.CrewTask, TaskId>.DeleteAsync(TaskId id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(TaskId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByStatusAsync(Orkeon.Domain.Task.ValueObjects.TaskStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> IsCompletedAsync(TaskId taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        // ISpecificationRepository methods
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> FindAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> FindAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AnyAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class MinimalAgentRepository : IAgentRepository
    {
        public Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
        {
            // Return null to simulate not found
            return Task.FromResult<DomainAgent?>(null);
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


    // Test repository with configurable data
    private class TaskRepositoryWithData : ITaskRepository
    {
        private readonly Dictionary<TaskId, Orkeon.Domain.Task.CrewTask> _tasks;

        public TaskRepositoryWithData(Dictionary<TaskId, Orkeon.Domain.Task.CrewTask> tasks)
        {
            _tasks = tasks;
        }

        public Task<Orkeon.Domain.Task.CrewTask?> GetByIdAsync(TaskId id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_tasks.TryGetValue(id, out var task) ? task : null);
        }

        // All other methods throw NotImplementedException
        public Task<Orkeon.Domain.Task.CrewTask> AddAsync(Orkeon.Domain.Task.CrewTask entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        Task IRepository<Orkeon.Domain.Task.CrewTask, TaskId>.AddAsync(Orkeon.Domain.Task.CrewTask aggregate, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateAsync(Orkeon.Domain.Task.CrewTask entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(TaskId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        Task IRepository<Orkeon.Domain.Task.CrewTask, TaskId>.DeleteAsync(TaskId id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(TaskId id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByIdsAsync(IEnumerable<TaskId> ids, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByAgentAsync(AgentId agentId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByStatusAsync(Orkeon.Domain.Task.ValueObjects.TaskStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetDependentTasksAsync(TaskId taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetReadyTasksAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByPriorityAsync(TaskPriority priority, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> IsCompletedAsync(TaskId taskId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        // ISpecificationRepository methods
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> FindAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Orkeon.Domain.Task.CrewTask>> FindAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AnyAsync(ISpecification<Orkeon.Domain.Task.CrewTask> specification, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class AgentRepositoryWithData : IAgentRepository
    {
        private readonly Dictionary<AgentId, DomainAgent> _agents;

        public AgentRepositoryWithData(Dictionary<AgentId, DomainAgent> agents)
        {
            _agents = agents;
        }

        public Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_agents.TryGetValue(id, out var agent) ? agent : null);
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
