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
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenUsingOnTaskCompletedAsyncWithNullContext()
    {
        // Act & Assert - Should handle null gracefully
        await _handler.OnTaskCompletedAsync(null!, TestContext.Current.CancellationToken);

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
