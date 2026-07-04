using Orkeon.Application.Callback;
using Microsoft.Extensions.Logging;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Callbacks;

public class LoggingCallbackHandlerTests
{
    private readonly TestLogger<LoggingCallbackHandler> _logger;
    private readonly LoggingCallbackHandler _handler;

    public LoggingCallbackHandlerTests()
    {
        _logger = new TestLogger<LoggingCallbackHandler>();
        _handler = new LoggingCallbackHandler(_logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnStepStartedAsync()
    {
        // Arrange
        var context = new StepStartedContext(
            AgentId: AgentId1,
            AgentRole: "DataAnalyst",
            TaskId: "task-100",
            Action: "Analyze sales data",
            Thought: "I need to aggregate the data by region",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnStepStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Step Started", message);
        Assert.Contains("DataAnalyst", message);
        Assert.Contains("Analyze sales data", message);
        Assert.Contains("I need to aggregate the data by region", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnStepCompletedAsyncWithSuccess()
    {
        // Arrange
        var context = new StepCompletedContext(
            Step: new StepIdentity(AgentId1, RoleDeveloper, "task-200"),
            Action: "Implement feature",
            Thought: "Using design pattern",
            Observation: "Feature implemented successfully",
            Success: true,
            Duration: TimeSpan.FromMilliseconds(1500),
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Step Completed", message);
        Assert.Contains(RoleDeveloper, message);
        Assert.Contains("Implement feature", message);
        Assert.Contains("Feature implemented successfully", message);
        Assert.Contains("Success: True", message);
        Assert.Contains("1500", message); // Duration in milliseconds
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnTaskStartedAsync()
    {
        // Arrange
        var context = new TaskStartedContext(
            TaskId: "task-300",
            Description: "Generate quarterly report",
            ExpectedOutput: "PDF report with charts",
            AgentId: AgentId1,
            AgentRole: "ReportGenerator",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Task Started", message);
        Assert.Contains("Generate quarterly report", message);
        Assert.Contains("ReportGenerator", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnTaskProgressAsync()
    {
        // Arrange
        var context = new TaskProgressContext(
            TaskId: "task-400",
            AgentId: AgentId1,
            StepNumber: 3,
            TotalSteps: 5,
            ProgressPercentage: 60.0,
            CurrentAction: "Processing customer data",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskProgressAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Task Progress", message);
        Assert.Contains("task-400", message);
        Assert.Contains("Step 3/5", message);
        Assert.Contains("60%", message);
        Assert.Contains("Processing customer data", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnTaskCompletedAsyncWithSuccess()
    {
        // Arrange
        var context = new TaskCompletedContext(
            TaskId: "task-500",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Report generated", null, null),
            Duration: TimeSpan.FromMilliseconds(5000),
            StepsExecuted: 10,
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Task Completed", message);
        Assert.Contains("task-500", message);
        Assert.Contains("Success: True", message);
        Assert.Contains("5000", message); // Duration
        Assert.Contains("Steps: 10", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformationAndError_WhenUsingOnTaskCompletedAsyncWithFailure()
    {
        // Arrange
        var context = new TaskCompletedContext(
            TaskId: "task-600",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(false, null, null, "Database connection timeout"),
            Duration: TimeSpan.FromMilliseconds(30000),
            StepsExecuted: 5,
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var infoMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(infoMessages);
        var infoMessage = infoMessages.First();
        Assert.Contains("Task Completed", infoMessage);
        Assert.Contains("task-600", infoMessage);
        Assert.Contains("Success: False", infoMessage);

        var errorMessages = _logger.GetLogMessages(LogLevel.Error);
        Assert.Single(errorMessages);
        var errorMessage = errorMessages.First();
        Assert.Contains("Task Error", errorMessage);
        Assert.Contains("Database connection timeout", errorMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnFlowStepStartedAsync()
    {
        // Arrange
        var context = new FlowStepStartedContext(
            FlowExecutionId: "flow-exec-100",
            FlowName: "CustomerOnboarding",
            StepId: StepId1,
            StepName: "ValidateCustomerData",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnFlowStepStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Flow Step Started", message);
        Assert.Contains("CustomerOnboarding", message);
        Assert.Contains("ValidateCustomerData", message);
        Assert.Contains(StepId1, message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnFlowStepCompletedAsyncWithSuccess()
    {
        // Arrange
        var context = new FlowStepCompletedContext(
            FlowStep: new FlowStepIdentity("flow-exec-200", "OrderProcessing", "step-2", "CalculateShipping"),
            Success: true,
            Output: new { shippingCost = 15.99 },
            Error: null,
            Duration: TimeSpan.FromMilliseconds(250),
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnFlowStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Flow Step Completed", message);
        Assert.Contains("OrderProcessing", message);
        Assert.Contains("CalculateShipping", message);
        Assert.Contains("Success: True", message);
        Assert.Contains("250", message); // Duration
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformationAndError_WhenUsingOnFlowStepCompletedAsyncWithFailure()
    {
        // Arrange
        var context = new FlowStepCompletedContext(
            FlowStep: new FlowStepIdentity("flow-exec-300", "PaymentProcessing", "step-3", "ChargeCard"),
            Success: false,
            Output: null,
            Error: "Insufficient funds",
            Duration: TimeSpan.FromMilliseconds(1200),
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnFlowStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var infoMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(infoMessages);
        var infoMessage = infoMessages.First();
        Assert.Contains("Flow Step Completed", infoMessage);
        Assert.Contains("PaymentProcessing", infoMessage);
        Assert.Contains("ChargeCard", infoMessage);
        Assert.Contains("Success: False", infoMessage);

        var errorMessages = _logger.GetLogMessages(LogLevel.Error);
        Assert.Single(errorMessages);
        var errorMessage = errorMessages.First();
        Assert.Contains("Flow Step Error", errorMessage);
        Assert.Contains("Insufficient funds", errorMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectToken_WhenCallingAllMethodsWithCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Create various contexts
        var stepStartContext = new StepStartedContext(AgentId1, RoleWorker, TaskId1, "Action", "Thought", DateTime.UtcNow);
        var stepCompleteContext = new StepCompletedContext(new StepIdentity(AgentId1, RoleWorker, TaskId1), "Action", "Thought", "Observation", true, TimeSpan.FromSeconds(1), DateTime.UtcNow);
        var taskStartContext = new TaskStartedContext(TaskId1, "Description", "Output", AgentId1, RoleWorker, DateTime.UtcNow);
        var taskProgressContext = new TaskProgressContext(TaskId1, AgentId1, 1, 10, 10.0, "Working", DateTime.UtcNow);
        var taskCompleteContext = new TaskCompletedContext(TaskId1, AgentId1, new TaskExecutionOutcome(true, "Done", null, null), TimeSpan.FromSeconds(5), 10, DateTime.UtcNow);
        var flowStartContext = new FlowStepStartedContext(FlowId1, "Flow", StepId1, "Step", DateTime.UtcNow);
        var flowCompleteContext = new FlowStepCompletedContext(new FlowStepIdentity(FlowId1, "Flow", StepId1, "Step"), true, null, null, TimeSpan.FromSeconds(2), DateTime.UtcNow);

        // Act & Assert - All should complete without throwing even if token is cancelled
        await cts.CancelAsync();

        await _handler.OnStepStartedAsync(stepStartContext, cts.Token);
        await _handler.OnStepCompletedAsync(stepCompleteContext, cts.Token);
        await _handler.OnTaskStartedAsync(taskStartContext, cts.Token);
        await _handler.OnTaskProgressAsync(taskProgressContext, cts.Token);
        await _handler.OnTaskCompletedAsync(taskCompleteContext, cts.Token);
        await _handler.OnFlowStepStartedAsync(flowStartContext, cts.Token);
        await _handler.OnFlowStepCompletedAsync(flowCompleteContext, cts.Token);

        // Verify logs were still created despite cancellation
        var allLogs = _logger.GetAllLogMessages();
        Assert.Equal(7, allLogs.Count); // One log per method
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseStructuredLogging_WhenLoggingMessages()
    {
        // Arrange
        var context = new TaskProgressContext(
            TaskId: "structured-task",
            AgentId: "agent-structured",
            StepNumber: 7,
            TotalSteps: 10,
            ProgressPercentage: 70.5,
            CurrentAction: "Validating results",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskProgressAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logEntry = _logger.GetStructuredLogs().First();
        Assert.Equal("structured-task", logEntry.Parameters!["TaskId"]);
        Assert.Equal(7, logEntry.Parameters["StepNumber"]);
        Assert.Equal(10, logEntry.Parameters["TotalSteps"]);
        Assert.Equal(70.5, logEntry.Parameters["ProgressPercentage"]);
        Assert.Equal("Validating results", logEntry.Parameters["CurrentAction"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnStepStartedAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnStepStartedAsync(null!, TestContext.Current.CancellationToken);

        // Verify no logs were created
        var logs = _logger.GetAllLogMessages();
        Assert.Empty(logs);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnStepCompletedAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnStepCompletedAsync(null!, TestContext.Current.CancellationToken);

        // Verify no logs were created
        var logs = _logger.GetAllLogMessages();
        Assert.Empty(logs);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnTaskStartedAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnTaskStartedAsync(null!, TestContext.Current.CancellationToken);

        // Verify no logs were created
        var logs = _logger.GetAllLogMessages();
        Assert.Empty(logs);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnTaskProgressAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnTaskProgressAsync(null!, TestContext.Current.CancellationToken);

        // Verify no logs were created
        var logs = _logger.GetAllLogMessages();
        Assert.Empty(logs);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnTaskCompletedAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnTaskCompletedAsync(null!, TestContext.Current.CancellationToken);

        // Verify no logs were created
        var logs = _logger.GetAllLogMessages();
        Assert.Empty(logs);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnFlowStepStartedAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnFlowStepStartedAsync(null!, TestContext.Current.CancellationToken);

        // Verify no logs were created
        var logs = _logger.GetAllLogMessages();
        Assert.Empty(logs);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnFlowStepCompletedAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnFlowStepCompletedAsync(null!, TestContext.Current.CancellationToken);

        // Verify no logs were created
        var logs = _logger.GetAllLogMessages();
        Assert.Empty(logs);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogWithEmptyValues_WhenUsingOnStepStartedAsyncWithEmptyStrings()
    {
        // Arrange
        var context = new StepStartedContext(
            AgentId: "",
            AgentRole: "",
            TaskId: "",
            Action: "",
            Thought: "",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnStepStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        Assert.Contains("Step Started", logMessages.First());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogCorrectly_WhenUsingOnStepCompletedAsyncWithVeryLongDuration()
    {
        // Arrange
        var context = new StepCompletedContext(
            Step: new StepIdentity(AgentId1, RoleWorker, TaskId1),
            Action: "Long running task",
            Thought: "This will take a while",
            Observation: "Finally completed",
            Success: true,
            Duration: TimeSpan.FromHours(24),
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("86400000", message); // 24 hours in milliseconds
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLog_WhenUsingOnTaskProgressAsyncWith100PercentProgress()
    {
        // Arrange
        var context = new TaskProgressContext(
            TaskId: "task-complete",
            AgentId: AgentId1,
            StepNumber: 10,
            TotalSteps: 10,
            ProgressPercentage: 100.0,
            CurrentAction: "Finalizing",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskProgressAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("100%", message);
        Assert.Contains("Step 10/10", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLog_WhenUsingOnTaskProgressAsyncWithZeroProgress()
    {
        // Arrange
        var context = new TaskProgressContext(
            TaskId: "task-start",
            AgentId: AgentId1,
            StepNumber: 0,
            TotalSteps: 10,
            ProgressPercentage: 0.0,
            CurrentAction: "Initializing",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskProgressAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("0%", message);
        Assert.Contains("Step 0/10", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogInformation_WhenUsingOnTaskCompletedAsyncWithStructuredOutput()
    {
        // Arrange
        var structuredData = new { result = "processed", count = 42 };
        var context = new TaskCompletedContext(
            TaskId: "task-structured",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Plain output", structuredData, null),
            Duration: TimeSpan.FromSeconds(3),
            StepsExecuted: 5,
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Task Completed", message);
        Assert.Contains("Success: True", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLog_WhenUsingOnTaskCompletedAsyncWithZeroSteps()
    {
        // Arrange
        var context = new TaskCompletedContext(
            TaskId: "task-no-steps",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(true, "Immediate result", null, null),
            Duration: TimeSpan.FromMilliseconds(10),
            StepsExecuted: 0,
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Steps: 0", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogCorrectly_WhenUsingOnFlowStepStartedAsyncWithSpecialCharacters()
    {
        // Arrange
        var context = new FlowStepStartedContext(
            FlowExecutionId: "flow-<>&\"'",
            FlowName: "Flow with 特殊 characters",
            StepId: "step-@#$%",
            StepName: "Step\nWith\tSpecial\rChars",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnFlowStepStartedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Flow Step Started", message);
        Assert.Contains("Flow with 特殊 characters", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLog_WhenUsingOnFlowStepCompletedAsyncWithComplexOutput()
    {
        // Arrange
        var complexOutput = new
        {
            nested = new { level1 = new { level2 = "deep" } },
            array = new[] { 1, 2, 3 },
            dictionary = new Dictionary<string, object> { ["key"] = "value" }
        };

        var context = new FlowStepCompletedContext(
            FlowStep: new FlowStepIdentity("flow-complex", "ComplexFlow", "step-complex", "ProcessComplexData"),
            Success: true,
            Output: complexOutput,
            Error: null,
            Duration: TimeSpan.FromMilliseconds(456),
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnFlowStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Flow Step Completed", message);
        Assert.Contains("Success: True", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogWithoutObservation_WhenUsingOnStepCompletedAsyncWithNullObservation()
    {
        // Arrange
        var context = new StepCompletedContext(
            Step: new StepIdentity(AgentId1, RoleWorker, TaskId1),
            Action: "Action",
            Thought: "Thought",
            Observation: null!,
            Success: true,
            Duration: TimeSpan.FromSeconds(1),
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnStepCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Step Completed", message);
        Assert.DoesNotContain("null", message); // Should not log "null" string
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogAllCorrectly_WhenUsingMultipleCallbacksInSequence()
    {
        // Arrange
        var taskStart = new TaskStartedContext(TaskId1, "Description", "Output", AgentId1, RoleWorker, DateTime.UtcNow);
        var stepStart = new StepStartedContext(AgentId1, RoleWorker, TaskId1, "Action1", "Thought1", DateTime.UtcNow);
        var stepComplete = new StepCompletedContext(new StepIdentity(AgentId1, RoleWorker, TaskId1), "Action1", "Thought1", "Observation1", true, TimeSpan.FromSeconds(1), DateTime.UtcNow);
        var taskProgress = new TaskProgressContext(TaskId1, AgentId1, 1, 2, 50.0, "Halfway", DateTime.UtcNow);
        var taskComplete = new TaskCompletedContext(TaskId1, AgentId1, new TaskExecutionOutcome(true, "Done", null, null), TimeSpan.FromSeconds(5), 2, DateTime.UtcNow);

        // Act
        await _handler.OnTaskStartedAsync(taskStart, TestContext.Current.CancellationToken);
        await _handler.OnStepStartedAsync(stepStart, TestContext.Current.CancellationToken);
        await _handler.OnStepCompletedAsync(stepComplete, TestContext.Current.CancellationToken);
        await _handler.OnTaskProgressAsync(taskProgress, TestContext.Current.CancellationToken);
        await _handler.OnTaskCompletedAsync(taskComplete, TestContext.Current.CancellationToken);

        // Assert
        var allLogs = _logger.GetAllLogMessages();
        Assert.Equal(5, allLogs.Count);
        Assert.Contains("Task Started", allLogs[0]);
        Assert.Contains("Step Started", allLogs[1]);
        Assert.Contains("Step Completed", allLogs[2]);
        Assert.Contains("Task Progress", allLogs[3]);
        Assert.Contains("Task Completed", allLogs[4]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogFullError_WhenUsingOnTaskCompletedAsyncWithVeryLongError()
    {
        // Arrange
        var longError = new string('x', 10000);
        var context = new TaskCompletedContext(
            TaskId: "task-error",
            AgentId: AgentId1,
            Outcome: new TaskExecutionOutcome(false, null, null, longError),
            Duration: TimeSpan.FromSeconds(1),
            StepsExecuted: 1,
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskCompletedAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var errorMessages = _logger.GetLogMessages(LogLevel.Error);
        Assert.Single(errorMessages);
        var errorMessage = errorMessages.First();
        Assert.Contains(longError.Substring(0, 100), errorMessage); // At least part of the error
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillLog_WhenUsingOnTaskProgressAsyncWithNegativeProgress()
    {
        // Arrange (invalid but should handle gracefully)
        var context = new TaskProgressContext(
            TaskId: "task-negative",
            AgentId: AgentId1,
            StepNumber: -1,
            TotalSteps: -10,
            ProgressPercentage: -50.0,
            CurrentAction: "Invalid state",
            Timestamp: DateTime.UtcNow);

        // Act
        await _handler.OnTaskProgressAsync(context, TestContext.Current.CancellationToken);

        // Assert
        var logMessages = _logger.GetLogMessages(LogLevel.Information);
        Assert.Single(logMessages);
        var message = logMessages.First();
        Assert.Contains("Task Progress", message);
        Assert.Contains("-50%", message);
    }

    #region Test Logger Implementation

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _logEntries = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var entry = new LogEntry
            {
                LogLevel = logLevel,
                Message = message,
                Exception = exception,
                Timestamp = DateTime.UtcNow
            };

            // Extract structured logging parameters if available
            if (state is IReadOnlyList<KeyValuePair<string, object>> values)
            {
                entry.Parameters = values
                    .Where(kvp => kvp.Key != "{OriginalFormat}")
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            _logEntries.Add(entry);
        }

        public List<string> GetLogMessages(LogLevel logLevel)
        {
            return _logEntries
                .Where(e => e.LogLevel == logLevel)
                .Select(e => e.Message)
                .ToList();
        }

        public List<string> GetAllLogMessages()
        {
            return _logEntries.Select(e => e.Message).ToList();
        }

        public List<LogEntry> GetStructuredLogs()
        {
            return _logEntries.Where(e => e.Parameters != null && e.Parameters.Count > 0).ToList();
        }

        public class LogEntry
        {
            public LogLevel LogLevel { get; set; }
            public string Message { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; }
            public Dictionary<string, object>? Parameters { get; set; }
        }

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    #endregion
}
