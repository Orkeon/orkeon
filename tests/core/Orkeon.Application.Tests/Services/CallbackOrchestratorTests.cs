using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Execution;
using Orkeon.Application.Callback;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ApplicationTaskResult = Orkeon.Application.Interfaces.Services.TaskResult;
using Orkeon.Application.Interfaces.Services;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Application.Tests.Services;

public class CallbackOrchestratorTests
{
    #region Test Doubles

    private class TestLogger : ILogger<CallbackOrchestrator>
    {
        private readonly object _lock = new();
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            lock (_lock)
            {
                LoggedMessages.Add($"[{logLevel}] {message}");
                if (exception != null)
                {
                    LoggedExceptions.Add(exception);
                }
            }
        }

        public bool HasLoggedDebug(string partialMessage)
        {
            lock (_lock)
            {
                return LoggedMessages.Any(m => m.StartsWith("[Debug]") && m.Contains(partialMessage));
            }
        }

        public bool HasLoggedInfo(string partialMessage)
        {
            lock (_lock)
            {
                return LoggedMessages.Any(m => m.StartsWith("[Information]") && m.Contains(partialMessage));
            }
        }

        public bool HasLoggedWarning(string partialMessage)
        {
            lock (_lock)
            {
                return LoggedMessages.Any(m => m.StartsWith("[Warning]") && m.Contains(partialMessage));
            }
        }
    }

    private class TestCallbackHandler : ICallbackHandler
    {
        private readonly object _lock = new();
        public List<string> RecordedEvents { get; } = [];
        public bool ThrowOnCallback { get; set; }

        public System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"StepStarted:{context.AgentId}:{context.Action}"); }
            if (ThrowOnCallback) throw new InvalidOperationException("Test exception");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"StepCompleted:{context.AgentId}:{context.Action}:{context.Success}"); }
            if (ThrowOnCallback) throw new InvalidOperationException("Test exception");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"TaskStarted:{context.TaskId}:{context.AgentId}"); }
            if (ThrowOnCallback) throw new InvalidOperationException("Test exception");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"TaskCompleted:{context.TaskId}:{context.Success}"); }
            if (ThrowOnCallback) throw new InvalidOperationException("Test exception");
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    #endregion

    #region Test Helpers

    private static DomainAgent CreateTestAgent(string id = "agent1", string role = "TestAgent")
    {
        return DomainAgent.Create(AgentRole.From(role), AgentGoal.From($"Goal for {role}"));
    }

    private static DomainTask CreateTestTask(string id = "task1", string description = "Test task")
    {
        var task = DomainTask.Create(TaskDescription.From(description), ExpectedOutput.From($"Expected output for {description}"));
        // TaskId is automatically generated, we'll work with the generated one
        return task;
    }

    private static ApplicationTaskResult CreateTestResult(bool success = true, string taskId = "task1", string agentId = "agent1")
    {
        return new ApplicationTaskResult(
            success,
            success ? "Task completed" : "Task failed",
            null,
            [],
            TimeSpan.FromSeconds(1),
            success ? null : "Task failed"
        );
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new CallbackOrchestrator(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithValidLogger()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var orchestrator = new CallbackOrchestrator(logger);

        // Assert
        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var handlers = new List<ICallbackHandler> { new TestCallbackHandler() };

        // Act
        var orchestrator = new CallbackOrchestrator(logger, handlers);

        // Assert
        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void ShouldInitializeWithEmptyList_WhenConstructingWithNullHandlers()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var orchestrator = new CallbackOrchestrator(logger, null);

        // Assert
        Assert.NotNull(orchestrator);
    }

    #endregion

    #region NotifyTaskStartedAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogDebugMessage_WhenNotifyingTaskStartedAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var startTime = DateTime.UtcNow;

        // Act
        await orchestrator.NotifyTaskStartedAsync(agent, task, startTime, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Task {task.Id.Value} started by agent {agent.Id}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchToAll_WhenNotifyingTaskStartedAsyncWithRegisteredHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var handler1 = new TestCallbackHandler();
        var handler2 = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler1, handler2]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        // Act
        await orchestrator.NotifyTaskStartedAsync(agent, task, DateTime.UtcNow, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler1.RecordedEvents);
        Assert.Contains(handler1.RecordedEvents, e => e.StartsWith("TaskStarted:"));
        Assert.Single(handler2.RecordedEvents);
        Assert.Contains(handler2.RecordedEvents, e => e.StartsWith("TaskStarted:"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellation_WhenNotifyingTaskStartedAsyncWithCancellation()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act - Should still log but skip handler dispatch for cancelled tokens
        var exception = await Record.ExceptionAsync(async () =>
            await orchestrator.NotifyTaskStartedAsync(
                agent, task, DateTime.UtcNow,
                cancellationToken: cts.Token));
        Assert.Null(exception);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContextContainsCorrectData_WhenNotifyingTaskStartedAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler]);
        var agent = CreateTestAgent(role: RoleAnalyst);
        var task = CreateTestTask(description: GoalAnalyzeData);

        // Act
        await orchestrator.NotifyTaskStartedAsync(agent, task, DateTime.UtcNow, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.RecordedEvents);
        Assert.Contains(agent.Id, handler.RecordedEvents[0]);
    }

    #endregion

    #region NotifyTaskCompletedAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogDebugMessage_WhenNotifyingTaskCompletedAsyncWithSuccessResult()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);
        var startTime = DateTime.UtcNow;

        // Act
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task, new TaskCompletionInfo { Result = result, StepsExecuted = 5, StartTime = startTime }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Task {task.Id.Value} completed by agent {agent.Id} with result: True"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogDebugMessage_WhenNotifyingTaskCompletedAsyncWithFailureResult()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(false);
        var startTime = DateTime.UtcNow;

        // Act
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task, new TaskCompletionInfo { Result = result, StepsExecuted = 3, StartTime = startTime }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Task {task.Id.Value} completed by agent {agent.Id} with result: False"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchToAll_WhenNotifyingTaskCompletedAsyncWithRegisteredHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var handler1 = new TestCallbackHandler();
        var handler2 = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler1, handler2]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);

        // Act
        await orchestrator.NotifyTaskCompletedAsync(agent, task, new TaskCompletionInfo { Result = result, StepsExecuted = 5, StartTime = DateTime.UtcNow }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(handler1.RecordedEvents, e => e.Contains("TaskCompleted:") && e.Contains(":True"));
        Assert.Contains(handler2.RecordedEvents, e => e.Contains("TaskCompleted:") && e.Contains(":True"));
    }

    #endregion

    #region Error Resilience Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueExecution_WhenNotifyingTaskStartedAsyncHandlerThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var failingHandler = new TestCallbackHandler { ThrowOnCallback = true };
        var successHandler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [failingHandler, successHandler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        // Act (should not throw)
        await orchestrator.NotifyTaskStartedAsync(agent, task, DateTime.UtcNow, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - failing handler was called (recorded event before throw)
        Assert.Contains(failingHandler.RecordedEvents, e => e.StartsWith("TaskStarted:"));
        // success handler was still called after failure
        Assert.Contains(successHandler.RecordedEvents, e => e.StartsWith("TaskStarted:"));
        // Warning was logged
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueExecution_WhenNotifyingTaskCompletedAsyncHandlerThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var failingHandler = new TestCallbackHandler { ThrowOnCallback = true };
        var successHandler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [failingHandler, successHandler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);

        // Act (should not throw)
        await orchestrator.NotifyTaskCompletedAsync(agent, task, new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = DateTime.UtcNow }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(successHandler.RecordedEvents, e => e.StartsWith("TaskCompleted:"));
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    #endregion


    #region Step notifications (GAP-06)

    private static StepStartedContext StepStarted() =>
        new("agent-1", "Analyst", "task-1", "tool:search", "Using tool search", DateTime.UtcNow);

    private static StepCompletedContext StepCompleted(bool success) =>
        new(new StepIdentity("agent-1", "Analyst", "task-1"), "tool:search", "Using tool search",
            success ? "3 rows" : "Error: boom", success, TimeSpan.FromMilliseconds(5), DateTime.UtcNow);

    [Fact]
    public async System.Threading.Tasks.Task NotifyStepStartedAsync_reaches_every_registered_handler()
    {
        var first = new TestCallbackHandler();
        var second = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(new TestLogger(), [first, second]);

        await orchestrator.NotifyStepStartedAsync(StepStarted(), TestContext.Current.CancellationToken);

        Assert.Equal(["StepStarted:agent-1:tool:search"], first.RecordedEvents);
        Assert.Equal(first.RecordedEvents, second.RecordedEvents);
    }

    [Fact]
    public async System.Threading.Tasks.Task NotifyStepCompletedAsync_reaches_every_registered_handler_with_the_outcome()
    {
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(new TestLogger(), [handler]);

        await orchestrator.NotifyStepCompletedAsync(StepCompleted(success: false), TestContext.Current.CancellationToken);

        Assert.Equal(["StepCompleted:agent-1:tool:search:False"], handler.RecordedEvents);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_throwing_step_handler_is_logged_and_the_next_one_still_runs()
    {
        var logger = new TestLogger();
        var failing = new TestCallbackHandler { ThrowOnCallback = true };
        var healthy = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [failing, healthy]);

        await orchestrator.NotifyStepStartedAsync(StepStarted(), TestContext.Current.CancellationToken);
        await orchestrator.NotifyStepCompletedAsync(StepCompleted(success: true), TestContext.Current.CancellationToken);

        Assert.Equal(2, healthy.RecordedEvents.Count);
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    #endregion
}
