using Orkeon.Application.Callback;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Callbacks;

public class ExampleCallbacksTests
{
    #region MetricsCallbackHandler Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRecordStartTime_WhenUsingMetricsCallbackHandlerOnTaskStartedAsync()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();
        var context = new TaskStartedContext(
            TaskId: "task-001",
            Description: "Test task",
            ExpectedOutput: "Test output",
            AgentId: AgentId1,
            AgentRole: RoleWorker,
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnTaskStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        // We can't directly verify internal state, but we can verify no exceptions thrown
        var metrics = handler.GetMetrics();
        Assert.Empty(metrics); // No metrics until task completes
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUpdateStepNumber_WhenUsingMetricsCallbackHandlerOnTaskProgressAsync()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();
        var startContext = new TaskStartedContext(
            TaskId: "task-002",
            Description: "Test task",
            ExpectedOutput: "Test output",
            AgentId: AgentId1,
            AgentRole: RoleWorker,
            Timestamp: DateTime.UtcNow);

        var progressContext = new TaskProgressContext(
            TaskId: "task-002",
            AgentId: AgentId1,
            StepNumber: 5,
            TotalSteps: 10,
            ProgressPercentage: 50.0,
            CurrentAction: "Processing",
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnTaskStartedAsync(startContext, TestContext.Current.CancellationToken);
        await handler.OnTaskProgressAsync(progressContext, TestContext.Current.CancellationToken);

        // Assert
        // Progress tracking is internal, but we verify no exceptions
        var metrics = handler.GetMetrics();
        Assert.Empty(metrics); // Still no metrics until completion
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateMetric_WhenUsingMetricsCallbackHandlerOnTaskCompletedAsync()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();
        var startTime = DateTime.UtcNow;

        var startContext = new TaskStartedContext(
            TaskId: "task-003",
            Description: "Test task",
            ExpectedOutput: "Test output",
            AgentId: AgentId1,
            AgentRole: RoleWorker,
            Timestamp: startTime);

        var completeContext = new TaskCompletedContext(
            TaskId: "task-003",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Completed successfully", null, null),
            Duration: TimeoutQuick,
            StepsExecuted: 10,
            Timestamp: startTime.AddSeconds(30));

        // Act
        await handler.OnTaskStartedAsync(startContext, TestContext.Current.CancellationToken);
        await handler.OnTaskCompletedAsync(completeContext, TestContext.Current.CancellationToken);

        // Assert
        var metrics = handler.GetMetrics();
        Assert.Single(metrics);

        var metric = metrics[0];
        Assert.Equal("task-003", metric.TaskId);
        Assert.True(metric.Success);
        Assert.Equal(30, metric.Duration.TotalSeconds);
        Assert.Equal(10, metric.StepsExecuted);
        Assert.Equal(startTime, metric.StartTime);
        Assert.Equal(startTime.AddSeconds(30), metric.EndTime);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnLastCompletedTask_WhenUsingMetricsCallbackHandlerGettingLatestMetric()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();

        // Complete first task
        await CompleteTaskWithMetrics(handler, TaskId1, true, 10);
        await System.Threading.Tasks.Task.Delay(10, TestContext.Current.CancellationToken); // Small delay to ensure different timestamps

        // Complete second task
        await CompleteTaskWithMetrics(handler, TaskId2, false, 20);

        // Act
        var latestMetric = handler.GetLatestMetric();

        // Assert
        Assert.NotNull(latestMetric);
        Assert.Equal(TaskId2, latestMetric.TaskId);
        Assert.False(latestMetric.Success);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCalculateCorrectly_WhenUsingMetricsCallbackHandlerGettingAverageExecutionTime()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();

        await CompleteTaskWithMetrics(handler, TaskId1, true, 10);
        await CompleteTaskWithMetrics(handler, TaskId2, true, 20);
        await CompleteTaskWithMetrics(handler, TaskId3, true, 30);

        // Act
        var avgTime = handler.GetAverageExecutionTime();

        // Assert
        Assert.Equal(20.0, avgTime); // (10 + 20 + 30) / 3
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCalculateCorrectly_WhenUsingMetricsCallbackHandlerGettingSuccessRate()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();

        await CompleteTaskWithMetrics(handler, TaskId1, true, 10);
        await CompleteTaskWithMetrics(handler, TaskId2, false, 20);
        await CompleteTaskWithMetrics(handler, TaskId3, true, 30);
        await CompleteTaskWithMetrics(handler, "task-4", true, 40);

        // Act
        var successRate = handler.GetSuccessRate();

        // Assert
        Assert.Equal(0.75, successRate); // 3/4 = 0.75
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingMetricsCallbackHandlerGettingAverageExecutionTimeWithNoMetrics()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();

        // Act
        var avgTime = handler.GetAverageExecutionTime();

        // Assert
        Assert.Equal(0, avgTime);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingMetricsCallbackHandlerGettingSuccessRateWithNoMetrics()
    {
        // Arrange
        var handler = new MetricsCallbackHandler();

        // Act
        var successRate = handler.GetSuccessRate();

        // Assert
        Assert.Equal(0, successRate);
    }

    #endregion

    #region BusinessLogicCallbackHandler Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogStartEvent_WhenUsingBusinessLogicCallbackHandlerOnTaskStartedAsync()
    {
        // Arrange
        var handler = new BusinessLogicCallbackHandler();
        var context = new TaskStartedContext(
            TaskId: "task-100",
            Description: "Process order",
            ExpectedOutput: "Order processed",
            AgentId: AgentId1,
            AgentRole: "OrderProcessor",
            Timestamp: new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        // Act
        await handler.OnTaskStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var log = handler.GetExecutionLog();
        Assert.Single(log);
        Assert.Contains("[12:00:00]", log[0]);
        Assert.Contains("Process order", log[0]);
        Assert.Contains("OrderProcessor", log[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotify_WhenUsingBusinessLogicCallbackHandlerOnTaskStartedAsyncWithCriticalTask()
    {
        // Arrange
        var handler = new BusinessLogicCallbackHandler();
        var context = new TaskStartedContext(
            TaskId: "task-critical",
            Description: "CRITICAL: System failure recovery",
            ExpectedOutput: "System recovered",
            AgentId: AgentId1,
            AgentRole: "SystemAdmin",
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnTaskStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var log = handler.GetExecutionLog();
        Assert.Equal(2, log.Count);
        Assert.Contains("[NOTIFICATION]", log[1]);
        Assert.Contains("Critical task started", log[1]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLog_WhenUsingBusinessLogicCallbackHandlerOnTaskCompletedAsyncWithSuccess()
    {
        // Arrange
        var handler = new BusinessLogicCallbackHandler();
        var context = new TaskCompletedContext(
            TaskId: "task-200",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Task completed", null, null),
            Duration: TimeSpan.FromMinutes(2),
            StepsExecuted: 5,
            Timestamp: new DateTime(2024, 1, 1, 12, 5, 0, DateTimeKind.Utc));

        // Act
        await handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var log = handler.GetExecutionLog();
        Assert.Single(log);
        Assert.Contains("[12:05:00]", log[0]);
        Assert.Contains("task-200", log[0]);
        Assert.Contains("Success: True", log[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleFailure_WhenUsingBusinessLogicCallbackHandlerOnTaskCompletedAsyncWithFailure()
    {
        // Arrange
        var handler = new BusinessLogicCallbackHandler();
        var context = new TaskCompletedContext(
            TaskId: "task-fail",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(false, null, null, "Connection timeout"),
            Duration: TimeoutQuick,
            StepsExecuted: 2,
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var log = handler.GetExecutionLog();
        Assert.Equal(2, log.Count);
        Assert.Contains("Success: False", log[0]);
        Assert.Contains("[FAILURE_HANDLER]", log[1]);
        Assert.Contains("Connection timeout", log[1]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleSlowTask_WhenUsingBusinessLogicCallbackHandlerOnTaskCompletedAsyncWithSlowTask()
    {
        // Arrange
        var handler = new BusinessLogicCallbackHandler();
        var context = new TaskCompletedContext(
            TaskId: "task-slow",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, Completed, null, null),
            Duration: TimeSpan.FromMinutes(6),
            StepsExecuted: 100,
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var log = handler.GetExecutionLog();
        Assert.Equal(2, log.Count);
        Assert.Contains("[SLOW_TASK]", log[1]);
        Assert.Contains("6.0 minutes", log[1]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleStepFailure_WhenUsingBusinessLogicCallbackHandlerOnStepCompletedAsyncWithFailure()
    {
        // Arrange
        var handler = new BusinessLogicCallbackHandler();
        var context = new StepCompletedContext(
            Step: new StepIdentity(AgentId1, RoleWorker, "task-300"),
            Action: "Connect to database",
            Thought: "Need to connect to database",
            Observation: "Connection refused",
            Success: false,
            Duration: TimeSpan.FromSeconds(5),
            Timestamp: new DateTime(2024, 1, 1, 12, 10, 0, DateTimeKind.Utc));

        // Act
        await handler.OnStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var log = handler.GetExecutionLog();
        Assert.Equal(2, log.Count);
        Assert.Contains("[12:10:00] Step failed", log[0]);
        Assert.Contains("Connect to database", log[0]);
        Assert.Contains("Connection refused", log[0]);
        Assert.Contains("[STEP_FAILURE]", log[1]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotLog_WhenUsingBusinessLogicCallbackHandlerOnStepCompletedAsyncWithSuccess()
    {
        // Arrange
        var handler = new BusinessLogicCallbackHandler();
        var context = new StepCompletedContext(
            Step: new StepIdentity(AgentId1, RoleWorker, "task-400"),
            Action: "Process data",
            Thought: "Processing",
            Observation: "Data processed successfully",
            Success: true,
            Duration: TimeSpan.FromSeconds(2),
            Timestamp: DateTime.UtcNow);

        // Act
        await handler.OnStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var log = handler.GetExecutionLog();
        Assert.Empty(log); // Successful steps are not logged
    }

    #endregion

    #region ConsoleCallbackHandler Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldWriteToConsole_WhenUsingConsoleCallbackHandlerOnTaskStartedAsync()
    {
        // Arrange
        var handler = new ConsoleCallbackHandler();
        var originalConsoleOut = Console.Out;
        using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        try
        {
            var context = new TaskStartedContext(
                TaskId: "console-task-1",
                Description: "Test console output",
                ExpectedOutput: "Output",
                AgentId: AgentId1,
                AgentRole: "Tester",
                Timestamp: DateTime.UtcNow);

            // Act
            await handler.OnTaskStartedAsync(context, TestContext.Current.CancellationToken);

            var output = consoleOutput.ToString();

            // Assert
            Assert.Contains("🚀 Task Started: Test console output", output);
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldWriteProgressToConsole_WhenUsingConsoleCallbackHandlerOnTaskProgressAsync()
    {
        // Arrange
        var handler = new ConsoleCallbackHandler();
        var originalConsoleOut = Console.Out;
        using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        try
        {
            var context = new TaskProgressContext(
                TaskId: "console-task-2",
                AgentId: AgentId1,
                StepNumber: 7,
                TotalSteps: 10,
                ProgressPercentage: 70.0,
                CurrentAction: "Analyzing data",
                Timestamp: DateTime.UtcNow);

            // Act
            await handler.OnTaskProgressAsync(context, TestContext.Current.CancellationToken);

            var output = consoleOutput.ToString();

            // Assert
            Assert.Contains("⚡ Progress: 70%", output);
            Assert.Contains("Analyzing data", output);
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldShowSuccessEmoji_WhenUsingConsoleCallbackHandlerOnTaskCompletedAsyncWithSuccess()
    {
        // Arrange
        var handler = new ConsoleCallbackHandler();
        var originalConsoleOut = Console.Out;
        using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        try
        {
            var context = new TaskCompletedContext(
            TaskId: "console-task-3",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Done", null, null),
            Duration: TimeSpan.FromSeconds(15.5),
            StepsExecuted: 5,
            Timestamp: DateTime.UtcNow);

            // Act
            await handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

            var output = consoleOutput.ToString();

            // Assert
            Assert.Contains("✅ Task Completed: 15.5s", output);
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldShowErrorDetails_WhenUsingConsoleCallbackHandlerOnTaskCompletedAsyncWithFailure()
    {
        // Arrange
        var handler = new ConsoleCallbackHandler();
        var originalConsoleOut = Console.Out;
        using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        try
        {
            var context = new TaskCompletedContext(
            TaskId: "console-task-fail",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(false, null, null, "Database connection failed"),
            Duration: TimeSpan.FromSeconds(5.2),
            StepsExecuted: 2,
            Timestamp: DateTime.UtcNow);

            // Act
            await handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

            var output = consoleOutput.ToString();

            // Assert
            Assert.Contains("❌ Task Failed: 5.2s", output);
            Assert.Contains("Error: Database connection failed", output);
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldShowCheckmark_WhenUsingConsoleCallbackHandlerOnStepCompletedAsyncWithSuccess()
    {
        // Arrange
        var handler = new ConsoleCallbackHandler();
        var originalConsoleOut = Console.Out;
        await using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        try
        {
            var context = new StepCompletedContext(
                Step: new StepIdentity(AgentId1, RoleWorker, "task-step"),
                Action: "Load configuration",
                Thought: "Loading config",
                Observation: "Configuration loaded successfully",
                Success: true,
                Duration: TimeSpan.FromMilliseconds(500),
                Timestamp: DateTime.UtcNow);

            // Act
            await handler.OnStepCompletedAsync(context, TestContext.Current.CancellationToken);

            var output = consoleOutput.ToString();

            // Assert
            Assert.Contains("  ✓ Load configuration: Configuration loaded successfully", output);
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldShowCross_WhenUsingConsoleCallbackHandlerOnStepCompletedAsyncWithFailure()
    {
        // Arrange
        var handler = new ConsoleCallbackHandler();
        var originalConsoleOut = Console.Out;
        using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        try
        {
            var context = new StepCompletedContext(
                Step: new StepIdentity(AgentId1, RoleWorker, "task-step-fail"),
                Action: "Parse JSON",
                Thought: "Parsing data",
                Observation: "Invalid JSON format",
                Success: false,
                Duration: TimeSpan.FromMilliseconds(100),
                Timestamp: DateTime.UtcNow);

            // Act
            await handler.OnStepCompletedAsync(context, TestContext.Current.CancellationToken);

            var output = consoleOutput.ToString();

            // Assert
            Assert.Contains("  ✗ Parse JSON: Invalid JSON format", output);
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    #endregion

    #region Helper Methods

    private static async System.Threading.Tasks.Task CompleteTaskWithMetrics(MetricsCallbackHandler handler, string taskId, bool success, int durationSeconds)
    {
        var startTime = DateTime.UtcNow;

        var startContext = new TaskStartedContext(
            TaskId: taskId,
            Description: $"Task {taskId}",
            ExpectedOutput: "Output",
            AgentId: AgentId1,
            AgentRole: RoleWorker,
            Timestamp: startTime);

        var completeContext = new TaskCompletedContext(
            TaskId: taskId,
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(success, success ? Success : null, null, success ? null : "Task failed"),
            Duration: TimeSpan.FromSeconds(durationSeconds),
            StepsExecuted: durationSeconds / 2,
            Timestamp: startTime.AddSeconds(durationSeconds));

        await handler.OnTaskStartedAsync(startContext);
        await handler.OnTaskCompletedAsync(completeContext);
    }

    #endregion
}
