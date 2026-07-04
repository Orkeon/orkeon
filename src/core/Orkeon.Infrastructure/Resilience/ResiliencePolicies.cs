using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Timeout;
using System.Net;
using Orkeon.Domain.Constants.Http;
using Orkeon.Domain.Constants.Resilience;

namespace Orkeon.Infrastructure.Resilience;

/// <summary>
/// Provides resilience policies for HTTP operations and external service calls.
/// </summary>
public static class ResiliencePolicies
{
    private static PolicyBuilder<HttpResponseMessage> HandleTransientHttpError()
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
            .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.RequestTimeout);
    }

    /// <summary>
    /// Creates an exponential backoff retry policy for transient HTTP errors and rate limiting.
    /// </summary>
    /// <param name="logger">Optional logger for retry diagnostics.</param>
    /// <param name="maxRetryAttempts">Maximum number of retry attempts before failing.</param>
    /// <returns>An async retry policy for HTTP response messages.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null, int maxRetryAttempts = 3)
    {
        var safeLogger = logger ?? NullLogger.Instance;
        return HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
            .WaitAndRetryAsync(maxRetryAttempts,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var reason = outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message ?? "Unknown";
                    ResiliencePoliciesLog.LogHttpRequestRetry(safeLogger, retryCount, timespan.TotalMilliseconds, reason);
                });
    }

    /// <summary>
    /// Creates a circuit breaker policy that trips after consecutive transient HTTP failures.
    /// </summary>
    /// <param name="logger">Optional logger for circuit breaker state changes.</param>
    /// <param name="handledEventsAllowedBeforeBreaking">Number of failures before the circuit opens.</param>
    /// <param name="durationOfBreak">How long the circuit stays open before testing again.</param>
    /// <returns>An async circuit breaker policy for HTTP response messages.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(ILogger? logger = null, int handledEventsAllowedBeforeBreaking = 3, TimeSpan durationOfBreak = default)
    {
        if (durationOfBreak == default) durationOfBreak = ResilienceDefaults.DefaultRetryMaxDelay;
        var safeLogger = logger ?? NullLogger.Instance;
        return HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(handledEventsAllowedBeforeBreaking, durationOfBreak,
                onBreak: (result, duration) => { ResiliencePoliciesLog.LogCircuitBreakerOpened(safeLogger, duration.TotalSeconds, handledEventsAllowedBeforeBreaking); },
                onReset: () => { ResiliencePoliciesLog.LogCircuitBreakerReset(safeLogger); },
                onHalfOpen: () => { ResiliencePoliciesLog.LogCircuitBreakerHalfOpen(safeLogger); });
    }

    /// <summary>
    /// Creates an optimistic timeout policy that cancels HTTP requests exceeding the specified duration.
    /// </summary>
    /// <param name="timeout">The timeout duration; defaults to 30 seconds.</param>
    /// <param name="logger">Optional logger for timeout events.</param>
    /// <returns>An async timeout policy for HTTP response messages.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy(TimeSpan timeout = default, ILogger? logger = null)
    {
        if (timeout == default) timeout = HttpDefaults.DefaultHttpTimeout;
        var safeLogger = logger ?? NullLogger.Instance;
        return Policy.TimeoutAsync<HttpResponseMessage>(timeout, TimeoutStrategy.Optimistic,
            onTimeoutAsync: (context, timespan, task) => { ResiliencePoliciesLog.LogHttpRequestTimedOut(safeLogger, timespan.TotalSeconds); return Task.CompletedTask; });
    }

    /// <summary>
    /// Creates a combined policy wrapping retry, circuit breaker, and timeout for comprehensive HTTP resilience.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="maxRetryAttempts">Maximum retry attempts.</param>
    /// <param name="circuitBreakerThreshold">Failures before the circuit breaker trips.</param>
    /// <param name="circuitBreakerDuration">Duration the circuit stays open.</param>
    /// <param name="timeout">Per-request timeout duration.</param>
    /// <returns>A wrapped async policy combining retry, circuit breaker, and timeout.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy(ILogger? logger = null, int maxRetryAttempts = 3, int circuitBreakerThreshold = 5, TimeSpan? circuitBreakerDuration = null, TimeSpan? timeout = null)
    {
        return Policy.WrapAsync(GetRetryPolicy(logger, maxRetryAttempts), GetCircuitBreakerPolicy(logger, circuitBreakerThreshold, circuitBreakerDuration ?? ResilienceDefaults.DefaultRetryMaxDelay), GetTimeoutPolicy(timeout ?? HttpDefaults.DefaultHttpTimeout, logger));
    }

    /// <summary>
    /// Creates a retry policy for SQLite database operations, handling busy and locked errors.
    /// </summary>
    /// <param name="logger">Optional logger for retry diagnostics.</param>
    /// <param name="maxRetryAttempts">Maximum retry attempts.</param>
    /// <returns>An async retry policy for database operations.</returns>
    public static IAsyncPolicy GetDatabaseRetryPolicy(ILogger? logger = null, int maxRetryAttempts = 3)
    {
        var safeLogger = logger ?? NullLogger.Instance;
        return Policy
            .Handle<Microsoft.Data.Sqlite.SqliteException>(ex => ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6)
            .WaitAndRetryAsync(maxRetryAttempts,
                retryAttempt => TimeSpan.FromMilliseconds(100 * retryAttempt),
                onRetry: (exception, timespan, retryCount, context) => { ResiliencePoliciesLog.LogDatabaseRetry(safeLogger, retryCount, timespan.TotalMilliseconds, exception.Message); });
    }

    /// <summary>
    /// Creates an exponential backoff retry policy for Redis operations, handling connection and timeout errors.
    /// </summary>
    /// <param name="logger">Optional logger for retry diagnostics.</param>
    /// <param name="maxRetryAttempts">Maximum retry attempts.</param>
    /// <returns>An async retry policy for Redis operations.</returns>
    public static IAsyncPolicy GetRedisRetryPolicy(ILogger? logger = null, int maxRetryAttempts = 3)
    {
        var safeLogger = logger ?? NullLogger.Instance;
        return Policy
            .Handle<StackExchange.Redis.RedisException>()
            .Or<StackExchange.Redis.RedisTimeoutException>()
            .Or<StackExchange.Redis.RedisConnectionException>()
            .WaitAndRetryAsync(maxRetryAttempts,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timespan, retryCount, context) => { ResiliencePoliciesLog.LogRedisRetry(safeLogger, retryCount, timespan.TotalMilliseconds, exception.Message); });
    }

    /// <summary>
    /// Creates a specialized retry policy for LLM API calls with aggressive backoff and Retry-After header support.
    /// </summary>
    /// <param name="logger">Optional logger for retry diagnostics.</param>
    /// <param name="maxRetryAttempts">Maximum retry attempts; defaults to 5 for rate-limited APIs.</param>
    /// <param name="baseDelay">Base delay between retries; defaults to 1 second.</param>
    /// <returns>An async retry policy for LLM API HTTP calls.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetLlmApiPolicy(ILogger? logger = null, int maxRetryAttempts = 5, TimeSpan? baseDelay = null)
    {
        var delay = baseDelay ?? ResilienceDefaults.DefaultRetryInitialDelay;
        var safeLogger = logger ?? NullLogger.Instance;
        return HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(maxRetryAttempts,
                retryAttempt => retryAttempt > 2 ? TimeSpan.FromSeconds(delay.TotalSeconds * Math.Pow(3, retryAttempt - 2)) : TimeSpan.FromSeconds(delay.TotalSeconds * retryAttempt),
                onRetry: async (outcome, timespan, retryCount, context) =>
                {
                    if (outcome.Result?.Headers.RetryAfter?.Delta != null)
                    {
                        ResiliencePoliciesLog.LogLlmApiRateLimited(safeLogger, outcome.Result.Headers.RetryAfter.Delta.Value.TotalSeconds);
                        await System.Threading.Tasks.Task.Delay(outcome.Result.Headers.RetryAfter.Delta.Value).ConfigureAwait(false);
                    }
                    else
                    {
                        ResiliencePoliciesLog.LogLlmApiRetry(safeLogger, retryCount, timespan.TotalMilliseconds, outcome.Result?.StatusCode.ToString() ?? "Error");
                    }
                });
    }
}

internal static partial class ResiliencePoliciesLog
{
    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "HTTP request retry {RetryCount} after {Delay}ms. Reason: {Reason}")]
    public static partial void LogHttpRequestRetry(ILogger logger, int retryCount, double delay, string reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Circuit breaker opened for {Duration}s after {FailureCount} consecutive failures")]
    public static partial void LogCircuitBreakerOpened(ILogger logger, double duration, int failureCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Circuit breaker reset, resuming normal operations")]
    public static partial void LogCircuitBreakerReset(ILogger logger);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Circuit breaker is half-open, testing service availability")]
    public static partial void LogCircuitBreakerHalfOpen(ILogger logger);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "HTTP request timed out after {Timeout}s")]
    public static partial void LogHttpRequestTimedOut(ILogger logger, double timeout);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Database retry {RetryCount} after {Delay}ms. Error: {Error}")]
    public static partial void LogDatabaseRetry(ILogger logger, int retryCount, double delay, string error);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Redis retry {RetryCount} after {Delay}ms. Error: {Error}")]
    public static partial void LogRedisRetry(ILogger logger, int retryCount, double delay, string error);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "LLM API rate limited. Waiting {RetryAfter}s as requested by server")]
    public static partial void LogLlmApiRateLimited(ILogger logger, double retryAfter);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "LLM API retry {RetryCount} after {Delay}ms. Status: {Status}")]
    public static partial void LogLlmApiRetry(ILogger logger, int retryCount, double delay, string status);
}
