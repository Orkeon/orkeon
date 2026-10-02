using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Memory;
using Orkeon.Application.Agent;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Context;
using Orkeon.Application.Callback;
using Orkeon.Application.Memory;
using Orkeon.Application.Tests.Doubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using TaskExecutionPlan = Orkeon.Application.Interfaces.Services.TaskExecutionPlan;
using PlannedStep = Orkeon.Application.Interfaces.Services.PlannedStep;
using ValidationResult = Orkeon.Application.Interfaces.Services.ValidationResult;
using ToolUsage = Orkeon.Domain.Tools.ToolUsage;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services;

public sealed class AgentExecutionServiceTests : IDisposable
{
    private readonly Fixtures.TestLogger<AgentExecutionService> _logger;
    private readonly TestExecutionOrchestrator _executionOrchestrator;
    private readonly TestCallbackOrchestrator _callbackOrchestrator;
    private readonly TestMemoryCoordinator _memoryCoordinator;
    private readonly TestPerformanceMetrics _performanceMetrics;
    private readonly AgentExecutionService _service;
    private readonly DomainAgent _testAgent;
    private readonly DomainTask _testTask;
    private readonly SimpleExecutionContext _context;
    private readonly TestMemoryScope _memoryScope;

    public AgentExecutionServiceTests()
    {
        _logger = new Fixtures.TestLogger<AgentExecutionService>();
        _executionOrchestrator = new TestExecutionOrchestrator();
        _callbackOrchestrator = new TestCallbackOrchestrator();
        _memoryCoordinator = new TestMemoryCoordinator();
        _performanceMetrics = new TestPerformanceMetrics();

        _service = new AgentExecutionService(
            _logger,
            _executionOrchestrator,
            _callbackOrchestrator,
            _memoryCoordinator,
            _performanceMetrics);

        _testAgent = DomainAgent.Create(
            AgentRole.From("Test Agent"),
            AgentGoal.From(TestGoal),
            AgentBackstory.From("Test backstory"));

        _testTask = DomainTask.Create(
            TaskDescription.From("Test task"),
            ExpectedOutput.From("Expected output"));

        _memoryScope = new TestMemoryScope();
        _context = new SimpleExecutionContext(
            CrewId: CrewId.From(Guid.NewGuid()),
            Variables: new Dictionary<string, string> { ["test"] = "value" },
            Memory: _memoryScope,
            PreviousOutputs: [],
            CancellationToken: CancellationToken.None);
    }

    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        _memoryScope.Dispose();
        foreach (var disposable in _disposables)
            disposable.Dispose();
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AgentExecutionService(
                null!,
                _executionOrchestrator,
                _callbackOrchestrator,
                _memoryCoordinator));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullExecutionOrchestrator()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AgentExecutionService(
                _logger,
                null!,
                _callbackOrchestrator,
                _memoryCoordinator));
        Assert.Equal("executionOrchestrator", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullCallbackOrchestrator()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AgentExecutionService(
                _logger,
                _executionOrchestrator,
                null!,
                _memoryCoordinator));
        Assert.Equal("callbackOrchestrator", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullMemoryCoordinator()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AgentExecutionService(
                _logger,
                _executionOrchestrator,
                _callbackOrchestrator,
                null!));
        Assert.Equal("memoryCoordinator", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnSuccessResult_WhenExecutingTaskAsyncWithSuccessfulExecution()
    {
        // Arrange
        var expectedResult = new TaskResult(
            Success: true,
            Output: "Test output",
            StructuredOutput: null,
            ToolsUsed: [new ToolUsage(new ToolCallIdentity("tool-1", "TestTool", AgentId1, TaskId1), TimeSpan.FromSeconds(0.5), true)],
            ExecutionTime: TimeSpan.FromSeconds(1),
            Error: null);

        _executionOrchestrator.SetupResult(expectedResult);

        // Act
        var result = await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test output", result.Output);
        Assert.Single(result.ToolsUsed);
        Assert.Equal("TestTool", result.ToolsUsed[0].ToolName);

        // Verify callbacks were called
        Assert.True(_callbackOrchestrator.TaskStartedCalled);
        Assert.True(_callbackOrchestrator.TaskCompletedCalled);
        Assert.Equal(_testAgent.Id, _callbackOrchestrator.LastAgent?.Id);
        Assert.Equal(_testTask.Id, _callbackOrchestrator.LastTask?.Id);

        // Verify memory was stored
        Assert.True(_memoryCoordinator.TaskResultStored);
        Assert.Equal("Test output", _memoryCoordinator.LastStoredResult);

        // Verify metrics were recorded
        Assert.True(_performanceMetrics.TaskExecutionRecorded);
        Assert.True(_performanceMetrics.LastSuccess);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_run_whose_context_stores_nothing_succeeds_without_a_memory()
    {
        // GAP-20: a consensual candidate or a ballot is not the task's result — the strategy
        // runs it with StoreResultInMemory off, and the service must honour it.
        _executionOrchestrator.SetupResult(new TaskResult(true, "candidate answer", null, [], TimeSpan.Zero));

        var result = await _service.ExecuteTaskAsync(
            _testAgent, _testTask, _context with { StoreResultInMemory = false }, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("candidate answer", result.Output);
        Assert.False(_memoryCoordinator.TaskResultStored);
    }

    [Fact]
    public void A_context_stores_the_result_by_default()
    {
        Assert.True(_context.StoreResultInMemory);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_steps_a_completed_task_reports_are_the_iterations_its_agent_loop_ran()
    {
        // GAP-21: StepsExecuted, handed to every ICallbackHandler and logged, used to be 1 whatever
        // the loop did.
        _executionOrchestrator.SetupResult(new TaskResult(true, "after four turns", null, [], TimeSpan.Zero) { IterationsUsed = 4 });

        await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        Assert.Equal(4, _callbackOrchestrator.LastCompletionInfo?.StepsExecuted);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_failed_loop_reports_the_iterations_it_ran_before_failing()
    {
        _executionOrchestrator.SetupResult(new TaskResult(false, "", null, [], TimeSpan.Zero, Error: "no final answer")
        {
            ExitReason = AgentExitReason.MaxIterationsReached,
            IterationsUsed = 3,
        });

        await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        Assert.Equal(3, _callbackOrchestrator.LastCompletionInfo?.StepsExecuted);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnErrorResult_WhenExecutingTaskAsyncWithFailedExecution()
    {
        // Arrange
        _executionOrchestrator.ThrowException(new InvalidOperationException("Test error"));

        // Act
        var result = await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Empty(result.Output);
        Assert.Equal("Test error", result.Error);

        // Verify callbacks were called
        Assert.True(_callbackOrchestrator.TaskStartedCalled);
        Assert.True(_callbackOrchestrator.TaskCompletedCalled);
        Assert.False(_callbackOrchestrator.LastResult!.Success);

        // Verify memory was NOT stored (since it failed)
        Assert.False(_memoryCoordinator.TaskResultStored);

        // Verify error was logged
        Assert.True(_logger.HasLoggedError());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenExecutingTaskAsyncWithCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _executionOrchestrator.SimulateDelay(TimeSpan.FromSeconds(5));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.ExecuteTaskAsync(_testAgent, _testTask, _context, cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnTypedResult_WhenExecutingTaskAsyncWithGenericWithStructuredOutput()
    {
        // Arrange
        var structuredOutput = new TestOutput { Value = "Structured data", Count = 42 };
        var expectedResult = new TaskResult(
            Success: true,
            Output: "Raw output",
            StructuredOutput: structuredOutput,
            ToolsUsed: [],
            ExecutionTime: TimeSpan.FromSeconds(1),
            Error: null);

        _executionOrchestrator.SetupResult(expectedResult);

        // Act
        var result = await _service.ExecuteTaskAsync<TestOutput>(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Raw output", result.RawOutput);
        Assert.NotNull(result.StructuredOutput);
        Assert.Equal("Structured data", result.StructuredOutput.Value);
        Assert.Equal(42, result.StructuredOutput.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDelegateToExecutionOrchestrator_WhenPlanningTaskExecutionAsync()
    {
        // Arrange
        var expectedPlan = new TaskExecutionPlan(
            AssignedAgent: _testAgent.Id,
            Steps:
            [
                new PlannedStep("Step 1: First step", null, null),
                new PlannedStep("Step 2: Second step", null, null)
            ],
            EstimatedDuration: TimeoutStandard,
            ConfidenceScore: 0.9);

        _executionOrchestrator.SetupPlan(expectedPlan);

        // Act
        var plan = await _service.PlanTaskExecutionAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedPlan, plan);
        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal("Step 1: First step", plan.Steps[0].Description);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnTrue_WhenUsingCanExecuteTaskAsyncWhenCanExecute()
    {
        // Arrange
        _executionOrchestrator.SetupValidation(new ExecutionValidation
        {
            CanExecute = true,
            Reasons = []
        });

        // Act
        var canExecute = await _service.CanExecuteTaskAsync(_testAgent, _testTask, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(canExecute);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFalse_WhenUsingCanExecuteTaskAsyncWhenCannotExecute()
    {
        // Arrange
        _executionOrchestrator.SetupValidation(new ExecutionValidation
        {
            CanExecute = false,
            Reasons = ["Agent lacks required tools"]
        });

        // Act
        var canExecute = await _service.CanExecuteTaskAsync(_testAgent, _testTask, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(canExecute);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenExecutingTaskAsyncWithNullAgent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ExecuteTaskAsync(null!, _testTask, _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenExecutingTaskAsyncWithNullTask()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ExecuteTaskAsync(_testAgent, null!, _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenExecutingTaskAsyncWithNullContext()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ExecuteTaskAsync(_testAgent, _testTask, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillRecordMetrics_WhenExecutingTaskAsyncWithEmptyOutput()
    {
        // Arrange
        var expectedResult = new TaskResult(
            Success: true,
            Output: string.Empty,
            StructuredOutput: null,
            ToolsUsed: [],
            ExecutionTime: TimeSpan.FromSeconds(0.5),
            Error: null);

        _executionOrchestrator.SetupResult(expectedResult);

        // Act
        var result = await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Output);

        // Verify metrics were still recorded even with empty output
        Assert.True(_performanceMetrics.TaskExecutionRecorded);
        Assert.True(_performanceMetrics.LastSuccess);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnAllTools_WhenExecutingTaskAsyncWithMultipleToolsUsed()
    {
        // Arrange
        var toolsUsed = new List<ToolUsage>
        {
            new ToolUsage(new ToolCallIdentity("tool-1", "Tool1", AgentId1, TaskId1), TimeSpan.FromSeconds(0.5), true),
            new ToolUsage(new ToolCallIdentity("tool-2", "Tool2", AgentId1, TaskId1), TimeSpan.FromSeconds(0.3), true),
            new ToolUsage(new ToolCallIdentity("tool-3", "Tool3", AgentId1, TaskId1), TimeSpan.FromSeconds(0.2), false)
        };

        var expectedResult = new TaskResult(
            Success: true,
            Output: "Multi-tool output",
            StructuredOutput: null,
            ToolsUsed: toolsUsed,
            ExecutionTime: TimeSpan.FromSeconds(2),
            Error: null);

        _executionOrchestrator.SetupResult(expectedResult);

        // Act
        var result = await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.ToolsUsed.Count);
        Assert.Contains(result.ToolsUsed, t => t.ToolName == "Tool1" && t.Success);
        Assert.Contains(result.ToolsUsed, t => t.ToolName == "Tool2" && t.Success);
        Assert.Contains(result.ToolsUsed, t => t.ToolName == "Tool3" && !t.Success);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRecordAccurately_WhenExecutingTaskAsyncWithLongExecutionTime()
    {
        // Arrange
        var expectedResult = new TaskResult(
            Success: true,
            Output: "Long running task output",
            StructuredOutput: null,
            ToolsUsed: [],
            ExecutionTime: TimeoutStandard,
            Error: null);

        _executionOrchestrator.SetupResult(expectedResult);

        // Act
        var result = await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(TimeoutStandard, result.ExecutionTime);
        Assert.True(_performanceMetrics.TaskExecutionRecorded);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenPlanningTaskExecutionAsyncWithNullAgent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.PlanTaskExecutionAsync(null!, _testTask, _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenPlanningTaskExecutionAsyncWithNullTask()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.PlanTaskExecutionAsync(_testAgent, null!, _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyPlan_WhenPlanningTaskExecutionAsyncWithEmptySteps()
    {
        // Arrange
        var expectedPlan = new TaskExecutionPlan(
            AssignedAgent: _testAgent.Id,
            Steps: [],
            EstimatedDuration: TimeSpan.Zero,
            ConfidenceScore: 0.5);

        _executionOrchestrator.SetupPlan(expectedPlan);

        // Act
        var plan = await _service.PlanTaskExecutionAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(plan.Steps);
        Assert.Equal(TimeSpan.Zero, plan.EstimatedDuration);
        Assert.Equal(0.5, plan.ConfidenceScore);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenUsingCanExecuteTaskAsyncWithNullAgent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.CanExecuteTaskAsync(null!, _testTask, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenUsingCanExecuteTaskAsyncWithNullTask()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.CanExecuteTaskAsync(_testAgent, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenExecutingTaskAsyncWithPartialSuccess()
    {
        // Arrange
        var expectedResult = new TaskResult(
            Success: true,
            Output: "Partial output",
            StructuredOutput: null,
            ToolsUsed:
            [
                new ToolUsage(new ToolCallIdentity("tool-1", "Tool1", AgentId1, TaskId1), TimeSpan.FromSeconds(1), true),
                new ToolUsage(new ToolCallIdentity("tool-2", "Tool2", AgentId1, TaskId1), TimeSpan.FromSeconds(0.5), false)
            ],
            ExecutionTime: TimeSpan.FromSeconds(2),
            Error: "Partial errors occurred");

        _executionOrchestrator.SetupResult(expectedResult);

        // Act
        var result = await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success); // Overall success despite partial failures
        Assert.Equal("Partial output", result.Output);
        Assert.Equal("Partial errors occurred", result.Error);
        Assert.Equal(2, result.ToolsUsed.Count);
        Assert.Equal(1, result.ToolsUsed.Count(t => t.Success));
        Assert.Equal(1, result.ToolsUsed.Count(t => !t.Success));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnDefault_WhenExecutingTaskAsyncWithGenericWithNullStructuredOutput()
    {
        // Arrange
        var expectedResult = new TaskResult(
            Success: true,
            Output: "Raw output only",
            StructuredOutput: null,
            ToolsUsed: [],
            ExecutionTime: TimeSpan.FromSeconds(1),
            Error: null);

        _executionOrchestrator.SetupResult(expectedResult);

        // Act
        var result = await _service.ExecuteTaskAsync<TestOutput>(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Raw output only", result.RawOutput);
        Assert.Null(result.StructuredOutput);
    }

    /// <summary>
    /// The service over the real <see cref="MemoryCoordinator"/> and memory (GAP-30): the crew of
    /// <c>_context</c> remembers, and its store is <paramref name="store"/>. Returns the coordinator's
    /// logger, which carries the warnings.
    /// </summary>
    private (AgentExecutionService Service, Fixtures.TestLogger<MemoryCoordinator> MemoryLog) WithRealMemory(
        Orkeon.Domain.Memory.IMemoryProvider store)
    {
        var registry = new CrewMemoryProviderRegistry();
        registry.Record(_context.CrewId, providerType: null, "news-desk", memoryEnabled: true);
        var embedder = new MockEmbeddingProvider();
        embedder.SetEmbeddingFunc(LexicalVectors.Of);
        var memory = new MemoryService(new StubMemoryProviderFactory(store), NullLogger<MemoryService>.Instance, registry, store);
        _disposables.Add(memory);
        var memoryLog = new Fixtures.TestLogger<MemoryCoordinator>();
        var coordinator = new MemoryCoordinator(memoryLog, memory, registry, embedder);
        return (new AgentExecutionService(_logger, _executionOrchestrator, _callbackOrchestrator, coordinator, _performanceMetrics), memoryLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_store_that_fails_leaves_the_task_succeeded_with_its_output_and_a_warning()
    {
        // GAP-30: the output is the deliverable, already paid for. A memory store that is down used
        // to fail the task — and, since GAP-03, the crew and every task depending on it.
        _executionOrchestrator.SetupResult(new TaskResult(
            Success: true,
            Output: "Test output",
            StructuredOutput: null,
            ToolsUsed: [],
            ExecutionTime: TimeSpan.FromSeconds(1),
            Error: null));
        var (service, memoryLog) = WithRealMemory(
            new ThrowingMemoryProvider(new InvalidOperationException("Memory error"), searches: false));

        var result = await service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal("Test output", result.Output);
        Assert.Contains(memoryLog.LogEntries, e => e.LogLevel == LogLevel.Warning && (e.Message ?? "").Contains("Memory error", StringComparison.Ordinal));
        Assert.False(_logger.HasLoggedError());
    }

    [Fact]
    public async System.Threading.Tasks.Task A_recall_that_fails_runs_the_task_without_memories_and_warns()
    {
        var (service, memoryLog) = WithRealMemory(
            new ThrowingMemoryProvider(new InvalidOperationException("search is down"), stores: false));

        var result = await service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Empty(_executionOrchestrator.LastContext!.RecalledMemories);
        Assert.Contains(memoryLog.LogEntries, e => e.LogLevel == LogLevel.Warning && (e.Message ?? "").Contains("search is down", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task The_recalled_memories_reach_the_execution_on_its_own_copy_of_the_context()
    {
        _memoryCoordinator.Recalled = [new RecalledMemory(DateTime.UtcNow, "Analyst", "An earlier task", "An earlier output")];

        await _service.ExecuteTaskAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        Assert.Equal(1, _memoryCoordinator.RecallCount);
        Assert.Equal("An earlier output", Assert.Single(_executionOrchestrator.LastContext!.RecalledMemories).Content);
        Assert.Empty(_context.RecalledMemories);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_context_that_does_not_recall_asks_the_memory_for_nothing()
    {
        _memoryCoordinator.Recalled = [new RecalledMemory(DateTime.UtcNow, "Analyst", "An earlier task", "An earlier output")];

        await _service.ExecuteTaskAsync(
            _testAgent, _testTask, _context with { RecallFromMemory = false }, TestContext.Current.CancellationToken);

        Assert.Equal(0, _memoryCoordinator.RecallCount);
        Assert.Empty(_executionOrchestrator.LastContext!.RecalledMemories);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnDetailedPlan_WhenPlanningTaskExecutionAsyncWithComplexSteps()
    {
        // Arrange
        var expectedPlan = new TaskExecutionPlan(
            AssignedAgent: _testAgent.Id,
            Steps:
            [
                new PlannedStep("Research phase", "Tool1", null),
                new PlannedStep("Analysis phase", "Tool3", null),
                new PlannedStep("Synthesis phase", null, null)
            ],
            EstimatedDuration: TimeSpan.FromHours(2),
            ConfidenceScore: 0.85);

        _executionOrchestrator.SetupPlan(expectedPlan);

        // Act
        var plan = await _service.PlanTaskExecutionAsync(_testAgent, _testTask, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, plan.Steps.Count);
        Assert.Equal("Research phase", plan.Steps[0].Description);
        Assert.Equal("Tool1", plan.Steps[0].ToolName);
        Assert.Null(plan.Steps[0].ToolParameters);
        Assert.Equal(TimeSpan.FromHours(2), plan.EstimatedDuration);
        Assert.Equal(0.85, plan.ConfidenceScore);
    }
}

// Test doubles for dependencies
internal class TestExecutionOrchestrator : IExecutionOrchestrator
{
    private TaskResult? _result;
    private TaskExecutionPlan? _plan;
    private ExecutionValidation? _validation;
    private Exception? _exception;
    private TimeSpan _delay = TimeSpan.Zero;

    public void SetupResult(TaskResult result) => _result = result;
    public void SetupPlan(TaskExecutionPlan plan) => _plan = plan;
    public void SetupValidation(ExecutionValidation validation) => _validation = validation;
    public void ThrowException(Exception exception) => _exception = exception;
    public void SimulateDelay(TimeSpan delay) => _delay = delay;

    /// <summary>The context of the last execution: what the agent's prompt was built from.</summary>
    public SimpleExecutionContext? LastContext { get; private set; }

    public async System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreAsync(
        DomainAgent agent, DomainTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
    {
        LastContext = context;
        if (_delay > TimeSpan.Zero)
        {
            await System.Threading.Tasks.Task.Delay(_delay, cancellationToken);
        }

        if (_exception != null)
        {
            throw _exception;
        }

        return _result ?? new TaskResult(
            Success: true,
            Output: "Default output",
            StructuredOutput: null,
            ToolsUsed: [],
            ExecutionTime: TimeSpan.FromSeconds(1),
            Error: null);
    }

    public System.Threading.Tasks.Task<TaskExecutionPlan> PlanExecutionAsync(
        DomainAgent agent, DomainTask task, SimpleExecutionContext context, CancellationToken cancellationToken)
    {
        return System.Threading.Tasks.Task.FromResult(_plan ?? new TaskExecutionPlan(
            AssignedAgent: agent.Id,
            Steps: [],
            EstimatedDuration: TimeSpan.Zero,
            ConfidenceScore: 1.0));
    }

    public System.Threading.Tasks.Task<ValidationResult> ValidateExecutionAsync(
        DomainAgent agent, DomainTask task, CancellationToken cancellationToken)
    {
        return System.Threading.Tasks.Task.FromResult(new ValidationResult(
            CanExecute: _validation?.CanExecute ?? true,
            Reason: _validation?.Reasons?.FirstOrDefault()));
    }

    public Domain.Task.ValueObjects.SimpleTaskExecutionContext MapExecutionContext(
        SimpleExecutionContext applicationContext, DomainAgent agent)
    {
        return Domain.Task.ValueObjects.SimpleTaskExecutionContext.Create(
            variables: applicationContext.Variables ?? [],
            previousOutputs: [],
            memory: null,
            availableAgents: null);
    }
}

internal class TestCallbackOrchestrator : ICallbackOrchestrator
{
    public bool TaskStartedCalled { get; private set; }
    public bool TaskCompletedCalled { get; private set; }
    public DomainAgent? LastAgent { get; private set; }
    public DomainTask? LastTask { get; private set; }
    public TaskResult? LastResult { get; private set; }
    public TaskCompletionInfo? LastCompletionInfo { get; private set; }

    public System.Threading.Tasks.Task NotifyTaskStartedAsync(
        DomainAgent agent, DomainTask task, DateTime startTime,
        CancellationToken cancellationToken = default)
    {
        TaskStartedCalled = true;
        LastAgent = agent;
        LastTask = task;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task NotifyTaskCompletedAsync(
        DomainAgent agent, DomainTask task, TaskCompletionInfo completionInfo,
        CancellationToken cancellationToken = default)
    {
        TaskCompletedCalled = true;
        LastAgent = agent;
        LastTask = task;
        LastResult = completionInfo.Result;
        LastCompletionInfo = completionInfo;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task NotifyStepStartedAsync(
        StepStartedContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task NotifyStepCompletedAsync(
        StepCompletedContext context, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;
}

internal class TestMemoryCoordinator : IMemoryCoordinator
{
    public bool TaskResultStored { get; private set; }
    public string? LastStoredResult { get; private set; }

    /// <summary>How many times a task asked to recall.</summary>
    public int RecallCount { get; private set; }

    /// <summary>What a recall returns.</summary>
    public IReadOnlyList<RecalledMemory> Recalled { get; set; } = [];

    public System.Threading.Tasks.Task EnsureReadyAsync(CrewId crewId, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        DomainAgent agent, DomainTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        RecallCount++;
        return System.Threading.Tasks.Task.FromResult(Recalled);
    }

    public System.Threading.Tasks.Task StoreTaskResultAsync(
        DomainAgent agent, DomainTask task, string output,
        SimpleExecutionContext context, CancellationToken cancellationToken)
    {
        TaskResultStored = true;
        LastStoredResult = output;
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

internal class TestPerformanceMetrics : IPerformanceMetrics
{
    public bool TaskExecutionRecorded { get; private set; }
    public bool LastSuccess { get; private set; }

    public void RecordTaskExecution(string agentId, string taskId,
        TimeSpan duration, bool success)
    {
        TaskExecutionRecorded = true;
        LastSuccess = success;
    }

    public void RecordToolUsage(string agentId, string toolName, TimeSpan duration) { }

    public void RecordMemoryOperation(string operation, TimeSpan duration, int itemCount) { }

    public void RecordLlmCall(string provider, TimeSpan duration, int tokenCount) { }

    public System.Threading.Tasks.Task<PerformanceReport> GenerateReportAsync(DateTime from, DateTime to)
    {
        return System.Threading.Tasks.Task.FromResult(new PerformanceReport(
            [],
            [],
            new MemoryMetrics(0, 0, TimeSpan.Zero, []),
            new LlmMetrics(0, 0, TimeSpan.Zero, []),
            TimeSpan.Zero));
    }

    public System.Threading.Tasks.Task<string> ExportToJsonAsync(DateTime from, DateTime to)
    {
        return System.Threading.Tasks.Task.FromResult("{}");
    }

    public System.Threading.Tasks.Task<string> ExportToCsvAsync(DateTime from, DateTime to)
    {
        return System.Threading.Tasks.Task.FromResult("");
    }
}

// Test output class
internal class TestOutput
{
    public string Value { get; set; } = string.Empty;
    public int Count { get; set; }
}

// Test memory scope
internal class TestMemoryScope : IMemoryScope
{
    public string ScopeId => "test-scope";
    public string AgentId => "test-agent";

    public System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation) => operation();
    public System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation) => operation();
    public void Dispose() { }
}

// Supporting classes
public class ExecutionValidation
{
    public bool CanExecute { get; set; }
    public List<string> Reasons { get; set; } = [];
}
