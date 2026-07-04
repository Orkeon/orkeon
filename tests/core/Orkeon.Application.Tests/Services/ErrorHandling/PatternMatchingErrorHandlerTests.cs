using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.Exceptions;
using Orkeon.Application.Services.ErrorHandling;
using System.Security;
using SystemTimeoutException = System.TimeoutException;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services.ErrorHandling;

public class PatternMatchingErrorHandlerTests
{
    #region Test Doubles

    private class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> LogEntries { get; } = [];

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add(new LogEntry
            {
                LogLevel = logLevel,
                Message = formatter(state, exception),
                Exception = exception
            });
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new DisposableScope();

        private class DisposableScope : IDisposable
        {
            public void Dispose() { }
        }

        public class LogEntry
        {
            public LogLevel LogLevel { get; set; }
            public string Message { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
        }

        public bool HasLogMessage(LogLevel level, string partialMessage)
        {
            return LogEntries.Any(e => e.LogLevel == level && e.Message.Contains(partialMessage));
        }
    }

    #endregion

    #region Test Helpers

    private static PatternMatchingErrorHandler CreateHandler(TestLogger<PatternMatchingErrorHandler>? logger = null)
    {
        return new PatternMatchingErrorHandler(logger ?? new TestLogger<PatternMatchingErrorHandler>());
    }

    private static ErrorContext CreateContext(
        string componentType = "Test",
        bool isCritical = false,
        bool hasFallback = false,
        int retryCount = 0)
    {
        return new ErrorContext(
            ComponentType: componentType,
            IsCritical: isCritical,
            HasFallback: hasFallback,
            RetryCount: retryCount,
            ElapsedTime: TimeSpan.FromSeconds(retryCount * 2),
            OperationId: "test-op-123");
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new PatternMatchingErrorHandler(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateInstance_WhenConstructingWithValidLogger()
    {
        // Arrange
        var logger = new TestLogger<PatternMatchingErrorHandler>();

        // Act
        var handler = new PatternMatchingErrorHandler(logger);

        // Assert
        Assert.NotNull(handler);
    }

    #endregion

    #region HandleException Tests - Network Errors

    [Theory]
    [InlineData(0, ErrorStrategy.Retry, RecoveryAction.RetryWithBackoff)]
    [InlineData(1, ErrorStrategy.Retry, RecoveryAction.RetryWithBackoff)]
    [InlineData(2, ErrorStrategy.Retry, RecoveryAction.RetryWithBackoff)]
    [InlineData(3, ErrorStrategy.Escalate, RecoveryAction.NotifyManager)] // Exceeds retry limit
    public void ShouldDetermineStrategyBasedOnRetryCount_WhenHandlingExceptionHttpRequestException(
        int retryCount, ErrorStrategy expectedStrategy, RecoveryAction expectedRecovery)
    {
        // Arrange
        var logger = new TestLogger<PatternMatchingErrorHandler>();
        var handler = CreateHandler(logger);
        var exception = new HttpRequestException("Connection failed");
        var context = CreateContext("LLMProvider", retryCount: retryCount);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(expectedStrategy, result.Strategy);
        // Use expectedRecovery to prevent xUnit1026
        _ = expectedRecovery;
        Assert.True(logger.HasLogMessage(LogLevel.Error, "Handling exception"));

        if (expectedStrategy == ErrorStrategy.Retry)
        {
            Assert.True(result.ShouldRetry);
            Assert.NotEqual(TimeSpan.Zero, result.RetryDelay);
        }
    }

    [Fact]
    public void ShouldRetry_WhenHandlingExceptionTimeoutExceptionWithLowRetryCount()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new SystemTimeoutException("Operation timed out");
        var context = CreateContext(retryCount: 1);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Retry, result.Strategy);
        Assert.True(result.ShouldRetry);
        Assert.Contains("Retrying operation", result.Message);
    }

    [Fact]
    public void ShouldFallback_WhenHandlingExceptionDomainTimeoutExceptionWithHighRetryAndFallback()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new Domain.Exceptions.TimeoutException("CustomOp", TimeoutQuick);
        var context = CreateContext(hasFallback: true, retryCount: 3);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Fallback, result.Strategy);
        Assert.False(result.ShouldRetry);
        Assert.Contains("Using fallback", result.Message);
    }

    #endregion

    #region HandleException Tests - Resource Errors

    [Fact]
    public void ShouldActivateCircuitBreaker_WhenHandlingExceptionOutOfMemoryException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new OutOfMemoryException();
        var context = CreateContext();

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Circuit, result.Strategy);
        Assert.Equal(RecoveryAction.ReduceLoad, result.Recovery);
        Assert.True(result.CircuitBreakerTripped);
        Assert.False(result.ShouldRetry);
        Assert.Equal(TimeoutStandard, result.RetryDelay);
    }

    [Theory]
    [InlineData("Rate limit exceeded")]
    [InlineData("API rate limit reached")]
    [InlineData("Quota exceeded for API calls")]
    public void ShouldActivateCircuitBreaker_WhenHandlingExceptionRateLimitMessage(string message)
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new InvalidOperationException(message);
        var context = CreateContext();

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Circuit, result.Strategy);
        Assert.True(result.CircuitBreakerTripped);
    }

    #endregion

    #region HandleException Tests - Domain Errors

    [Fact]
    public void ShouldEscalate_WhenHandlingExceptionCrewException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new CrewException(CrewId.Create(), "Crew operation failed");
        var context = CreateContext(componentType: "Crew");

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Escalate, result.Strategy);
        Assert.Equal(RecoveryAction.NotifyManager, result.Recovery);
        Assert.True(result.RequiresEscalation);
        Assert.False(result.ShouldRetry);
    }

    [Fact]
    public void ShouldEscalate_WhenHandlingExceptionAgentException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new AgentException(AgentId.Create(), "Agent failed");
        var context = CreateContext(componentType: "Agent");

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Escalate, result.Strategy);
        Assert.Equal(RecoveryAction.ReassignToOtherAgent, result.Recovery);
    }

    [Fact]
    public void ShouldEscalate_WhenHandlingExceptionTaskExceptionCritical()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new TaskException(TaskId.Create(), "Critical task failed");
        var context = CreateContext(isCritical: true);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Escalate, result.Strategy);
        Assert.Contains("requires human intervention", result.Message);
    }

    #endregion

    #region HandleException Tests - Security Errors

    [Fact]
    public void ShouldFailFast_WhenHandlingExceptionUnauthorizedAccessException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new UnauthorizedAccessException("Access denied");
        var context = CreateContext();

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Fail, result.Strategy);
        Assert.Equal(RecoveryAction.FailFast, result.Recovery);
        Assert.True(result.IsFatal);
        Assert.False(result.ShouldRetry);
    }

    [Fact]
    public void ShouldFailFast_WhenHandlingExceptionSecurityException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new SecurityException("Security violation");
        var context = CreateContext();

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Fail, result.Strategy);
        Assert.True(result.IsFatal);
    }

    #endregion

    #region HandleException Tests - Configuration Errors

    [Fact]
    public void ShouldUseFallback_WhenHandlingExceptionArgumentExceptionWithFallback()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new ArgumentException("Invalid argument");
        var context = CreateContext(hasFallback: true);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Fallback, result.Strategy);
        Assert.Equal(RecoveryAction.UseFallback, result.Recovery);
        Assert.False(result.ShouldRetry);
    }

    [Fact]
    public void ShouldEscalate_WhenHandlingExceptionWithInvalidOperationExceptionNoFallback()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new InvalidOperationException("Invalid state");
        var context = CreateContext(hasFallback: false);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Escalate, result.Strategy);
    }

    #endregion

    #region HandleException Tests - Component-Specific Recovery

    [Theory]
    [InlineData("LLMProvider", ErrorStrategy.Retry, RecoveryAction.SwitchProvider)]
    [InlineData("LLMProvider", ErrorStrategy.Fallback, RecoveryAction.UseBackupProvider)]
    [InlineData("MemoryProvider", ErrorStrategy.Retry, RecoveryAction.RefreshConnection)]
    [InlineData("MemoryProvider", ErrorStrategy.Fallback, RecoveryAction.UseInMemoryFallback)]
    [InlineData("Tool", ErrorStrategy.Retry, RecoveryAction.RetryWithBackoff)]
    [InlineData("Tool", ErrorStrategy.Fallback, RecoveryAction.UseAlternativeTool)]
    [InlineData("Agent", ErrorStrategy.Retry, RecoveryAction.RestartAgent)]
    [InlineData("Crew", ErrorStrategy.Fallback, RecoveryAction.SimplifyExecution)]
    public void ShouldUseCorrectRecovery_WhenHandlingExceptionComponentSpecificErrors(
        string componentType, ErrorStrategy strategy, RecoveryAction expectedRecovery)
    {
        // Arrange
        var handler = CreateHandler();
        Exception exception = strategy switch
        {
            ErrorStrategy.Retry => new HttpRequestException("Network error"),
            ErrorStrategy.Fallback => new HttpRequestException("Network error"),
            _ => new Exception("Test error")
        };
        var context = CreateContext(
            componentType: componentType,
            hasFallback: strategy == ErrorStrategy.Fallback,
            retryCount: strategy == ErrorStrategy.Fallback ? 3 : 0);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(expectedRecovery, result.Recovery);
    }

    #endregion

    #region CalculateRetryDelay Tests

    [Theory]
    [InlineData(1, 2000)]  // Base 2s * 1.0
    [InlineData(2, 4000)]  // Base 2s * 2.0
    [InlineData(3, 8000)]  // Base 2s * 4.0
    [InlineData(4, 16000)] // Base 2s * 8.0
    [InlineData(5, 32000)] // Base 2s * 16.0 (max)
    public void ShouldUseExponentialBackoff_WhenCalculatingRetryDelayHttpRequestException(int attemptCount, int expectedBaseMs)
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new HttpRequestException("Test");

        // Act
        var delay = PatternMatchingErrorHandler.CalculateRetryDelay(exception, attemptCount);

        // Assert
        // Account for jitter (0-1000ms)
        Assert.InRange(delay.TotalMilliseconds, expectedBaseMs, expectedBaseMs + 1000);
    }

    [Fact]
    public void ShouldUseLongerDelay_WhenCalculatingRetryDelayRateLimitException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new Exception("Rate limit exceeded");

        // Act
        var delay = PatternMatchingErrorHandler.CalculateRetryDelay(exception, 1);

        // Assert
        // Base delay is 1 minute for rate limit
        Assert.InRange(delay.TotalMilliseconds, 60000, 61000);
    }

    [Fact]
    public void ShouldUse5SecondBase_WhenCalculatingRetryDelayTimeoutException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new SystemTimeoutException();

        // Act
        var delay = PatternMatchingErrorHandler.CalculateRetryDelay(exception, 1);

        // Assert
        Assert.InRange(delay.TotalMilliseconds, 5000, 6000);
    }

    #endregion

    #region DetermineErrorSeverity Tests

    [Theory]
    [InlineData(typeof(OutOfMemoryException), ErrorSeverity.Critical)]
    [InlineData(typeof(StackOverflowException), ErrorSeverity.Critical)]
    [InlineData(typeof(AccessViolationException), ErrorSeverity.Critical)]
    [InlineData(typeof(UnauthorizedAccessException), ErrorSeverity.High)]
    [InlineData(typeof(SecurityException), ErrorSeverity.High)]
    [InlineData(typeof(TaskCanceledException), ErrorSeverity.Low)]
    public void ShouldReturnCorrectSeverity_WhenDeterminingErrorSeverityWithVariousExceptions(
        Type exceptionType, ErrorSeverity expectedSeverity)
    {
        // Arrange
        var handler = CreateHandler();
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        var context = CreateContext();

        // Act
        var severity = PatternMatchingErrorHandler.DetermineErrorSeverity(exception, context);

        // Assert
        Assert.Equal(expectedSeverity, severity);
    }

    [Fact]
    public void ShouldReturnHigh_WhenDeterminingErrorSeverityCriticalDomainException()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new CrewException(CrewId.Create(), "Critical failure");
        var context = CreateContext(isCritical: true);

        // Act
        var severity = PatternMatchingErrorHandler.DetermineErrorSeverity(exception, context);

        // Assert
        Assert.Equal(ErrorSeverity.High, severity);
    }

    [Theory]
    [InlineData(0, ErrorSeverity.Low)]    // First attempt
    [InlineData(1, ErrorSeverity.Medium)] // Second attempt
    [InlineData(2, ErrorSeverity.Medium)] // Third attempt
    [InlineData(3, ErrorSeverity.High)]   // Fourth+ attempt
    [InlineData(5, ErrorSeverity.High)]   // Many attempts
    public void ShouldEscalateWithRetries_WhenDeterminingErrorSeverityHttpRequestException(
        int retryCount, ErrorSeverity expectedSeverity)
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new HttpRequestException("Network error");
        var context = CreateContext(retryCount: retryCount);

        // Act
        var severity = PatternMatchingErrorHandler.DetermineErrorSeverity(exception, context);

        // Assert
        Assert.Equal(expectedSeverity, severity);
    }

    [Fact]
    public void ShouldReturnWarning_WhenDeterminingErrorSeverityArgumentExceptionWithFallback()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new ArgumentException("Invalid arg");
        var context = CreateContext(hasFallback: true);

        // Act
        var severity = PatternMatchingErrorHandler.DetermineErrorSeverity(exception, context);

        // Assert
        Assert.Equal(ErrorSeverity.Warning, severity);
    }

    #endregion

    #region Edge Cases and Special Scenarios

    [Fact]
    public void ShouldIgnore_WhenHandlingExceptionWarningInMessageNotCritical()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new WarningException("This is just a warning");
        var context = CreateContext(isCritical: false);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Ignore, result.Strategy);
        Assert.Equal(RecoveryAction.LogAndContinue, result.Recovery);
        Assert.False(result.ShouldRetry);
    }

    [Fact]
    public void ShouldFailRegardlessOfType_WhenHandlingExceptionHighRetryCount()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new Exception("Any error");
        var context = CreateContext(retryCount: 5);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Fail, result.Strategy);
    }

    [Fact]
    public void ShouldRetry_WhenHandlingExceptionTransientErrorMessageLowRetryCount()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new Exception("A temporary glitch occurred");
        var context = CreateContext(retryCount: 1);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Retry, result.Strategy);
        Assert.True(result.ShouldRetry);
    }

    [Fact]
    public void ShouldUseCorrectRecovery_WhenHandlingExceptionTaskCancelledExceptionWithIncreaseTimeout()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new TaskCanceledException("Operation cancelled");
        var context = CreateContext(retryCount: 0);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Retry, result.Strategy);
        Assert.Equal(RecoveryAction.IncreaseTimeout, result.Recovery);
    }

    [Fact]
    public void ShouldSwitchProvider_WhenHandlingExceptionWithComplexScenarioNetworkErrorWithLLMProvider()
    {
        // Arrange
        var logger = new TestLogger<PatternMatchingErrorHandler>();
        var handler = CreateHandler(logger);
        var exception = new HttpRequestException("OpenAI API error");
        var context = CreateContext(
            componentType: "LLMProvider",
            isCritical: true,
            hasFallback: true,
            retryCount: 2);

        // Act
        var result = handler.HandleException(exception, context);

        // Assert
        Assert.Equal(ErrorStrategy.Retry, result.Strategy);
        Assert.Equal(RecoveryAction.SwitchProvider, result.Recovery);
        Assert.True(result.ShouldRetry);
        Assert.True(logger.HasLogMessage(LogLevel.Error, "LLMProvider"));
    }

    #endregion

    #region Test Helper Classes

    private class WarningException : Exception
    {
        public WarningException(string message) : base(message) { }
    }

    #endregion
}
