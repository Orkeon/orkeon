using Microsoft.Extensions.Logging;
using Orkeon.Application.Constants.Resilience;
using Orkeon.Domain.Common;
using Orkeon.Domain.Exceptions;
using System.Security;
using SystemTimeoutException = System.TimeoutException;
using Orkeon.Domain.Constants.Resilience;

namespace Orkeon.Application.Services.ErrorHandling;

/// <summary>
/// Error handler using pattern matching for sophisticated error categorization and recovery.
/// Phase 3.2.3: Modern C# pattern matching for error handling strategies.
/// </summary>
public partial class PatternMatchingErrorHandler
{
    private readonly ILogger<PatternMatchingErrorHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PatternMatchingErrorHandler"/>.
    /// </summary>
    public PatternMatchingErrorHandler(ILogger<PatternMatchingErrorHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Handles exceptions using pattern matching to determine appropriate response.
    /// </summary>
    public ErrorHandlingResult HandleException(Exception exception, ErrorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var strategy = DetermineErrorStrategy(exception, context);
        var recovery = DetermineRecoveryAction(exception, context, strategy);

        LogHandlingException(exception, strategy.ToString(), recovery.ToString(), context.ToString()!);

        return strategy switch
        {
            ErrorStrategy.Retry => HandleRetryableError(exception, context, recovery),
            ErrorStrategy.Fallback => HandleFallbackError(exception, context, recovery),
            ErrorStrategy.Escalate => HandleEscalationError(exception, context, recovery),
            ErrorStrategy.Ignore => HandleIgnorableError(recovery),
            ErrorStrategy.Fail => HandleFatalError(recovery),
            ErrorStrategy.Circuit => HandleCircuitBreakerError(recovery),
            _ => HandleUnknownError(recovery)
        };
    }

    /// <summary>
    /// Determines error handling strategy using pattern matching on exception types and context.
    /// </summary>
    private static ErrorStrategy DetermineErrorStrategy(Exception exception, ErrorContext context)
    {
        return (exception, context) switch
        {
            // Resource exhaustion and rate limiting - circuit breaker pattern (check first)
            (OutOfMemoryException, _) => ErrorStrategy.Circuit,
            (var ex, _) when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => ErrorStrategy.Circuit,
            (var ex, _) when ex.Message.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase) => ErrorStrategy.Circuit,

            // Network and connectivity errors - retry with backoff
            (HttpRequestException, var ctx) when ctx.RetryCount < 3 => ErrorStrategy.Retry,
            (TaskCanceledException, var ctx) when ctx.RetryCount < 2 => ErrorStrategy.Retry,
            (SystemTimeoutException, var ctx) when ctx.RetryCount < 3 => ErrorStrategy.Retry,
            (Domain.Exceptions.TimeoutException, var ctx) when ctx.RetryCount < 3 => ErrorStrategy.Retry,

            // Domain business logic errors - escalate for human intervention
            (CrewException, _) => ErrorStrategy.Escalate,
            (AgentException, _) => ErrorStrategy.Escalate,
            (TaskException, var ctx) when ctx.IsCritical => ErrorStrategy.Escalate,
            (ValidationException, _) => ErrorStrategy.Escalate,

            // Authorization and authentication errors - fail fast
            (UnauthorizedAccessException, _) => ErrorStrategy.Fail,
            (SecurityException, _) => ErrorStrategy.Fail,

            // Configuration errors - use fallback
            (ArgumentException, var ctx) when ctx.HasFallback => ErrorStrategy.Fallback,
            (InvalidOperationException, var ctx) when ctx.HasFallback => ErrorStrategy.Fallback,
            (NotSupportedException, var ctx) when ctx.HasFallback => ErrorStrategy.Fallback,

            // Transient errors with high retry count - fallback
            (HttpRequestException, var ctx) when ctx.RetryCount >= 3 && ctx.HasFallback => ErrorStrategy.Fallback,
            (SystemTimeoutException, var ctx) when ctx.RetryCount >= 3 && ctx.HasFallback => ErrorStrategy.Fallback,
            (Domain.Exceptions.TimeoutException, var ctx) when ctx.RetryCount >= 3 && ctx.HasFallback => ErrorStrategy.Fallback,

            // Non-critical errors that can be ignored
            (var ex, var ctx) when !ctx.IsCritical && ex.GetType().Name.Contains("Warning", StringComparison.Ordinal) => ErrorStrategy.Ignore,

            // High retry count without fallback - fail
            (_, var ctx) when ctx.RetryCount >= 5 => ErrorStrategy.Fail,

            // Default to retry for unknown transient-looking errors
            (var ex, var ctx) when IsTransientError(ex) && ctx.RetryCount < 2 => ErrorStrategy.Retry,

            // Default to escalate for unknown errors
            _ => ErrorStrategy.Escalate
        };
    }

    /// <summary>
    /// Determines recovery action using pattern matching on error characteristics.
    /// </summary>
    private static RecoveryAction DetermineRecoveryAction(Exception exception, ErrorContext context, ErrorStrategy strategy)
    {
        return (exception, context, strategy) switch
        {
            // LLM provider errors
            (_, var ctx, ErrorStrategy.Retry) when ctx.ComponentType == "LLMProvider"
                => RecoveryAction.SwitchProvider,
            (_, var ctx, ErrorStrategy.Fallback) when ctx.ComponentType == "LLMProvider"
                => RecoveryAction.UseBackupProvider,

            // Memory provider errors
            (_, var ctx, ErrorStrategy.Retry) when ctx.ComponentType == "MemoryProvider"
                => RecoveryAction.RefreshConnection,
            (_, var ctx, ErrorStrategy.Fallback) when ctx.ComponentType == "MemoryProvider"
                => RecoveryAction.UseInMemoryFallback,

            // Tool execution errors
            (_, var ctx, ErrorStrategy.Retry) when ctx.ComponentType == "Tool"
                => RecoveryAction.RetryWithBackoff,
            (_, var ctx, ErrorStrategy.Fallback) when ctx.ComponentType == "Tool"
                => RecoveryAction.UseAlternativeTool,

            // Agent execution errors
            (_, var ctx, ErrorStrategy.Escalate) when ctx.ComponentType == "Agent"
                => RecoveryAction.ReassignToOtherAgent,
            (_, var ctx, ErrorStrategy.Retry) when ctx.ComponentType == "Agent"
                => RecoveryAction.RestartAgent,

            // Crew orchestration errors
            (_, var ctx, ErrorStrategy.Escalate) when ctx.ComponentType == "Crew"
                => RecoveryAction.NotifyManager,
            (_, var ctx, ErrorStrategy.Fallback) when ctx.ComponentType == "Crew"
                => RecoveryAction.SimplifyExecution,

            // Network errors with specific patterns
            (HttpRequestException, _, ErrorStrategy.Retry) => RecoveryAction.RetryWithBackoff,
            (TaskCanceledException, _, ErrorStrategy.Retry) => RecoveryAction.IncreaseTimeout,

            // Resource errors
            (OutOfMemoryException, _, ErrorStrategy.Circuit) => RecoveryAction.ReduceLoad,
            (var ex, _, ErrorStrategy.Circuit) when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                => RecoveryAction.RateLimitBackoff,

            // Default actions per strategy
            (_, _, ErrorStrategy.Retry) => RecoveryAction.RetryWithBackoff,
            (_, _, ErrorStrategy.Fallback) => RecoveryAction.UseFallback,
            (_, _, ErrorStrategy.Escalate) => RecoveryAction.NotifyManager,
            (_, _, ErrorStrategy.Ignore) => RecoveryAction.LogAndContinue,
            (_, _, ErrorStrategy.Fail) => RecoveryAction.FailFast,
            (_, _, ErrorStrategy.Circuit) => RecoveryAction.ActivateCircuitBreaker,

            _ => RecoveryAction.LogAndContinue
        };
    }

    /// <summary>
    /// Calculates retry delay using pattern matching on error type and attempt count.
    /// </summary>
    public static TimeSpan CalculateRetryDelay(Exception exception, int attemptCount)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var baseDelay = exception switch
        {
            HttpRequestException => TimeSpan.FromSeconds(RetryDefaults.HttpRetryDelaySeconds),
            Domain.Exceptions.TimeoutException => TimeSpan.FromSeconds(RetryDefaults.TimeoutRetryDelaySeconds),
            System.TimeoutException => TimeSpan.FromSeconds(RetryDefaults.TimeoutRetryDelaySeconds),
            TaskCanceledException => ResilienceDefaults.DefaultRetryInitialDelay,
            var ex when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => TimeSpan.FromMinutes(RetryDefaults.RateLimitDelayMinutes),
            var ex when ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase) => TimeSpan.FromMinutes(RetryDefaults.QuotaExceededDelayMinutes),
            _ => TimeSpan.FromSeconds(RetryDefaults.DefaultInitialDelaySeconds)
        };

        // Exponential backoff with jitter
        var clampedAttempt = Math.Clamp(attemptCount - 1, 0, RetryDefaults.ExponentialBackoffMultipliers.Length - 1);
        var multiplier = RetryDefaults.ExponentialBackoffMultipliers[clampedAttempt];

        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
#pragma warning disable CA5394 // jitter for retry exponential-backoff; randomness only spreads retry timing, not security-sensitive.
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, RetryDefaults.MaxJitterMilliseconds));
#pragma warning restore CA5394

        return delay + jitter;
    }

    /// <summary>
    /// Determines error severity using pattern matching.
    /// </summary>
    public static ErrorSeverity DetermineErrorSeverity(Exception exception, ErrorContext context)
    {
        return (exception, context) switch
        {
            // Critical system errors
            (OutOfMemoryException, _) => ErrorSeverity.Critical,
            (StackOverflowException, _) => ErrorSeverity.Critical,
            (AccessViolationException, _) => ErrorSeverity.Critical,

            // High severity - security and data integrity
            (UnauthorizedAccessException, _) => ErrorSeverity.High,
            (SecurityException, _) => ErrorSeverity.High,
            (var ex, var ctx) when ctx.IsCritical && ex is DomainException => ErrorSeverity.High,

            // Medium severity - business logic errors

            // Low severity - transient network issues (first attempt only)
            (HttpRequestException, var ctx) when ctx.RetryCount == 0 => ErrorSeverity.Low,
            (Domain.Exceptions.TimeoutException, var ctx) when ctx.RetryCount == 0 => ErrorSeverity.Low,
            (System.TimeoutException, var ctx) when ctx.RetryCount == 0 => ErrorSeverity.Low,
            (TaskCanceledException, _) => ErrorSeverity.Low,

            // Warning level - recoverable issues
            (ArgumentException, var ctx) when ctx.HasFallback => ErrorSeverity.Warning,
            (InvalidOperationException, var ctx) when ctx.HasFallback => ErrorSeverity.Warning,

            // Escalate based on retry count
            (_, var ctx) when ctx.RetryCount >= 3 => ErrorSeverity.High,
            (_, var ctx) when ctx.RetryCount >= 1 => ErrorSeverity.Medium,

            _ => ErrorSeverity.Medium
        };
    }

    private static ErrorHandlingResult HandleRetryableError(Exception exception, ErrorContext context, RecoveryAction recovery)
    {
        var delay = CalculateRetryDelay(exception, context.RetryCount + 1);
        return new ErrorHandlingResult(
            ErrorStrategy.Retry,
            recovery,
            ShouldRetry: true,
            Message: Inv.Format($"Retrying operation after {delay.TotalSeconds:F1} seconds (attempt {context.RetryCount + 1})"),
            RetryDelay: delay);
    }

#pragma warning disable S1172 // Parameters reserved for uniform handler signature
    private static ErrorHandlingResult HandleFallbackError(Exception _exception, ErrorContext _context, RecoveryAction recovery)
    {
        return new ErrorHandlingResult(
            ErrorStrategy.Fallback,
            recovery,
            ShouldRetry: false,
            Message: "Using fallback mechanism due to repeated failures");
    }

    private static ErrorHandlingResult HandleEscalationError(Exception _exception, ErrorContext _context, RecoveryAction recovery)
    {
        return new ErrorHandlingResult(
            ErrorStrategy.Escalate,
            recovery,
            ShouldRetry: false,
            RequiresEscalation: true,
            Message: "Error requires human intervention or system administrator attention");
    }
#pragma warning restore S1172

    private static ErrorHandlingResult HandleIgnorableError(RecoveryAction recovery)
    {
        return new ErrorHandlingResult(
            ErrorStrategy.Ignore,
            recovery,
            ShouldRetry: false,
            Message: "Non-critical error logged and ignored");
    }

    private static ErrorHandlingResult HandleFatalError(RecoveryAction recovery)
    {
        return new ErrorHandlingResult(
            ErrorStrategy.Fail,
            recovery,
            ShouldRetry: false,
            IsFatal: true,
            Message: "Fatal error - operation cannot continue");
    }

    private static ErrorHandlingResult HandleCircuitBreakerError(RecoveryAction recovery)
    {
        return new ErrorHandlingResult(
            ErrorStrategy.Circuit,
            recovery,
            ShouldRetry: false,
            CircuitBreakerTripped: true,
            RetryDelay: TimeSpan.FromMinutes(RetryDefaults.DefaultTrippedDelayMinutes),
            Message: "Circuit breaker activated - service temporarily unavailable");
    }

    private static ErrorHandlingResult HandleUnknownError(RecoveryAction recovery)
    {
        return new ErrorHandlingResult(
            ErrorStrategy.Escalate,
            recovery,
            ShouldRetry: false,
            RequiresEscalation: true,
            Message: "Unknown error type - requires investigation");
    }

    private static bool IsTransientError(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => true,
            Domain.Exceptions.TimeoutException => true,
            System.TimeoutException => true,
            TaskCanceledException => true,
            var ex when ex.Message.Contains("temporary", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("transient", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("retry", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling exception with strategy {Strategy} and recovery {Recovery}. Context: {Context}")]
    private partial void LogHandlingException(Exception ex, string strategy, string recovery, string context);
}

/// <summary>
/// Error handling strategies using pattern matching.
/// </summary>
public enum ErrorStrategy
{
    /// <summary>Retry.</summary>
    Retry,
    /// <summary>Fallback.</summary>
    Fallback,
    /// <summary>Escalate.</summary>
    Escalate,
    /// <summary>Ignore.</summary>
    Ignore,
    /// <summary>Fail.</summary>
    Fail,
    /// <summary>Circuit.</summary>
    Circuit
}

/// <summary>
/// Recovery actions based on error pattern matching.
/// </summary>
public enum RecoveryAction
{
    /// <summary>Retry With Backoff.</summary>
    RetryWithBackoff,
    /// <summary>Switch Provider.</summary>
    SwitchProvider,
    /// <summary>Use Backup Provider.</summary>
    UseBackupProvider,
    /// <summary>Refresh Connection.</summary>
    RefreshConnection,
    /// <summary>Use In Memory Fallback.</summary>
    UseInMemoryFallback,
    /// <summary>Use Alternative Tool.</summary>
    UseAlternativeTool,
    /// <summary>Reassign To Other Agent.</summary>
    ReassignToOtherAgent,
    /// <summary>Restart Agent.</summary>
    RestartAgent,
    /// <summary>Notify Manager.</summary>
    NotifyManager,
    /// <summary>Simplify Execution.</summary>
    SimplifyExecution,
    /// <summary>Increase Timeout.</summary>
    IncreaseTimeout,
    /// <summary>Reduce Load.</summary>
    ReduceLoad,
    /// <summary>Rate Limit Backoff.</summary>
    RateLimitBackoff,
    /// <summary>Use Fallback.</summary>
    UseFallback,
    /// <summary>Log And Continue.</summary>
    LogAndContinue,
    /// <summary>Fail Fast.</summary>
    FailFast,
    /// <summary>Activate Circuit Breaker.</summary>
    ActivateCircuitBreaker
}

/// <summary>
/// Error severity levels for pattern matching.
/// </summary>
public enum ErrorSeverity
{
    /// <summary>Warning.</summary>
    Warning,
    /// <summary>Low.</summary>
    Low,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>High.</summary>
    High,
    /// <summary>Critical.</summary>
    Critical
}

/// <summary>
/// Context information for error pattern matching.
/// </summary>
public record ErrorContext(
    string ComponentType,
    bool IsCritical,
    bool HasFallback,
    int RetryCount = 0,
    TimeSpan ElapsedTime = default,
    string? OperationId = null,
    IDictionary<string, object>? Metadata = null);

/// <summary>
/// Flags indicating the severity and handling state of an error.
/// </summary>
public record ErrorHandlingFlags(
    bool ShouldRetry = false,
    bool RequiresEscalation = false,
    bool IsFatal = false,
    bool CircuitBreakerTripped = false);

/// <summary>
/// Result of error handling with pattern matching decisions.
/// </summary>
public record ErrorHandlingResult(
    ErrorStrategy Strategy,
    RecoveryAction Recovery,
    ErrorHandlingFlags Flags,
    TimeSpan RetryDelay = default,
    string? Message = null)
{
    // Backward-compatible accessors
    /// <summary>Whether the operation should be retried.</summary>
    public bool ShouldRetry => Flags.ShouldRetry;
    /// <summary>Whether the error requires escalation.</summary>
    public bool RequiresEscalation => Flags.RequiresEscalation;
    /// <summary>Whether the error is fatal.</summary>
    public bool IsFatal => Flags.IsFatal;
    /// <summary>Whether the circuit breaker was tripped.</summary>
    public bool CircuitBreakerTripped => Flags.CircuitBreakerTripped;

    /// <summary>
    /// Initializes a new instance of <see cref="ErrorHandlingResult"/> for backward compatibility.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use primary constructor with ErrorHandlingFlags instead
    public ErrorHandlingResult(
        ErrorStrategy Strategy,
        RecoveryAction Recovery,
        bool ShouldRetry = false,
        bool RequiresEscalation = false,
        bool IsFatal = false,
        bool CircuitBreakerTripped = false,
        TimeSpan RetryDelay = default,
        string? Message = null)
        : this(Strategy, Recovery,
            new ErrorHandlingFlags(ShouldRetry, RequiresEscalation, IsFatal, CircuitBreakerTripped),
            RetryDelay, Message)
    {
    }
#pragma warning restore S107
}
