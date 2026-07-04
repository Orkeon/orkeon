using System.Text.RegularExpressions;
using Orkeon.Domain.Exceptions;
using SystemTimeoutException = System.TimeoutException;

namespace Orkeon.Application.Services.ErrorHandling;

/// <summary>
/// Error classifier using pattern matching for sophisticated error categorization.
/// Phase 3.2.3: Advanced pattern matching for error classification and routing.
/// </summary>
public static partial class ErrorClassifier
{
    /// <summary>
    /// Classifies errors into categories using pattern matching on exception types and messages.
    /// </summary>
    public static ErrorCategory ClassifyError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            // Network and connectivity errors
            HttpRequestException => ErrorCategory.Network,
            TaskCanceledException when exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Network,
            SystemTimeoutException => ErrorCategory.Network,
            Domain.Exceptions.TimeoutException => ErrorCategory.Network,

            // Resource and performance errors
            OutOfMemoryException => ErrorCategory.Resource,
            StackOverflowException => ErrorCategory.Resource,
            var ex when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Resource,
            var ex when ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Resource,
            var ex when ex.Message.Contains("throttle", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Resource,

            // Security and authorization errors
            UnauthorizedAccessException => ErrorCategory.Security,
            System.Security.SecurityException => ErrorCategory.Security,
            var ex when ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Security,
            var ex when ex.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Security,
            var ex when ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Security,

            // Configuration and setup errors
            ArgumentNullException => ErrorCategory.Configuration,
            ArgumentException => ErrorCategory.Configuration,
            InvalidOperationException => ErrorCategory.Configuration,
            NotSupportedException => ErrorCategory.Configuration,
            FileNotFoundException => ErrorCategory.Configuration,
            DirectoryNotFoundException => ErrorCategory.Configuration,

            // Domain-specific business logic errors
            CrewException => ErrorCategory.BusinessLogic,
            AgentException => ErrorCategory.BusinessLogic,
            TaskException => ErrorCategory.BusinessLogic,
            ValidationException => ErrorCategory.BusinessLogic,
            var ex when ex.GetType().Namespace?.Contains("Orkeon.Domain", StringComparison.Ordinal) == true => ErrorCategory.BusinessLogic,

            // Data and persistence errors
            var ex when ex.GetType().Name.Contains("Sql", StringComparison.Ordinal) => ErrorCategory.Data,
            var ex when ex.GetType().Name.Contains("Database", StringComparison.Ordinal) => ErrorCategory.Data,
            var ex when ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Data,
            var ex when ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Data,

            // External service integration errors
            var ex when ex.Message.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) => ErrorCategory.ExternalService,
            var ex when ex.Message.Contains("API", StringComparison.OrdinalIgnoreCase) => ErrorCategory.ExternalService,
            var ex when ex.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase) => ErrorCategory.ExternalService,

            // Validation and input errors
            FormatException => ErrorCategory.Validation,
            var ex when ex.Message.Contains("validation", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Validation,
            var ex when ex.Message.Contains("invalid format", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Validation,
            var ex when ex.Message.Contains("parse", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Validation,

            // Concurrency and threading errors
            var ex when ex is InvalidOperationException && ex.Message.Contains("thread", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Concurrency,
            var ex when ex.Message.Contains("race condition", StringComparison.OrdinalIgnoreCase) => ErrorCategory.Concurrency,

            // Unknown errors
            _ => ErrorCategory.Unknown
        };
    }

    /// <summary>
    /// Determines if error is transient using pattern matching.
    /// </summary>
    public static bool IsTransientError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            // Network errors are typically transient
            HttpRequestException => true,
            SystemTimeoutException => true,
            Domain.Exceptions.TimeoutException => true,
            TaskCanceledException => true,

            // Some resource errors are transient
            var ex when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("throttle", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("temporary", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("transient", StringComparison.OrdinalIgnoreCase) => true,

            // Database connection issues can be transient
            var ex when ex.Message.Contains("connection timeout", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("connection lost", StringComparison.OrdinalIgnoreCase) => true,

            // External service issues are often transient
            var ex when ex.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("502", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("503", StringComparison.OrdinalIgnoreCase) => true,
            var ex when ex.Message.Contains("504", StringComparison.OrdinalIgnoreCase) => true,

            // Non-transient errors
            ArgumentNullException => false,
            ArgumentException => false,
            UnauthorizedAccessException => false,
            System.Security.SecurityException => false,
            OutOfMemoryException => false,
            StackOverflowException => false,

            _ => false
        };
    }

    /// <summary>
    /// Determines error recoverability using pattern matching.
    /// </summary>
    public static RecoverabilityLevel DetermineRecoverability(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            // Fully recoverable - can retry or use alternatives
            HttpRequestException => RecoverabilityLevel.FullyRecoverable,
            SystemTimeoutException => RecoverabilityLevel.FullyRecoverable,
            Domain.Exceptions.TimeoutException => RecoverabilityLevel.FullyRecoverable,
            TaskCanceledException => RecoverabilityLevel.FullyRecoverable,
            var ex when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => RecoverabilityLevel.FullyRecoverable,

            // Partially recoverable - may need fallback
            InvalidOperationException => RecoverabilityLevel.PartiallyRecoverable,
            NotSupportedException => RecoverabilityLevel.PartiallyRecoverable,
            var ex when ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) => RecoverabilityLevel.PartiallyRecoverable,

            // Requires intervention - human or system admin needed
            CrewException => RecoverabilityLevel.RequiresIntervention,
            AgentException => RecoverabilityLevel.RequiresIntervention,
            TaskException => RecoverabilityLevel.RequiresIntervention,
            ValidationException => RecoverabilityLevel.RequiresIntervention,
            UnauthorizedAccessException => RecoverabilityLevel.RequiresIntervention,

            // Non-recoverable - fatal errors
            OutOfMemoryException => RecoverabilityLevel.NonRecoverable,
            StackOverflowException => RecoverabilityLevel.NonRecoverable,
            AccessViolationException => RecoverabilityLevel.NonRecoverable,

            _ => RecoverabilityLevel.PartiallyRecoverable
        };
    }

    /// <summary>
    /// Extracts error details using pattern matching on error messages.
    /// </summary>
    public static ErrorDetails ExtractErrorDetails(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var category = ClassifyError(exception);
        var isTransient = IsTransientError(exception);
        var recoverability = DetermineRecoverability(exception);

        var errorCode = ExtractErrorCode(exception);
        var component = ExtractComponent(exception);
        var operation = ExtractOperation(exception);

        return new ErrorDetails(
            Category: category,
            IsTransient: isTransient,
            Recoverability: recoverability,
            ErrorCode: errorCode,
            Component: component,
            Operation: operation,
            StackTrace: exception.StackTrace,
            InnerExceptionType: exception.InnerException?.GetType().Name);
    }

    /// <summary>
    /// Extracts error codes using pattern matching on exception messages.
    /// </summary>
    private static string? ExtractErrorCode(Exception exception)
    {
        return exception.Message switch
        {
            var msg when ThreeDigitCodeRegex().IsMatch(msg) => ThreeDigitCodeRegex().Match(msg).Value,
            var msg when msg.Contains("HTTP", StringComparison.Ordinal) => HttpStatusCodeRegex().Match(msg).Groups[1].Value,
            var msg when msg.Contains("Error:", StringComparison.Ordinal) => ErrorCodeRegex().Match(msg).Groups[1].Value,
            var msg when msg.Contains("Code:", StringComparison.Ordinal) => CodeValueRegex().Match(msg).Groups[1].Value,
            _ => null
        };
    }

    /// <summary>
    /// Extracts component name using pattern matching on exception types and stack traces.
    /// </summary>
    private static string? ExtractComponent(Exception exception)
    {
        return exception switch
        {
            var ex when ex.GetType().Namespace?.Contains("LLMs", StringComparison.Ordinal) == true => "LLMProvider",
            var ex when ex.GetType().Namespace?.Contains("Memory", StringComparison.Ordinal) == true => "MemoryProvider",
            var ex when ex.GetType().Namespace?.Contains("Tools", StringComparison.Ordinal) == true => "Tool",
            var ex when ex.GetType().Namespace?.Contains("Agent", StringComparison.Ordinal) == true => "Agent",
            var ex when ex.GetType().Namespace?.Contains("Crew", StringComparison.Ordinal) == true => "Crew",
            var ex when ex.StackTrace?.Contains("HttpClient", StringComparison.Ordinal) == true => "HttpClient",
            var ex when ex.StackTrace?.Contains("Database", StringComparison.Ordinal) == true => "Database",
            _ => ExtractComponentFromStackTrace(exception.StackTrace)
        };
    }

    /// <summary>
    /// Extracts operation name using pattern matching on stack traces.
    /// </summary>
    private static string? ExtractOperation(Exception exception)
    {
        if (string.IsNullOrEmpty(exception.StackTrace))
            return null;

        return exception.StackTrace switch
        {
            var st when st.Contains("ExecuteAsync", StringComparison.Ordinal) => "Execute",
            var st when st.Contains("ProcessAsync", StringComparison.Ordinal) => "Process",
            var st when st.Contains("ValidateAsync", StringComparison.Ordinal) => "Validate",
            var st when st.Contains("TransformAsync", StringComparison.Ordinal) => "Transform",
            var st when st.Contains("SaveAsync", StringComparison.Ordinal) => "Save",
            var st when st.Contains("LoadAsync", StringComparison.Ordinal) => "Load",
            var st when st.Contains("ConnectAsync", StringComparison.Ordinal) => "Connect",
            var st when st.Contains("SendAsync", StringComparison.Ordinal) => "Send",
            var st when st.Contains("ReceiveAsync", StringComparison.Ordinal) => "Receive",
            _ => ExtractMethodFromStackTrace(exception.StackTrace)
        };
    }

    private static string? ExtractComponentFromStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return null;

        var match = OrkeonComponentRegex().Match(stackTrace);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractMethodFromStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return null;

        var match = MethodNameRegex().Match(stackTrace);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"\b\d{3}\b")]
    private static partial Regex ThreeDigitCodeRegex();

    [GeneratedRegex(@"HTTP\s+(\d+)")]
    private static partial Regex HttpStatusCodeRegex();

    [GeneratedRegex(@"Error:\s*(\w+)")]
    private static partial Regex ErrorCodeRegex();

    [GeneratedRegex(@"Code:\s*(\w+)")]
    private static partial Regex CodeValueRegex();

    [GeneratedRegex(@"at\s+Orkeon\.(\w+)\.")]
    private static partial Regex OrkeonComponentRegex();

    [GeneratedRegex(@"at\s+[\w\.]+\.(\w+)\(")]
    private static partial Regex MethodNameRegex();
}

/// <summary>
/// Error categories for pattern matching classification.
/// </summary>
public enum ErrorCategory
{
    /// <summary>Network.</summary>
    Network,
    /// <summary>Resource.</summary>
    Resource,
    /// <summary>Security.</summary>
    Security,
    /// <summary>Configuration.</summary>
    Configuration,
    /// <summary>Business Logic.</summary>
    BusinessLogic,
    /// <summary>Data.</summary>
    Data,
    /// <summary>External Service.</summary>
    ExternalService,
    /// <summary>Validation.</summary>
    Validation,
    /// <summary>Concurrency.</summary>
    Concurrency,
    /// <summary>Unknown.</summary>
    Unknown
}

/// <summary>
/// Error recoverability levels.
/// </summary>
public enum RecoverabilityLevel
{
    /// <summary>Fully Recoverable.</summary>
    FullyRecoverable,
    /// <summary>Partially Recoverable.</summary>
    PartiallyRecoverable,
    /// <summary>Requires Intervention.</summary>
    RequiresIntervention,
    /// <summary>Non Recoverable.</summary>
    NonRecoverable
}

/// <summary>
/// Diagnostic context information for an error.
/// </summary>
public record ErrorDiagnosticInfo(
    string? ErrorCode = null,
    string? Component = null,
    string? Operation = null,
    string? StackTrace = null,
    string? InnerExceptionType = null);

/// <summary>
/// Detailed error information extracted through pattern matching.
/// </summary>
public record ErrorDetails(
    ErrorCategory Category,
    bool IsTransient,
    RecoverabilityLevel Recoverability,
    ErrorDiagnosticInfo Diagnostics)
{
    // Backward-compatible accessors
    /// <summary>Error code if available.</summary>
    public string? ErrorCode => Diagnostics.ErrorCode;
    /// <summary>Component where the error occurred.</summary>
    public string? Component => Diagnostics.Component;
    /// <summary>Operation that failed.</summary>
    public string? Operation => Diagnostics.Operation;
    /// <summary>Stack trace of the error.</summary>
    public string? StackTrace => Diagnostics.StackTrace;
    /// <summary>Inner exception type name.</summary>
    public string? InnerExceptionType => Diagnostics.InnerExceptionType;

    /// <summary>
    /// Initializes a new instance of <see cref="ErrorDetails"/> for backward compatibility.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use primary constructor with ErrorDiagnosticInfo instead
    public ErrorDetails(
        ErrorCategory Category,
        bool IsTransient,
        RecoverabilityLevel Recoverability,
        string? ErrorCode = null,
        string? Component = null,
        string? Operation = null,
        string? StackTrace = null,
        string? InnerExceptionType = null)
        : this(Category, IsTransient, Recoverability,
            new ErrorDiagnosticInfo(ErrorCode, Component, Operation, StackTrace, InnerExceptionType))
    {
    }
#pragma warning restore S107
}
