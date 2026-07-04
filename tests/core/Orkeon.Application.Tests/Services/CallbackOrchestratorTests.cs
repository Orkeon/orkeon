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

        public System.Threading.Tasks.Task OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"TaskProgress:{context.TaskId}:{context.ProgressPercentage}"); }
            if (ThrowOnCallback) throw new InvalidOperationException("Test exception");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"TaskCompleted:{context.TaskId}:{context.Success}"); }
            if (ThrowOnCallback) throw new InvalidOperationException("Test exception");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnFlowStepStartedAsync(FlowStepStartedContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"FlowStepStarted:{context.FlowExecutionId}:{context.StepId}"); }
            if (ThrowOnCallback) throw new InvalidOperationException("Test exception");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task OnFlowStepCompletedAsync(FlowStepCompletedContext context, CancellationToken cancellationToken = default)
        {
            lock (_lock) { RecordedEvents.Add($"FlowStepCompleted:{context.FlowExecutionId}:{context.Success}"); }
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
    public async System.Threading.Tasks.Task ShouldDispatchToHandlers_WhenNotifyingTaskStartedAsyncWithCallbackHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var startTime = DateTime.UtcNow;

        var agentHandler = new TestCallbackHandler();
        var taskHandler = new TestCallbackHandler();

        // Act
        await orchestrator.NotifyTaskStartedAsync(
            agent, task, startTime,
            new CallbackHandlers { AgentCallbackHandler = agentHandler, TaskCallbackHandler = taskHandler }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(agentHandler.RecordedEvents, e => e.StartsWith("TaskStarted:"));
        Assert.Contains(taskHandler.RecordedEvents, e => e.StartsWith("TaskStarted:"));
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
    public async System.Threading.Tasks.Task ShouldInvokeOnStarted_WhenNotifyingTaskStartedAsyncWithTaskCallbacks()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        TaskStartedContext? capturedContext = null;

        var taskCallbacks = TaskCallbacks.Create(
            onStarted: ctx => { capturedContext = ctx; return System.Threading.Tasks.Task.CompletedTask; });

        // Act
        await orchestrator.NotifyTaskStartedAsync(
            agent, task, DateTime.UtcNow,
            new CallbackHandlers { TaskCallbacks = taskCallbacks }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedContext);
        Assert.Equal(task.Id.Value.ToString(), capturedContext!.TaskId);
        Assert.Equal(agent.Id, capturedContext.AgentId);
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

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchToAll_WhenNotifyingTaskCompletedAsyncWithOptionalHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);
        var agentHandler = new TestCallbackHandler();
        var taskHandler = new TestCallbackHandler();

        // Act
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task,
            new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = DateTime.UtcNow },
            new CallbackHandlers { AgentCallbackHandler = agentHandler, TaskCallbackHandler = taskHandler }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(agentHandler.RecordedEvents, e => e.StartsWith("TaskCompleted:"));
        Assert.Contains(taskHandler.RecordedEvents, e => e.StartsWith("TaskCompleted:"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldInvokeOnCompletedAndOnFinally_WhenNotifyingTaskCompletedAsyncWithTaskCallbacksSuccess()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);
        var completedCalled = false;
        var failedCalled = false;
        var finallyCalled = false;

        var taskCallbacks = TaskCallbacks.Create(
            onCompleted: ctx => { completedCalled = true; return System.Threading.Tasks.Task.CompletedTask; },
            onFailed: ctx => { failedCalled = true; return System.Threading.Tasks.Task.CompletedTask; },
            onFinally: ctx => { finallyCalled = true; return System.Threading.Tasks.Task.CompletedTask; });

        // Act
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task,
            new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = DateTime.UtcNow },
            new CallbackHandlers { TaskCallbacks = taskCallbacks }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(completedCalled);
        Assert.False(failedCalled);
        Assert.True(finallyCalled);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldInvokeOnFailedAndOnFinally_WhenNotifyingTaskCompletedAsyncWithTaskCallbacksFailure()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(false);
        var completedCalled = false;
        var failedCalled = false;
        var finallyCalled = false;

        var taskCallbacks = TaskCallbacks.Create(
            onCompleted: ctx => { completedCalled = true; return System.Threading.Tasks.Task.CompletedTask; },
            onFailed: ctx => { failedCalled = true; return System.Threading.Tasks.Task.CompletedTask; },
            onFinally: ctx => { finallyCalled = true; return System.Threading.Tasks.Task.CompletedTask; });

        // Act
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task,
            new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = DateTime.UtcNow },
            new CallbackHandlers { TaskCallbacks = taskCallbacks }, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(completedCalled);
        Assert.True(failedCalled);
        Assert.True(finallyCalled);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldComplete_WhenNotifyingTaskCompletedAsyncWithTaskCallbacksOldStyle()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);

        var taskCallbacks = new TaskCallbacks
        {
            OnStarted = (ctx) => System.Threading.Tasks.Task.CompletedTask,
            OnCompleted = (ctx) => System.Threading.Tasks.Task.CompletedTask
        };

        // Act
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task,
            new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = DateTime.UtcNow },
            new CallbackHandlers { TaskCallbacks = taskCallbacks }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Task {task.Id.Value} completed by agent {agent.Id} with result: True"));
    }

    #endregion

    #region NotifyStepProgressAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogStepProgress_WhenNotifyingStepProgressAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var stepDescription = "Processing data";
        var currentStep = 3;
        var totalSteps = 10;

        // Act
        await orchestrator.NotifyStepProgressAsync(
            agent, task, new StepProgressInfo { StepDescription = stepDescription, CurrentStep = currentStep, TotalSteps = totalSteps }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Task {task.Id.Value} step {currentStep}/{totalSteps}: {stepDescription}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchToHandlers_WhenNotifyingStepProgressAsyncWithHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        var agentHandler = new TestCallbackHandler();
        var taskHandler = new TestCallbackHandler();

        // Act
        await orchestrator.NotifyStepProgressAsync(
            agent, task,
            new StepProgressInfo { StepDescription = "Step 1", CurrentStep = 1, TotalSteps = 5 },
            new CallbackHandlers { AgentCallbackHandler = agentHandler, TaskCallbackHandler = taskHandler }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug("step 1/5"));
        Assert.Contains(agentHandler.RecordedEvents, e => e.StartsWith("TaskProgress:"));
        Assert.Contains(taskHandler.RecordedEvents, e => e.StartsWith("TaskProgress:"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatch_WhenNotifyingStepProgressAsyncWithRegisteredHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        // Act
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "Processing", CurrentStep = 2, TotalSteps = 4 }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.RecordedEvents);
        Assert.Contains(handler.RecordedEvents, e => e.Contains("TaskProgress:") && e.Contains("50"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotDivideByZero_WhenNotifyingStepProgressAsyncWithZeroTotalSteps()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        // Act (should not throw)
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "Processing", CurrentStep = 0, TotalSteps = 0 }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.RecordedEvents);
        Assert.Contains(handler.RecordedEvents, e => e.Contains("TaskProgress:") && e.Contains('0'));
    }

    #endregion

    #region NotifyToolUsedAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogToolUsage_WhenNotifyingToolUsedAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var toolName = ToolSearch;
        var input = new { query = "test" };
        var output = new { results = new[] { "result1", "result2" } };
        var duration = TimeSpan.FromMilliseconds(250);

        // Act
        await orchestrator.NotifyToolUsedAsync(
            agent, toolName, input, output, duration, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Agent {agent.Id} used tool {toolName} for {duration.TotalMilliseconds}ms"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillLog_WhenNotifyingToolUsedAsyncWithZeroDuration()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();

        // Act
        await orchestrator.NotifyToolUsedAsync(
            agent, "TestTool", "input", "output", TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug("used tool TestTool for 0ms"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchStepCompleted_WhenNotifyingToolUsedAsyncWithRegisteredHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler]);
        var agent = CreateTestAgent();

        // Act
        await orchestrator.NotifyToolUsedAsync(agent, ToolSearch, ParamQuery, "results", TimeSpan.FromSeconds(1), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(handler.RecordedEvents, e => e.Contains("StepCompleted:") && e.Contains("tool:SearchTool"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatch_WhenNotifyingToolUsedAsyncWithOptionalHandler()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();

        // Act
        await orchestrator.NotifyToolUsedAsync(agent, ToolWebScrape, ParamUrl, "content", TimeSpan.FromSeconds(2),
            agentCallbackHandler: handler, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(handler.RecordedEvents, e => e.Contains("StepCompleted:") && e.Contains("tool:WebScrape"));
    }

    #endregion

    #region NotifyDelegationAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogDelegationInfo_WhenNotifyingDelegationAsync()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var fromAgent = CreateTestAgent("agent1", RoleDeveloper);
        var toAgent = CreateTestAgent("agent2", "Reviewer");
        var task = CreateTestTask();
        var reason = "Agent lacks required skills";

        // Act
        await orchestrator.NotifyDelegationAsync(
            fromAgent, toAgent, task, reason, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedInfo($"Task {task.Id.Value} delegated from {fromAgent.Role} to {toAgent.Role}: {reason}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillLog_WhenNotifyingDelegationAsyncWithEmptyReason()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var fromAgent = CreateTestAgent();
        var toAgent = CreateTestAgent("agent2");
        var task = CreateTestTask();

        // Act
        await orchestrator.NotifyDelegationAsync(
            fromAgent, toAgent, task, string.Empty, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedInfo("delegated from"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchStepCompleted_WhenNotifyingDelegationAsyncWithRegisteredHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler]);
        var fromAgent = CreateTestAgent(role: RoleDeveloper);
        var toAgent = CreateTestAgent(role: "Reviewer");
        var task = CreateTestTask();

        // Act
        await orchestrator.NotifyDelegationAsync(fromAgent, toAgent, task, "Needs review", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(handler.RecordedEvents, e => e.Contains("StepCompleted:") && e.Contains("delegate_to:Reviewer"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatch_WhenNotifyingDelegationAsyncWithOptionalHandler()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger);
        var fromAgent = CreateTestAgent(role: RoleWorker);
        var toAgent = CreateTestAgent(role: RoleManager);
        var task = CreateTestTask();

        // Act
        await orchestrator.NotifyDelegationAsync(fromAgent, toAgent, task, "Escalation",
            agentCallbackHandler: handler, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(handler.RecordedEvents, e => e.Contains("StepCompleted:") && e.Contains("delegate_to:Manager"));
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

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueExecution_WhenNotifyingStepProgressAsyncHandlerThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var failingHandler = new TestCallbackHandler { ThrowOnCallback = true };
        var successHandler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [failingHandler, successHandler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        // Act (should not throw)
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "step", CurrentStep = 1, TotalSteps = 2 }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(successHandler.RecordedEvents, e => e.StartsWith("TaskProgress:"));
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueExecution_WhenNotifyingTaskStartedAsyncOptionalHandlerThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var failingAgentHandler = new TestCallbackHandler { ThrowOnCallback = true };
        var taskHandler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        // Act (should not throw)
        await orchestrator.NotifyTaskStartedAsync(
            agent, task, DateTime.UtcNow,
            new CallbackHandlers { AgentCallbackHandler = failingAgentHandler, TaskCallbackHandler = taskHandler }, TestContext.Current.CancellationToken);

        // Assert - taskHandler still got called after agentHandler failed
        Assert.Contains(taskHandler.RecordedEvents, e => e.StartsWith("TaskStarted:"));
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueToOnFinally_WhenNotifyingTaskCompletedAsyncTaskCallbackOnCompletedThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);
        var finallyCalled = false;

        var taskCallbacks = TaskCallbacks.Create(
            onCompleted: ctx => throw new InvalidOperationException("OnCompleted failed"),
            onFinally: ctx => { finallyCalled = true; return System.Threading.Tasks.Task.CompletedTask; });

        // Act (should not throw)
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task,
            new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = DateTime.UtcNow },
            new CallbackHandlers { TaskCallbacks = taskCallbacks }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(finallyCalled);
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueExecution_WhenNotifyingToolUsedAsyncHandlerThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var failingHandler = new TestCallbackHandler { ThrowOnCallback = true };
        var orchestrator = new CallbackOrchestrator(logger, [failingHandler]);
        var agent = CreateTestAgent();

        // Act (should not throw)
        await orchestrator.NotifyToolUsedAsync(agent, "Tool", "in", "out", TimeSpan.FromSeconds(1), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueExecution_WhenNotifyingDelegationAsyncHandlerThrows()
    {
        // Arrange
        var logger = new TestLogger();
        var failingHandler = new TestCallbackHandler { ThrowOnCallback = true };
        var orchestrator = new CallbackOrchestrator(logger, [failingHandler]);
        var fromAgent = CreateTestAgent(role: "A");
        var toAgent = CreateTestAgent(role: "B");
        var task = CreateTestTask();

        // Act (should not throw)
        await orchestrator.NotifyDelegationAsync(fromAgent, toAgent, task, "reason", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedWarning("threw an exception"));
    }

    #endregion

    #region Combined Registered + Optional Handlers Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchToAll_WhenNotifyingTaskStartedAsyncRegisteredAndOptionalHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var registeredHandler = new TestCallbackHandler();
        var agentHandler = new TestCallbackHandler();
        var taskHandler = new TestCallbackHandler();
        TaskStartedContext? callbacksContext = null;

        var orchestrator = new CallbackOrchestrator(logger, [registeredHandler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();

        var taskCallbacks = TaskCallbacks.Create(
            onStarted: ctx => { callbacksContext = ctx; return System.Threading.Tasks.Task.CompletedTask; });

        // Act
        await orchestrator.NotifyTaskStartedAsync(
            agent, task, DateTime.UtcNow,
            new CallbackHandlers { AgentCallbackHandler = agentHandler, TaskCallbackHandler = taskHandler, TaskCallbacks = taskCallbacks }, TestContext.Current.CancellationToken);

        // Assert - all 4 dispatch targets were called
        Assert.Single(registeredHandler.RecordedEvents);
        Assert.Single(agentHandler.RecordedEvents);
        Assert.Single(taskHandler.RecordedEvents);
        Assert.NotNull(callbacksContext);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchToAll_WhenNotifyingTaskCompletedAsyncRegisteredAndOptionalHandlers()
    {
        // Arrange
        var logger = new TestLogger();
        var registeredHandler = new TestCallbackHandler();
        var agentHandler = new TestCallbackHandler();
        var taskHandler = new TestCallbackHandler();
        var completedCalled = false;
        var finallyCalled = false;

        var orchestrator = new CallbackOrchestrator(logger, [registeredHandler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var result = CreateTestResult(true);

        var taskCallbacks = TaskCallbacks.Create(
            onCompleted: ctx => { completedCalled = true; return System.Threading.Tasks.Task.CompletedTask; },
            onFinally: ctx => { finallyCalled = true; return System.Threading.Tasks.Task.CompletedTask; });

        // Act
        await orchestrator.NotifyTaskCompletedAsync(
            agent, task,
            new TaskCompletionInfo { Result = result, StepsExecuted = 1, StartTime = DateTime.UtcNow },
            new CallbackHandlers { AgentCallbackHandler = agentHandler, TaskCallbackHandler = taskHandler, TaskCallbacks = taskCallbacks }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(registeredHandler.RecordedEvents, e => e.StartsWith("TaskCompleted:"));
        Assert.Contains(agentHandler.RecordedEvents, e => e.StartsWith("TaskCompleted:"));
        Assert.Contains(taskHandler.RecordedEvents, e => e.StartsWith("TaskCompleted:"));
        Assert.True(completedCalled);
        Assert.True(finallyCalled);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldAllBeLogged_WhenUsingMultipleNotifications()
    {
        // Arrange
        var logger = new TestLogger();
        var orchestrator = new CallbackOrchestrator(logger);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var startTime = DateTime.UtcNow;

        // Act
        await orchestrator.NotifyTaskStartedAsync(agent, task, startTime, cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "Step 1", CurrentStep = 1, TotalSteps = 3 }, cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyToolUsedAsync(agent, "Tool1", "input", "output", TimeSpan.FromSeconds(1), cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "Step 2", CurrentStep = 2, TotalSteps = 3 }, cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "Step 3", CurrentStep = 3, TotalSteps = 3 }, cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyTaskCompletedAsync(agent, task, new TaskCompletionInfo { Result = CreateTestResult(true), StepsExecuted = 3, StartTime = startTime }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(6, logger.LoggedMessages.Count);
        Assert.Contains(logger.LoggedMessages, m => m.Contains("started"));
        Assert.Contains(logger.LoggedMessages, m => m.Contains("step 1/3"));
        Assert.Contains(logger.LoggedMessages, m => m.Contains("used tool"));
        Assert.Contains(logger.LoggedMessages, m => m.Contains("completed"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDispatchAll_WhenUsingMultipleNotificationsWithRegisteredHandler()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler]);
        var agent = CreateTestAgent();
        var task = CreateTestTask();
        var startTime = DateTime.UtcNow;

        // Act
        await orchestrator.NotifyTaskStartedAsync(agent, task, startTime, cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "Step 1", CurrentStep = 1, TotalSteps = 2 }, cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyToolUsedAsync(agent, "Tool1", "input", "output", TimeSpan.FromSeconds(1), cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = "Step 2", CurrentStep = 2, TotalSteps = 2 }, cancellationToken: TestContext.Current.CancellationToken);
        await orchestrator.NotifyTaskCompletedAsync(agent, task, new TaskCompletionInfo { Result = CreateTestResult(true), StepsExecuted = 2, StartTime = startTime }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - handler should have received 5 events
        Assert.Equal(5, handler.RecordedEvents.Count);
        Assert.Contains(handler.RecordedEvents, e => e.StartsWith("TaskStarted:"));
        Assert.Equal(2, handler.RecordedEvents.Count(e => e.StartsWith("TaskProgress:")));
        Assert.Contains(handler.RecordedEvents, e => e.StartsWith("StepCompleted:") && e.Contains("tool:Tool1"));
        Assert.Contains(handler.RecordedEvents, e => e.StartsWith("TaskCompleted:"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingConcurrentNotifications()
    {
        // Arrange
        var logger = new TestLogger();
        var handler = new TestCallbackHandler();
        var orchestrator = new CallbackOrchestrator(logger, [handler]);
        var tasks = new List<System.Threading.Tasks.Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var agent = CreateTestAgent($"agent{i}");
            var task = CreateTestTask($"task{i}");
            var index = i;

            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await orchestrator.NotifyTaskStartedAsync(agent, task, DateTime.UtcNow);
                await orchestrator.NotifyStepProgressAsync(agent, task, new StepProgressInfo { StepDescription = $"Step {index}", CurrentStep = 1, TotalSteps = 1 });
                await orchestrator.NotifyTaskCompletedAsync(
                    agent, task, new TaskCompletionInfo { Result = CreateTestResult(true, task.Id.Value.ToString(), agent.Id), StepsExecuted = 1, StartTime = DateTime.UtcNow });
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        // Deterministic wait (R5.6): poll until all logging/dispatch is complete instead of a fixed delay
        await Orkeon.Tests.Shared.Timing.Polling.WaitUntilAsync(
            () => logger.LoggedMessages.Count >= 30 && handler.RecordedEvents.Count >= 30);
        Assert.True(logger.LoggedMessages.Count >= 30); // At least 3 logs per iteration * 10 iterations
        Assert.True(handler.RecordedEvents.Count >= 30); // Handler also dispatched for all
    }

    #endregion
}
