using Orkeon.Application.Callback;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Domain.Tests.Callbacks;

/// <summary>
/// Tests for BaseCallbackHandler following Clean Architecture principles.
/// Tests the base callback handler implementation and composite callback handler.
/// </summary>
public class BaseCallbackHandlerTests
{
    #region Test Implementations

    private class TestCallbackHandler : BaseCallbackHandler
    {
        public List<string> CallLog { get; } = [];

        public override async System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"StepStarted:{context.AgentId}:{context.Action}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"StepCompleted:{context.AgentId}:{context.Success}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"TaskStarted:{context.TaskId}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"TaskProgress:{context.TaskId}:{context.ProgressPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"TaskCompleted:{context.TaskId}:{context.Success}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task OnFlowStepStartedAsync(FlowStepStartedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"FlowStepStarted:{context.StepId}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task OnFlowStepCompletedAsync(FlowStepCompletedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"FlowStepCompleted:{context.StepId}:{context.Success}");
            await System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private class MinimalCallbackHandler : BaseCallbackHandler
    {
        // Uses all default implementations
    }

    private class PartialCallbackHandler : BaseCallbackHandler
    {
        public List<string> CallLog { get; } = [];

        public override async System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"TaskStarted:{context.TaskId}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public override async System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
        {
            CallLog.Add($"TaskCompleted:{context.TaskId}");
            await System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private class ThrowingCallbackHandler : BaseCallbackHandler
    {
        public override System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test exception in step started");
        }

        public override System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
        {
            throw new ArgumentException("Test exception in task started");
        }
    }

    private class CancellationAwareCallbackHandler : BaseCallbackHandler
    {
        public List<string> CallLog { get; } = [];
        public bool WasCancelled { get; private set; }

        public override async System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(100, cancellationToken);
                CallLog.Add("StepStarted:Completed");
            }
            catch (TaskCanceledException ex)
            {
                WasCancelled = true;
                CallLog.Add("StepStarted:Cancelled");
                throw new OperationCanceledException(ex.Message, ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                CallLog.Add("StepStarted:Cancelled");
                throw;
            }
        }
    }

    #endregion

    #region BaseCallbackHandler Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldCompleteSuccessfully_WhenUsingBaseCallbackHandlerWithDefaultImplementations()
    {
        // Arrange
        var handler = new MinimalCallbackHandler();
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        var stepCompletedContext = new StepCompletedContext(new StepIdentity(AgentId1, "analyst", TaskId1), "analyze", "thinking", "result", true, TimeSpan.FromSeconds(5), DateTime.UtcNow);

        // Act & Assert - Should not throw
        await handler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken);
        await handler.OnStepCompletedAsync(stepCompletedContext, TestContext.Current.CancellationToken);

        // All default implementations should return completed tasks
        var task1 = handler.OnTaskStartedAsync(new TaskStartedContext(TaskId1, "desc", "output", AgentId1, "role", DateTime.UtcNow), TestContext.Current.CancellationToken);
        var task2 = handler.OnTaskProgressAsync(new TaskProgressContext(TaskId1, AgentId1, 1, 5, 20.0, "action", DateTime.UtcNow), TestContext.Current.CancellationToken);
        var task3 = handler.OnTaskCompletedAsync(new TaskCompletedContext(TaskId1, AgentId1, new TaskExecutionOutcome(true, "output", null, null), TimeSpan.FromMinutes(1), 3, DateTime.UtcNow), TestContext.Current.CancellationToken);
        var task4 = handler.OnFlowStepStartedAsync(new FlowStepStartedContext(FlowId1, "TestFlow", StepId1, "TestStep", DateTime.UtcNow), TestContext.Current.CancellationToken);
        var task5 = handler.OnFlowStepCompletedAsync(new FlowStepCompletedContext(new FlowStepIdentity(FlowId1, "TestFlow", StepId1, "TestStep"), true, "result", null, TimeSpan.FromSeconds(2), DateTime.UtcNow), TestContext.Current.CancellationToken);

        Assert.True(task1.IsCompletedSuccessfully);
        Assert.True(task2.IsCompletedSuccessfully);
        Assert.True(task3.IsCompletedSuccessfully);
        Assert.True(task4.IsCompletedSuccessfully);
        Assert.True(task5.IsCompletedSuccessfully);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallOverriddenVersions_WhenUsingBaseCallbackHandlerWithOverriddenMethods()
    {
        // Arrange
        var handler = new TestCallbackHandler();
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        var stepCompletedContext = new StepCompletedContext(new StepIdentity(AgentId1, "analyst", TaskId1), "analyze", "thinking", "result", true, TimeSpan.FromSeconds(5), DateTime.UtcNow);
        var taskStartedContext = new TaskStartedContext(TaskId1, "Test task", "Expected output", AgentId1, "analyst", DateTime.UtcNow);

        // Act
        await handler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken);
        await handler.OnStepCompletedAsync(stepCompletedContext, TestContext.Current.CancellationToken);
        await handler.OnTaskStartedAsync(taskStartedContext, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, handler.CallLog.Count);
        Assert.Contains("StepStarted:agent-1:analyze", handler.CallLog);
        Assert.Contains("StepCompleted:agent-1:True", handler.CallLog);
        Assert.Contains("TaskStarted:task-1", handler.CallLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallOnlyOverriddenMethods_WhenUsingBaseCallbackHandlerWithPartialOverrides()
    {
        // Arrange
        var handler = new PartialCallbackHandler();
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        var taskStartedContext = new TaskStartedContext(TaskId1, "Test task", "Expected output", AgentId1, "analyst", DateTime.UtcNow);
        var taskCompletedContext = new TaskCompletedContext(TaskId1, AgentId1, new TaskExecutionOutcome(true, "output", null, null), TimeSpan.FromMinutes(1), 3, DateTime.UtcNow);

        // Act
        await handler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken); // Default implementation
        await handler.OnTaskStartedAsync(taskStartedContext, TestContext.Current.CancellationToken); // Overridden
        await handler.OnTaskCompletedAsync(taskCompletedContext, TestContext.Current.CancellationToken); // Overridden

        // Assert
        Assert.Equal(2, handler.CallLog.Count);
        Assert.Contains("TaskStarted:task-1", handler.CallLog);
        Assert.Contains("TaskCompleted:task-1", handler.CallLog);
        Assert.DoesNotContain("StepStarted", handler.CallLog.FirstOrDefault(x => x.Contains("StepStarted")) ?? "");
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleAllCallbacks_WhenUsingBaseCallbackHandlerWithAllContextTypes()
    {
        // Arrange
        var handler = new TestCallbackHandler();
        var timestamp = DateTime.UtcNow;

        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", timestamp);
        var stepCompletedContext = new StepCompletedContext(new StepIdentity(AgentId1, "analyst", TaskId1), "analyze", "thinking", "result", true, TimeSpan.FromSeconds(5), timestamp);
        var taskStartedContext = new TaskStartedContext(TaskId1, "Test task", "Expected output", AgentId1, "analyst", timestamp);
        var taskProgressContext = new TaskProgressContext(TaskId1, AgentId1, 2, 5, 40.0, "current action", timestamp);
        var taskCompletedContext = new TaskCompletedContext(TaskId1, AgentId1, new TaskExecutionOutcome(true, "final output", new { result = "success" }, null), TimeSpan.FromMinutes(2), 5, timestamp);
        var flowStepStartedContext = new FlowStepStartedContext(FlowId1, "TestFlow", StepId1, "AnalysisStep", timestamp);
        var flowStepCompletedContext = new FlowStepCompletedContext(new FlowStepIdentity(FlowId1, "TestFlow", StepId1, "AnalysisStep"), true, "step result", null, TimeSpan.FromSeconds(10), timestamp);

        // Act
        await handler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken);
        await handler.OnStepCompletedAsync(stepCompletedContext, TestContext.Current.CancellationToken);
        await handler.OnTaskStartedAsync(taskStartedContext, TestContext.Current.CancellationToken);
        await handler.OnTaskProgressAsync(taskProgressContext, TestContext.Current.CancellationToken);
        await handler.OnTaskCompletedAsync(taskCompletedContext, TestContext.Current.CancellationToken);
        await handler.OnFlowStepStartedAsync(flowStepStartedContext, TestContext.Current.CancellationToken);
        await handler.OnFlowStepCompletedAsync(flowStepCompletedContext, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, handler.CallLog.Count);
        Assert.Contains("StepStarted:agent-1:analyze", handler.CallLog);
        Assert.Contains("StepCompleted:agent-1:True", handler.CallLog);
        Assert.Contains("TaskStarted:task-1", handler.CallLog);
        Assert.Contains("TaskProgress:task-1:40", handler.CallLog);
        Assert.Contains("TaskCompleted:task-1:True", handler.CallLog);
        Assert.Contains("FlowStepStarted:step-1", handler.CallLog);
        Assert.Contains("FlowStepCompleted:step-1:True", handler.CallLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCancellation_WhenUsingBaseCallbackHandlerWithCancellationToken()
    {
        // Arrange
        var handler = new CancellationAwareCallbackHandler();
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        using var cts = new CancellationTokenSource();

        // Act & Assert — cancel deterministically before awaiting. The previous
        // CancelAfter(50) racing a 100 ms Task.Delay was flaky: under load the 50 ms
        // cancellation timer could fire only after the delay had already completed, so
        // no exception was thrown. A pre-cancelled token exercises the same handler
        // cancellation path (catch → set flag → log → rethrow) without the race.
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.OnStepStartedAsync(stepStartedContext, cts.Token));

        Assert.True(handler.WasCancelled);
        Assert.Contains("StepStarted:Cancelled", handler.CallLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldComplete_WhenUsingBaseCallbackHandlerWithCancellationTokenNotCancelled()
    {
        // Arrange
        var handler = new CancellationAwareCallbackHandler();
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        using var cts = new CancellationTokenSource();

        // Act
        await handler.OnStepStartedAsync(stepStartedContext, cts.Token);

        // Assert
        Assert.False(handler.WasCancelled);
        Assert.Contains("StepStarted:Completed", handler.CallLog);
    }

    #endregion

    #region CompositeCallbackHandler Tests

    [Fact]
    public void ShouldStoreHandlers_WhenUsingCompositeCallbackHandlerWithParamsConstructor()
    {
        // Arrange
        var handler1 = new TestCallbackHandler();
        var handler2 = new PartialCallbackHandler();

        // Act
        var compositeHandler = new CompositeCallbackHandler(handler1, handler2);

        // Assert
        Assert.NotNull(compositeHandler);
    }

    [Fact]
    public void ShouldStoreHandlers_WhenUsingCompositeCallbackHandlerWithEnumerableConstructor()
    {
        // Arrange
        var handlers = new List<ICallbackHandler>
        {
            new TestCallbackHandler(),
            new PartialCallbackHandler(),
            new MinimalCallbackHandler()
        };

        // Act
        var compositeHandler = new CompositeCallbackHandler(handlers);

        // Assert
        Assert.NotNull(compositeHandler);
    }

    [Fact]
    public void ShouldNotThrow_WhenUsingCompositeCallbackHandlerWithEmptyHandlers()
    {
        // Act & Assert
        var emptyParamsHandler = new CompositeCallbackHandler();
        var emptyEnumerableHandler = new CompositeCallbackHandler(new List<ICallbackHandler>());

        Assert.NotNull(emptyParamsHandler);
        Assert.NotNull(emptyEnumerableHandler);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallAllHandlersInSequence_WhenUsingCompositeCallbackHandler()
    {
        // Arrange
        var handler1 = new TestCallbackHandler();
        var handler2 = new TestCallbackHandler();
        var handler3 = new PartialCallbackHandler();
        var compositeHandler = new CompositeCallbackHandler(handler1, handler2, handler3);

        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        var taskStartedContext = new TaskStartedContext(TaskId1, "Test task", "Expected output", AgentId1, "analyst", DateTime.UtcNow);

        // Act
        await compositeHandler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken);
        await compositeHandler.OnTaskStartedAsync(taskStartedContext, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, handler1.CallLog.Count);
        Assert.Equal(2, handler2.CallLog.Count);
        Assert.Single(handler3.CallLog); // Only handles task events

        Assert.Contains("StepStarted:agent-1:analyze", handler1.CallLog);
        Assert.Contains("TaskStarted:task-1", handler1.CallLog);
        Assert.Contains("StepStarted:agent-1:analyze", handler2.CallLog);
        Assert.Contains("TaskStarted:task-1", handler2.CallLog);
        Assert.Contains("TaskStarted:task-1", handler3.CallLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallAllHandlers_WhenUsingCompositeCallbackHandlerWithAllEventTypes()
    {
        // Arrange
        var handler1 = new TestCallbackHandler();
        var handler2 = new TestCallbackHandler();
        var compositeHandler = new CompositeCallbackHandler(handler1, handler2);
        var timestamp = DateTime.UtcNow;

        // Act
        await compositeHandler.OnStepStartedAsync(new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", timestamp), TestContext.Current.CancellationToken);
        await compositeHandler.OnStepCompletedAsync(new StepCompletedContext(new StepIdentity(AgentId1, "analyst", TaskId1), "analyze", "thinking", "result", true, TimeSpan.FromSeconds(5), timestamp), TestContext.Current.CancellationToken);
        await compositeHandler.OnTaskStartedAsync(new TaskStartedContext(TaskId1, "Test task", "Expected output", AgentId1, "analyst", timestamp), TestContext.Current.CancellationToken);
        await compositeHandler.OnTaskProgressAsync(new TaskProgressContext(TaskId1, AgentId1, 1, 3, 33.33, "action", timestamp), TestContext.Current.CancellationToken);
        await compositeHandler.OnTaskCompletedAsync(new TaskCompletedContext(TaskId1, AgentId1, new TaskExecutionOutcome(true, "output", null, null), TimeSpan.FromMinutes(1), 3, timestamp), TestContext.Current.CancellationToken);
        await compositeHandler.OnFlowStepStartedAsync(new FlowStepStartedContext(FlowId1, "TestFlow", StepId1, "TestStep", timestamp), TestContext.Current.CancellationToken);
        await compositeHandler.OnFlowStepCompletedAsync(new FlowStepCompletedContext(new FlowStepIdentity(FlowId1, "TestFlow", StepId1, "TestStep"), true, "result", null, TimeSpan.FromSeconds(2), timestamp), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, handler1.CallLog.Count);
        Assert.Equal(7, handler2.CallLog.Count);

        // Verify both handlers received all events
        var expectedCalls = new[]
        {
            "StepStarted:agent-1:analyze",
            "StepCompleted:agent-1:True",
            "TaskStarted:task-1",
            "TaskProgress:task-1:33.33",
            "TaskCompleted:task-1:True",
            "FlowStepStarted:step-1",
            "FlowStepCompleted:step-1:True"
        };

        foreach (var expectedCall in expectedCalls)
        {
            Assert.Contains(expectedCall, handler1.CallLog);
            Assert.Contains(expectedCall, handler2.CallLog);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCompleteSuccessfully_WhenUsingCompositeCallbackHandlerWithEmptyHandlers()
    {
        // Arrange
        var compositeHandler = new CompositeCallbackHandler();
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);

        // Act & Assert - Should not throw
        var exception = await Record.ExceptionAsync(async () =>
        {
            await compositeHandler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken);
            await compositeHandler.OnStepCompletedAsync(new StepCompletedContext(new StepIdentity(AgentId1, "analyst", TaskId1), "analyze", "thinking", "result", true, TimeSpan.FromSeconds(5), DateTime.UtcNow), TestContext.Current.CancellationToken);
            await compositeHandler.OnTaskStartedAsync(new TaskStartedContext(TaskId1, "desc", "output", AgentId1, "role", DateTime.UtcNow), TestContext.Current.CancellationToken);
            await compositeHandler.OnTaskProgressAsync(new TaskProgressContext(TaskId1, AgentId1, 1, 5, 20.0, "action", DateTime.UtcNow), TestContext.Current.CancellationToken);
            await compositeHandler.OnTaskCompletedAsync(new TaskCompletedContext(TaskId1, AgentId1, new TaskExecutionOutcome(true, "output", null, null), TimeSpan.FromMinutes(1), 3, DateTime.UtcNow), TestContext.Current.CancellationToken);
            await compositeHandler.OnFlowStepStartedAsync(new FlowStepStartedContext(FlowId1, "TestFlow", StepId1, "TestStep", DateTime.UtcNow), TestContext.Current.CancellationToken);
            await compositeHandler.OnFlowStepCompletedAsync(new FlowStepCompletedContext(new FlowStepIdentity(FlowId1, "TestFlow", StepId1, "TestStep"), true, "result", null, TimeSpan.FromSeconds(2), DateTime.UtcNow), TestContext.Current.CancellationToken);
        });
        Assert.Null(exception);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStopAtFirstException_WhenUsingCompositeCallbackHandlerWithOneHandlerThrowing()
    {
        // Arrange
        var handler1 = new TestCallbackHandler();
        var throwingHandler = new ThrowingCallbackHandler();
        var handler3 = new TestCallbackHandler();
        var compositeHandler = new CompositeCallbackHandler(handler1, throwingHandler, handler3);

        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            compositeHandler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken));

        Assert.Equal("Test exception in step started", exception.Message);

        // First handler should have been called
        Assert.Single(handler1.CallLog);
        Assert.Contains("StepStarted:agent-1:analyze", handler1.CallLog);

        // Third handler should not have been called due to exception
        Assert.Empty(handler3.CallLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateCancellation_WhenUsingCompositeCallbackHandlerWithCancellationToken()
    {
        // Arrange
        var handler1 = new TestCallbackHandler();
        var cancellationAwareHandler = new CancellationAwareCallbackHandler();
        var compositeHandler = new CompositeCallbackHandler(handler1, cancellationAwareHandler);

        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        using var cts = new CancellationTokenSource();

        // Act & Assert — cancel deterministically (was CancelAfter(50) racing the second
        // handler's 100 ms Task.Delay, flaky under load). The composite still invokes
        // handler1 (which ignores the token) before the cancellation-aware handler observes
        // the cancelled token, so the ordering assertions below hold.
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            compositeHandler.OnStepStartedAsync(stepStartedContext, cts.Token));

        // First handler completed successfully
        Assert.Single(handler1.CallLog);
        Assert.Contains("StepStarted:agent-1:analyze", handler1.CallLog);

        // Second handler was cancelled
        Assert.True(cancellationAwareHandler.WasCancelled);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallAllAppropriately_WhenUsingCompositeCallbackHandlerWithMixedHandlerTypes()
    {
        // Arrange
        var fullHandler = new TestCallbackHandler();
        var partialHandler = new PartialCallbackHandler();
        var minimalHandler = new MinimalCallbackHandler();
        var compositeHandler = new CompositeCallbackHandler(fullHandler, partialHandler, minimalHandler);

        var taskStartedContext = new TaskStartedContext(TaskId1, "Test task", "Expected output", AgentId1, "analyst", DateTime.UtcNow);
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);

        // Act
        await compositeHandler.OnTaskStartedAsync(taskStartedContext, TestContext.Current.CancellationToken);
        await compositeHandler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken);

        // Assert
        // Full handler should have both calls
        Assert.Equal(2, fullHandler.CallLog.Count);
        Assert.Contains("TaskStarted:task-1", fullHandler.CallLog);
        Assert.Contains("StepStarted:agent-1:analyze", fullHandler.CallLog);

        // Partial handler should only have task call
        Assert.Single(partialHandler.CallLog);
        Assert.Contains("TaskStarted:task-1", partialHandler.CallLog);

        // Minimal handler uses default implementations (no logging)
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenUsingBaseCallbackHandlerWithNullContext()
    {
        // Arrange
        var handler = new MinimalCallbackHandler();

        // Act & Assert - Should not throw null reference exceptions
        // The methods should handle null contexts gracefully by completing immediately
        var exception = await Record.ExceptionAsync(async () =>
        {
            await handler.OnStepStartedAsync(null!, TestContext.Current.CancellationToken);
            await handler.OnStepCompletedAsync(null!, TestContext.Current.CancellationToken);
            await handler.OnTaskStartedAsync(null!, TestContext.Current.CancellationToken);
            await handler.OnTaskProgressAsync(null!, TestContext.Current.CancellationToken);
            await handler.OnTaskCompletedAsync(null!, TestContext.Current.CancellationToken);
            await handler.OnFlowStepStartedAsync(null!, TestContext.Current.CancellationToken);
            await handler.OnFlowStepCompletedAsync(null!, TestContext.Current.CancellationToken);
        });
        Assert.Null(exception);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSkipNull_WhenUsingCompositeCallbackHandlerWithNullHandlerInList()
    {
        // Arrange
        var handler1 = new TestCallbackHandler();
        ICallbackHandler? nullHandler = null;
        var handler3 = new TestCallbackHandler();
        var handlers = new List<ICallbackHandler?> { handler1, nullHandler, handler3 };

        // This will cause a compilation error, but let's test the behavior if nulls somehow get in
        var validHandlers = handlers.Where(h => h != null).Cast<ICallbackHandler>().ToList();
        var compositeHandler = new CompositeCallbackHandler(validHandlers);

        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);

        // Act
        await compositeHandler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler1.CallLog);
        Assert.Single(handler3.CallLog);
        Assert.Contains("StepStarted:agent-1:analyze", handler1.CallLog);
        Assert.Contains("StepStarted:agent-1:analyze", handler3.CallLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenUsingBaseCallbackHandlerWithExceptionInOverride()
    {
        // Arrange
        var throwingHandler = new ThrowingCallbackHandler();
        var stepStartedContext = new StepStartedContext(AgentId1, "analyst", TaskId1, "analyze", "thinking", DateTime.UtcNow);
        var taskStartedContext = new TaskStartedContext(TaskId1, "Test task", "Expected output", AgentId1, "analyst", DateTime.UtcNow);

        // Act & Assert
        var stepException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            throwingHandler.OnStepStartedAsync(stepStartedContext, TestContext.Current.CancellationToken));
        Assert.Equal("Test exception in step started", stepException.Message);

        var taskException = await Assert.ThrowsAsync<ArgumentException>(() =>
            throwingHandler.OnTaskStartedAsync(taskStartedContext, TestContext.Current.CancellationToken));
        Assert.Equal("Test exception in task started", taskException.Message);
    }

    #endregion
}
