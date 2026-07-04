using Orkeon.Application.Callback;
using Microsoft.Extensions.Logging;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Callbacks;

public class CallbackExamplesTests
{
    private readonly TestLogger<LoggingCallbackHandler> _logger;

    public CallbackExamplesTests()
    {
        _logger = new TestLogger<LoggingCallbackHandler>();
    }

    [Fact]
    public void ShouldReturnValidAgent_WhenUsingCreateAgent()
    {
        // Act
        var agent = CallbackExamples.CreateAgent();

        // Assert
        Assert.NotNull(agent);
        Assert.Equal("Research Analyst", agent.Role.Value);
        Assert.Equal("Analyze market data and provide insights", agent.Goal.Value);
        Assert.Equal("You are an experienced analyst with deep market knowledge.", agent.Backstory!);
    }

    [Fact]
    public void ShouldReturnValidAgent_WhenCreatingAgentForCallbacks()
    {
        // Act
        var agent = CallbackExamples.CreateAgentForCallbacks();

        // Assert
        Assert.NotNull(agent);
        Assert.Equal("Content Writer", agent.Role.Value);
        Assert.Equal("Create engaging content based on research", agent.Goal.Value);
        Assert.Equal("You are a skilled writer who transforms data into compelling narratives.", agent.Backstory!);
    }

    [Fact]
    public void ShouldReturnValidTask_WhenUsingCreateTask()
    {
        // Act
        var task = CallbackExamples.CreateTask();

        // Assert
        Assert.NotNull(task);
        Assert.Equal("Research the latest AI trends in 2024", task.Description.Value);
        Assert.Equal("A comprehensive report on AI trends", task.ExpectedOutput);
    }

    [Fact]
    public void ShouldReturnValidCallbacks_WhenCreatingSimpleTaskCallbacks()
    {
        // Act
        var callbacks = CallbackExamples.CreateSimpleTaskCallbacks(_logger);

        // Assert
        Assert.NotNull(callbacks);
        Assert.NotNull(callbacks.OnStarted);
        Assert.NotNull(callbacks.OnProgress);
        Assert.NotNull(callbacks.OnCompleted);
        Assert.NotNull(callbacks.OnFailed);
        Assert.NotNull(callbacks.OnFinally);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteCallbacks_WhenCreatingSimpleTaskCallbacks()
    {
        // Arrange
        var testLogger = new TestLogger<LoggingCallbackHandler>();
        var callbacks = CallbackExamples.CreateSimpleTaskCallbacks(testLogger);

        var startedContext = new TaskStartedContext(
            TaskId: "test-123",
            Description: "Test Task",
            ExpectedOutput: "Test output",
            AgentId: AgentId1,
            AgentRole: RoleWorker,
            Timestamp: DateTime.UtcNow);

        var progressContext = new TaskProgressContext(
            TaskId: "test-123",
            AgentId: AgentId1,
            StepNumber: 5,
            TotalSteps: 10,
            ProgressPercentage: 50.0,
            CurrentAction: "Processing",
            Timestamp: DateTime.UtcNow);

        var completedContext = new TaskCompletedContext(
            TaskId: "test-123",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Task completed successfully", null, null),
            Duration: TimeSpan.FromSeconds(10),
            StepsExecuted: 10,
            Timestamp: DateTime.UtcNow);

        // Act
        await callbacks.OnStarted!.Invoke(startedContext);
        await callbacks.OnProgress!.Invoke(progressContext);
        await callbacks.OnCompleted!.Invoke(completedContext);

        // Assert
        Assert.Contains(testLogger.LogMessages, m => m.Contains("Starting task: Test Task"));
        Assert.Contains(testLogger.LogMessages, m => m.Contains("Progress: 50%"));
        Assert.Contains(testLogger.LogMessages, m => m.Contains("Task completed in 10"));
    }

    [Fact]
    public void ShouldReturnValidCallbacks_WhenCreatingAsyncTaskCallbacks()
    {
        // Act
        var callbacks = CallbackExamples.CreateAsyncTaskCallbacks(_logger);

        // Assert
        Assert.NotNull(callbacks);
        Assert.NotNull(callbacks.OnStarted);
        Assert.NotNull(callbacks.OnProgress);
        Assert.NotNull(callbacks.OnCompleted);
        Assert.NotNull(callbacks.OnFailed);
    }

    [Fact]
    public void ShouldReturnValidTask_WhenUsingCreateAnalysisTask()
    {
        // Act
        var task = CallbackExamples.CreateAnalysisTask();

        // Assert
        Assert.NotNull(task);
        Assert.Equal("Generate comprehensive market analysis", task.Description.Value);
        Assert.Equal("Detailed market analysis report", task.ExpectedOutput);
    }

    [Fact]
    public void ShouldReturnValidHandler_WhenUsingCreateCompositeCallbackHandler()
    {
        // Act
        var handler = CallbackExamples.CreateCompositeCallbackHandler(_logger);

        // Assert
        Assert.NotNull(handler);
        Assert.IsType<CompositeCallbackHandler>(handler);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldTrackWorkflowSteps_WhenUsingCustomWorkflowCallbackHandler()
    {
        // Arrange
        var handler = new CustomWorkflowCallbackHandler("workflow-123");

        var startContext = new TaskStartedContext(
            TaskId: "task-456",
            Description: "Process data",
            ExpectedOutput: "Processed data",
            AgentId: AgentId1,
            AgentRole: RoleWorker,
            Timestamp: DateTime.UtcNow);

        var completedContext = new TaskCompletedContext(
            TaskId: "task-456",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Workflow step completed", null, null),
            Duration: TimeSpan.FromSeconds(5),
            StepsExecuted: 1,
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnTaskStartedAsync(startContext, TestContext.Current.CancellationToken);
        await handler.OnTaskCompletedAsync(completedContext, TestContext.Current.CancellationToken);

        var steps = handler.GetWorkflowSteps();

        // Assert
        Assert.Equal(2, steps.Count);
        Assert.Contains("Task Started: Process data", steps[0]);
        Assert.Contains("Task Completed: True in", steps[1]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleFailure_WhenUsingCustomWorkflowCallbackHandler()
    {
        // Arrange
        var handler = new CustomWorkflowCallbackHandler("workflow-789");

        var completedContext = new TaskCompletedContext(
            TaskId: "task-fail",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(false, null, null, "Task failed due to error"),
            Duration: TimeSpan.FromSeconds(2),
            StepsExecuted: 0,
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnTaskCompletedAsync(completedContext, TestContext.Current.CancellationToken);

        var steps = handler.GetWorkflowSteps();

        // Assert
        Assert.Single(steps);
        Assert.Contains("Task Completed: False", steps[0]);
    }

}

// Test doubles
public class TestLogger<T> : ILogger<T>
{
    private readonly List<string> _logMessages = [];

    public IReadOnlyList<string> LogMessages => _logMessages.AsReadOnly();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _logMessages.Add($"[{logLevel}] {message}");
    }

    private class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
