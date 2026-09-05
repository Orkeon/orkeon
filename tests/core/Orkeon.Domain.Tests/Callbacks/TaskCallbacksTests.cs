using Orkeon.Application.Callback;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Callbacks;

/// <summary>
/// Tests for TaskCallbacks following Clean Architecture principles.
/// Tests the task-specific callback functionality.
/// </summary>
public class TaskCallbacksTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    #region Test Helpers

    private static TaskStartedContext CreateTaskStartedContext(string taskId = TaskId1, string? agentId = AgentId1)
    {
        return new TaskStartedContext(taskId, "Test task", "Expected output", agentId, "TestRole", DateTime.UtcNow);
    }

    private static TaskProgressContext CreateTaskProgressContext(string taskId = TaskId1, double progress = 50.0)
    {
        return new TaskProgressContext(taskId, AgentId1, 5, 10, progress, "Current action", DateTime.UtcNow);
    }

    private static TaskCompletedContext CreateTaskCompletedContext(string taskId = TaskId1, bool success = true)
    {
        return new TaskCompletedContext(
            taskId, AgentId1, new TaskExecutionOutcome(success, success ? "Success output" : null,
            success ? new { result = "ok" } : null,
            success ? null : "Error occurred"),
            TimeSpan.FromMinutes(2), 10, DateTime.UtcNow);
    }

    #endregion

    #region Constructor and Property Tests

    [Fact]
    public void ShouldHaveNullCallbacks_WhenConstructingWithDefaultValues()
    {
        // Act
        var callbacks = new TaskCallbacks();

        // Assert
        Assert.Null(callbacks.OnStarted);
        Assert.Null(callbacks.OnProgress);
        Assert.Null(callbacks.OnCompleted);
        Assert.Null(callbacks.OnFailed);
        Assert.Null(callbacks.OnFinally);
    }

    [Fact]
    public void ShouldSetCallbacks_WhenConstructingWithInitProperties()
    {
        // Arrange
        var executionLog = new List<string>();

        Func<TaskStartedContext, System.Threading.Tasks.Task> onStarted = async ctx =>
        {
            executionLog.Add("Started");
            await System.Threading.Tasks.Task.CompletedTask;
        };

        Func<TaskProgressContext, System.Threading.Tasks.Task> onProgress = async ctx =>
        {
            executionLog.Add($"Progress: {ctx.ProgressPercentage}");
            await System.Threading.Tasks.Task.CompletedTask;
        };

        // Act
        var callbacks = new TaskCallbacks
        {
            OnStarted = onStarted,
            OnProgress = onProgress
        };

        // Assert
        Assert.NotNull(callbacks.OnStarted);
        Assert.NotNull(callbacks.OnProgress);
        Assert.Null(callbacks.OnCompleted);
        Assert.Null(callbacks.OnFailed);
        Assert.Null(callbacks.OnFinally);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteCorrectly_WhenAccessingPropertiesWhenSet()
    {
        // Arrange
        var executionLog = new List<string>();
        var callbacks = new TaskCallbacks
        {
            OnStarted = async ctx =>
            {
                executionLog.Add($"Started: {ctx.TaskId}");
                await System.Threading.Tasks.Task.Delay(1);
            },
            OnProgress = async ctx =>
            {
                executionLog.Add($"Progress: {ctx.ProgressPercentage}%");
                await System.Threading.Tasks.Task.Delay(1);
            },
            OnCompleted = async ctx =>
            {
                executionLog.Add($"Completed: {ctx.Success}");
                await System.Threading.Tasks.Task.Delay(1);
            }
        };

        // Act
        await callbacks.OnStarted!(CreateTaskStartedContext());
        await callbacks.OnProgress!(CreateTaskProgressContext(TaskId1, 75.0));
        await callbacks.OnCompleted!(CreateTaskCompletedContext());

        // Assert
        Assert.Equal(3, executionLog.Count);
        Assert.Contains("Started: task-1", executionLog);
        Assert.Contains("Progress: 75%", executionLog);
        Assert.Contains("Completed: True", executionLog);
    }

    #endregion

    #region Create Method Tests

    [Fact]
    public void ShouldReturnCallbacksWithNullProperties_WhenCreatingWithAllNullParameters()
    {
        // Act
        var callbacks = TaskCallbacks.Create();

        // Assert
        Assert.Null(callbacks.OnStarted);
        Assert.Null(callbacks.OnProgress);
        Assert.Null(callbacks.OnCompleted);
        Assert.Null(callbacks.OnFailed);
        Assert.Null(callbacks.OnFinally);
    }

    [Fact]
    public void ShouldSetAllCallbacks_WhenCreatingWithAllParameters()
    {
        // Arrange
        var executionLog = new List<string>();

        Func<TaskStartedContext, System.Threading.Tasks.Task> onStarted = async ctx =>
        {
            executionLog.Add("Started");
            await System.Threading.Tasks.Task.CompletedTask;
        };

        Func<TaskProgressContext, System.Threading.Tasks.Task> onProgress = async ctx =>
        {
            executionLog.Add("Progress");
            await System.Threading.Tasks.Task.CompletedTask;
        };

        Func<TaskCompletedContext, System.Threading.Tasks.Task> onCompleted = async ctx =>
        {
            executionLog.Add(Completed);
            await System.Threading.Tasks.Task.CompletedTask;
        };

        Func<TaskCompletedContext, System.Threading.Tasks.Task> onFailed = async ctx =>
        {
            executionLog.Add(Failed);
            await System.Threading.Tasks.Task.CompletedTask;
        };

        Func<TaskCompletedContext, System.Threading.Tasks.Task> onFinally = async ctx =>
        {
            executionLog.Add("Finally");
            await System.Threading.Tasks.Task.CompletedTask;
        };

        // Act
        var callbacks = TaskCallbacks.Create(onStarted, onProgress, onCompleted, onFailed, onFinally);

        // Assert
        Assert.NotNull(callbacks.OnStarted);
        Assert.NotNull(callbacks.OnProgress);
        Assert.NotNull(callbacks.OnCompleted);
        Assert.NotNull(callbacks.OnFailed);
        Assert.NotNull(callbacks.OnFinally);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSetOnlyProvidedCallbacks_WhenCreatingWithPartialParameters()
    {
        // Arrange
        var executionLog = new List<string>();

        // Act
        var callbacks = TaskCallbacks.Create(
            onStarted: async ctx =>
            {
                executionLog.Add("Started");
                await System.Threading.Tasks.Task.CompletedTask;
            },
            onCompleted: async ctx =>
            {
                executionLog.Add(Completed);
                await System.Threading.Tasks.Task.CompletedTask;
            });

        // Assert
        Assert.NotNull(callbacks.OnStarted);
        Assert.Null(callbacks.OnProgress);
        Assert.NotNull(callbacks.OnCompleted);
        Assert.Null(callbacks.OnFailed);
        Assert.Null(callbacks.OnFinally);

        // Test execution
        await callbacks.OnStarted!(CreateTaskStartedContext());
        await callbacks.OnCompleted!(CreateTaskCompletedContext());

        Assert.Equal(2, executionLog.Count);
        Assert.Contains("Started", executionLog);
        Assert.Contains(Completed, executionLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteAsynchronously_WhenCreatingWithAsyncCallbacks()
    {
        // Arrange - the invariant is that an async callback is awaited to completion, not that
        // it took a given number of milliseconds. The previous version slept 2 x 50 ms and
        // asserted a >= 90 ms wall-clock span, which a loaded machine could round below the
        // bound; each callback now suspends with a real yield and records its own step, so the
        // ordering is observed instead of timed.
        var executionOrder = new List<string>();
        var startedFinished = false;
        var progressFinished = false;

        var callbacks = TaskCallbacks.Create(
            onStarted: async ctx =>
            {
                // Task.Yield forces a genuine suspension: the returned task cannot be
                // already-completed, so completing it proves the caller awaited the body.
                await System.Threading.Tasks.Task.Yield();
                executionOrder.Add("started");
                startedFinished = true;
            },
            onProgress: async ctx =>
            {
                await System.Threading.Tasks.Task.Yield();
                executionOrder.Add("progress");
                progressFinished = true;
            });

        // Act & Assert - each await must observe its callback's body already finished.
        await callbacks.OnStarted!(CreateTaskStartedContext());
        Assert.True(startedFinished);
        Assert.False(progressFinished);

        await callbacks.OnProgress!(CreateTaskProgressContext());
        Assert.True(progressFinished);

        // Sequential execution, in the order the caller invoked them.
        Assert.Collection(
            executionOrder,
            first => Assert.Equal("started", first),
            second => Assert.Equal("progress", second));
    }

    #endregion

    #region CreateActions Method Tests

    [Fact]
    public void ShouldReturnCallbacksWithNullProperties_WhenCreatingActionsWithAllNullParameters()
    {
        // Act
        var callbacks = TaskCallbacks.CreateActions();

        // Assert
        Assert.Null(callbacks.OnStarted);
        Assert.Null(callbacks.OnProgress);
        Assert.Null(callbacks.OnCompleted);
        Assert.Null(callbacks.OnFailed);
        Assert.Null(callbacks.OnFinally);
    }

    [Fact]
    public void ShouldSetAllCallbacks_WhenCreatingActionsWithAllParameters()
    {
        // Arrange
        var executionLog = new List<string>();

        Action<TaskStartedContext> onStarted = ctx => executionLog.Add("Started");
        Action<TaskProgressContext> onProgress = ctx => executionLog.Add("Progress");
        Action<TaskCompletedContext> onCompleted = ctx => executionLog.Add(Completed);
        Action<TaskCompletedContext> onFailed = ctx => executionLog.Add(Failed);
        Action<TaskCompletedContext> onFinally = ctx => executionLog.Add("Finally");

        // Act
        var callbacks = TaskCallbacks.CreateActions(onStarted, onProgress, onCompleted, onFailed, onFinally);

        // Assert
        Assert.NotNull(callbacks.OnStarted);
        Assert.NotNull(callbacks.OnProgress);
        Assert.NotNull(callbacks.OnCompleted);
        Assert.NotNull(callbacks.OnFailed);
        Assert.NotNull(callbacks.OnFinally);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteSynchronously_WhenCreatingActionsWithActionCallbacks()
    {
        // Arrange
        var executionLog = new List<string>();

        var callbacks = TaskCallbacks.CreateActions(
            onStarted: ctx => executionLog.Add($"Started: {ctx.TaskId}"),
            onProgress: ctx => executionLog.Add($"Progress: {ctx.ProgressPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture)}%"),
            onCompleted: ctx => executionLog.Add($"Completed: {ctx.Success}"));

        // Act
        await callbacks.OnStarted!(CreateTaskStartedContext("task-123"));
        await callbacks.OnProgress!(CreateTaskProgressContext("task-123", 85.5));
        await callbacks.OnCompleted!(CreateTaskCompletedContext("task-123", true));

        // Assert
        Assert.Equal(3, executionLog.Count);
        Assert.Contains("Started: task-123", executionLog);
        Assert.Contains("Progress: 85.5%", executionLog);
        Assert.Contains("Completed: True", executionLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSetOnlyProvidedCallbacks_WhenCreatingActionsWithPartialParameters()
    {
        // Arrange
        var executionLog = new List<string>();

        // Act
        var callbacks = TaskCallbacks.CreateActions(
            onStarted: ctx => executionLog.Add("Started"),
            onFailed: ctx => executionLog.Add(Failed));

        // Assert
        Assert.NotNull(callbacks.OnStarted);
        Assert.Null(callbacks.OnProgress);
        Assert.Null(callbacks.OnCompleted);
        Assert.NotNull(callbacks.OnFailed);
        Assert.Null(callbacks.OnFinally);

        // Test execution
        await callbacks.OnStarted!(CreateTaskStartedContext());
        await callbacks.OnFailed!(CreateTaskCompletedContext(TaskId1, false));

        Assert.Equal(2, executionLog.Count);
        Assert.Contains("Started", executionLog);
        Assert.Contains(Failed, executionLog);
    }

    [Fact]
    public void ShouldReturnCompletedTasks_WhenCreatingActions()
    {
        // Arrange
        var callbacks = TaskCallbacks.CreateActions(
            onStarted: ctx => { /* Do nothing */ },
            onProgress: ctx => { /* Do nothing */ });

        // Act
        var startedTask = callbacks.OnStarted!(CreateTaskStartedContext());
        var progressTask = callbacks.OnProgress!(CreateTaskProgressContext());

        // Assert
        Assert.True(startedTask.IsCompletedSuccessfully);
        Assert.True(progressTask.IsCompletedSuccessfully);
        Assert.Equal(TaskStatus.RanToCompletion, startedTask.Status);
        Assert.Equal(TaskStatus.RanToCompletion, progressTask.Status);
    }

    #endregion

    #region Task Lifecycle Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteInCorrectOrder_WhenUsingTaskCallbacksWithFullLifecycle()
    {
        // Arrange
        var executionOrder = new List<string>();
        var callbacks = TaskCallbacks.Create(
            onStarted: async ctx =>
            {
                executionOrder.Add("1-Started");
                await System.Threading.Tasks.Task.CompletedTask;
            },
            onProgress: async ctx =>
            {
                executionOrder.Add($"2-Progress-{ctx.ProgressPercentage}");
                await System.Threading.Tasks.Task.CompletedTask;
            },
            onCompleted: async ctx =>
            {
                executionOrder.Add("3-Completed");
                await System.Threading.Tasks.Task.CompletedTask;
            },
            onFinally: async ctx =>
            {
                executionOrder.Add("4-Finally");
                await System.Threading.Tasks.Task.CompletedTask;
            });

        // Act - Simulate task lifecycle
        await callbacks.OnStarted!(CreateTaskStartedContext());
        await callbacks.OnProgress!(CreateTaskProgressContext(TaskId1, 25.0));
        await callbacks.OnProgress!(CreateTaskProgressContext(TaskId1, 75.0));
        await callbacks.OnCompleted!(CreateTaskCompletedContext(TaskId1, true));
        await callbacks.OnFinally!(CreateTaskCompletedContext(TaskId1, true));

        // Assert
        Assert.Equal(5, executionOrder.Count);
        Assert.Equal("1-Started", executionOrder[0]);
        Assert.Equal("2-Progress-25", executionOrder[1]);
        Assert.Equal("2-Progress-75", executionOrder[2]);
        Assert.Equal("3-Completed", executionOrder[3]);
        Assert.Equal("4-Finally", executionOrder[4]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteFailedAndFinally_WhenUsingTaskCallbacksFailureLifecycle()
    {
        // Arrange
        var executionOrder = new List<string>();
        var callbacks = TaskCallbacks.Create(
            onStarted: async ctx =>
            {
                executionOrder.Add("Started");
                await System.Threading.Tasks.Task.CompletedTask;
            },
            onCompleted: async ctx =>
            {
                executionOrder.Add(Completed); // Should not be called for failed task
                await System.Threading.Tasks.Task.CompletedTask;
            },
            onFailed: async ctx =>
            {
                executionOrder.Add($"Failed: {ctx.Error}");
                await System.Threading.Tasks.Task.CompletedTask;
            },
            onFinally: async ctx =>
            {
                executionOrder.Add("Finally");
                await System.Threading.Tasks.Task.CompletedTask;
            });

        var failedContext = new TaskCompletedContext(
            TaskId1, AgentId1, new TaskExecutionOutcome(false, null, null, "Task execution failed"),
            TimeSpan.FromMinutes(1), 5, DateTime.UtcNow);

        // Act - Simulate failed task lifecycle
        await callbacks.OnStarted!(CreateTaskStartedContext());
        // Simulate logic: if (context.Success) onCompleted else onFailed
        if (failedContext.Success)
        {
            await callbacks.OnCompleted!(failedContext);
        }
        else
        {
            await callbacks.OnFailed!(failedContext);
        }
        await callbacks.OnFinally!(failedContext);

        // Assert
        Assert.Equal(3, executionOrder.Count);
        Assert.Contains("Started", executionOrder);
        Assert.Contains("Failed: Task execution failed", executionOrder);
        Assert.Contains("Finally", executionOrder);
        Assert.DoesNotContain(Completed, executionOrder);
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenUsingTaskCallbacksWithThrowingCallback()
    {
        // Arrange
        var callbacks = TaskCallbacks.Create(
            onStarted: ctx => throw new InvalidOperationException("Callback failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            callbacks.OnStarted!(CreateTaskStartedContext()));

        Assert.Equal("Callback failed", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenCreatingActionsWithThrowingAction()
    {
        // Arrange
        var callbacks = TaskCallbacks.CreateActions(
            onStarted: ctx => throw new ArgumentException("Action failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            callbacks.OnStarted!(CreateTaskStartedContext()));

        Assert.Equal("Action failed", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateCorrectly_WhenUsingTaskCallbacksWithAsyncException()
    {
        // Arrange
        var callbacks = TaskCallbacks.Create(
            onProgress: async ctx =>
            {
                await System.Threading.Tasks.Task.Delay(10);
                throw new TimeoutException("Async callback timeout");
            });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            callbacks.OnProgress!(CreateTaskProgressContext()));

        Assert.Equal("Async callback timeout", exception.Message);
    }

    #endregion

    #region Edge Cases and Integration Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenUsingTaskCallbacksWithNullContexts()
    {
        // Arrange
        var executionCount = 0;
        var callbacks = TaskCallbacks.Create(
            onStarted: async ctx =>
            {
                executionCount++;
                await System.Threading.Tasks.Task.CompletedTask;
            });

        // Act & Assert - Should handle null context without throwing NullReferenceException
        // The callback implementation should handle this appropriately
        await callbacks.OnStarted!(null!);

        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingTaskCallbacksWithManyCallbacks()
    {
        // Arrange
        var callbackCount = 100;
        var executionLog = new List<int>();

        var callbacks = TaskCallbacks.Create(
            onProgress: async ctx =>
            {
                executionLog.Add((int)ctx.ProgressPercentage);
                await System.Threading.Tasks.Task.CompletedTask;
            });

        // Act - Execute many callbacks
        var tasks = new List<System.Threading.Tasks.Task>();
        for (int i = 0; i < callbackCount; i++)
        {
            var progress = (double)i / callbackCount * 100;
            tasks.Add(callbacks.OnProgress!(CreateTaskProgressContext(TaskId1, progress)));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(callbackCount, executionLog.Count);
        Assert.Equal(0, executionLog.Min());
        Assert.True(executionLog.Max() < 100); // Last one should be 99% (99/100 * 100)
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingTaskCallbacksWithConcurrentExecution()
    {
        // Arrange
        var executionTimes = new List<DateTime>();
        var lockObject = new object();

        var callbacks = TaskCallbacks.Create(
            onProgress: async ctx =>
            {
                await System.Threading.Tasks.Task.Delay(10); // Simulate work
                lock (lockObject)
                {
                    executionTimes.Add(DateTime.UtcNow);
                }
            });

        // Act - Execute callbacks concurrently
        var tasks = new[]
        {
            callbacks.OnProgress!(CreateTaskProgressContext(TaskId1, 25.0)),
            callbacks.OnProgress!(CreateTaskProgressContext(TaskId2, 50.0)),
            callbacks.OnProgress!(CreateTaskProgressContext(TaskId3, 75.0))
        };

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(3, executionTimes.Count);
        // Times should be relatively close (concurrent execution)
        var maxTime = executionTimes.Max();
        var minTime = executionTimes.Min();
        var timeDifference = maxTime - minTime;
        Assert.True(timeDifference.TotalMilliseconds < 100); // Should complete within 100ms of each other
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingTaskCallbacksWithComplexContextData()
    {
        // Arrange
        var capturedContexts = new List<object>();

        var callbacks = TaskCallbacks.Create(
            onCompleted: async ctx =>
            {
                capturedContexts.Add(new
                {
                    TaskId = ctx.TaskId,
                    Success = ctx.Success,
                    Duration = ctx.Duration,
                    StepsExecuted = ctx.StepsExecuted,
                    OutputType = ctx.StructuredOutput?.GetType().Name
                });
                await System.Threading.Tasks.Task.CompletedTask;
            });

        var complexContext = new TaskCompletedContext(
            "complex-task-123",
            "advanced-agent",
            new TaskExecutionOutcome(true, "Complex output with detailed results",
                new { Results = Int123, Metadata = "test" }, null),
            TimeoutStandard,
            25,
            DateTime.UtcNow);

        // Act
        await callbacks.OnCompleted!(complexContext);

        // Assert
        Assert.Single(capturedContexts);
        var captured = capturedContexts[0];
        Assert.NotNull(captured);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotAllowModificationAfterCreation_WhenUsingTaskCallbacksInitOnlyProperties()
    {
        // Arrange
        var callbacks = TaskCallbacks.Create(
            onStarted: async ctx => await System.Threading.Tasks.Task.CompletedTask);

        // Act & Assert
        // Properties are init-only, so they cannot be modified after object creation
        // This is enforced by the compiler, so we just verify they're set correctly
        Assert.NotNull(callbacks.OnStarted);
        Assert.Null(callbacks.OnProgress);
        Assert.Null(callbacks.OnCompleted);
        Assert.Null(callbacks.OnFailed);
        Assert.Null(callbacks.OnFinally);

        // Verify the callback works
        await callbacks.OnStarted!(CreateTaskStartedContext());
    }

    #endregion
}
