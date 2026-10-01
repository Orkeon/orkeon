using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using System.Net;
using Orkeon.Domain.Constants.Resilience;

namespace Orkeon.Infrastructure.Resilience;

/// <summary>
/// The Polly policies on the execution path: the LLM HTTP retry policy every provider runs
/// under, and the Redis retry policy of <c>RedisMemoryProvider</c>.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// True when <paramref name="exception"/> is what <see cref="HttpClient"/> throws once its
    /// <see cref="HttpClient.Timeout"/> elapses: a <see cref="TaskCanceledException"/> nesting
    /// a <see cref="TimeoutException"/> (since .NET 5). A caller's own cancellation nests none.
    /// </summary>
    /// <remarks>
    /// The token is no discriminator: HttpClient cancels its own linked source on a timeout,
    /// so the exception's <see cref="OperationCanceledException.CancellationToken"/> reads as
    /// cancelled in both cases. The retry clause written as
    /// <c>!ex.CancellationToken.IsCancellationRequested</c> therefore never fired on a real
    /// timeout — every timeout looked like a cancellation and failed on its first attempt,
    /// whatever <c>Llm:MaxRetries</c> said (LLM-11).
    /// </remarks>
    /// <param name="exception">The exception a send failed with.</param>
    public static bool IsHttpClientTimeout(Exception? exception) =>
        exception is TaskCanceledException { InnerException: TimeoutException };

    /// <summary>The transient failures that come back in seconds: a request error, a 5xx, a 408.</summary>
    private static PolicyBuilder<HttpResponseMessage> HandleTransientHttpStatusOrRequestError()
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.RequestTimeout);
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
    /// <param name="maxRetryAttempts">Maximum retry attempts; defaults to <see cref="Orkeon.Domain.Constants.Llm.LlmDefaults.DefaultMaxRetries"/>.</param>
    /// <param name="baseDelay">Base delay between retries; defaults to 1 second.</param>
    /// <param name="onRetry">Optional per-retry notification hook: (retry number, effective delay, reason). Must not throw.</param>
    /// <returns>An async retry policy for LLM API HTTP calls.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetLlmApiPolicy(
        ILogger? logger = null,
        int maxRetryAttempts = Orkeon.Domain.Constants.Llm.LlmDefaults.DefaultMaxRetries,
        TimeSpan? baseDelay = null,
        Action<int, TimeSpan, string>? onRetry = null)
    {
        var delay = baseDelay ?? ResilienceDefaults.DefaultRetryInitialDelay;
        var safeLogger = logger ?? NullLogger.Instance;

        // The failures that come back in seconds take the whole Llm:MaxRetries budget.
        var transient = HandleTransientHttpStatusOrRequestError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(maxRetryAttempts,
                retryAttempt => LlmRetryDelay(retryAttempt, delay),
                onRetry: async (outcome, timespan, retryCount, context) =>
                {
                    var reason = outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message ?? "Error";
                    if (outcome.Result?.Headers.RetryAfter?.Delta != null)
                    {
                        // Capped like the streaming path's Retry-After: an interactive turn
                        // never parks for minutes on a server-suggested wait.
                        var retryAfter = outcome.Result.Headers.RetryAfter.Delta.Value;
                        if (retryAfter > ResilienceDefaults.DefaultRetryMaxDelay)
                            retryAfter = ResilienceDefaults.DefaultRetryMaxDelay;
                        ResiliencePoliciesLog.LogLlmApiRateLimited(safeLogger, retryAfter.TotalSeconds);
                        onRetry?.Invoke(retryCount, timespan + retryAfter, reason);
                        await System.Threading.Tasks.Task.Delay(retryAfter).ConfigureAwait(false);
                    }
                    else
                    {
                        ResiliencePoliciesLog.LogLlmApiRetry(safeLogger, retryCount, timespan.TotalMilliseconds, reason);
                        onRetry?.Invoke(retryCount, timespan, reason);
                    }
                });

        // A call that hit Llm:TimeoutSeconds costs the whole timeout per attempt, so it gets
        // its own, short budget (ResilienceDefaults.LlmTimeoutRetries) instead of the ladder
        // above — and none at all when the caller turned retries off. The outer policy does
        // not handle the timeout, so a second one surfaces to the provider, whose message
        // then names the setting that elapsed (LLM-11).
        var timeoutRetries = maxRetryAttempts > 0 ? ResilienceDefaults.LlmTimeoutRetries : 0;
        var timedOut = Policy<HttpResponseMessage>
            .Handle<TaskCanceledException>(IsHttpClientTimeout)
            .WaitAndRetryAsync(timeoutRetries,
                retryAttempt => LlmRetryDelay(retryAttempt, delay),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var reason = outcome.Exception?.Message ?? "HTTP timeout";
                    ResiliencePoliciesLog.LogLlmApiRetry(safeLogger, retryCount, timespan.TotalMilliseconds, reason);
                    onRetry?.Invoke(retryCount, timespan, reason);
                });

        return Policy.WrapAsync(transient, timedOut);
    }

    /// <summary>
    /// The LLM retry ladder: linear for the first two retries, then base×3^(n−2), every wait
    /// capped at <see cref="ResilienceDefaults.DefaultRetryMaxDelay"/> so a 10-retry budget
    /// (see <see cref="Orkeon.Domain.Constants.Llm.LlmDefaults.DefaultMaxRetries"/>) degrades
    /// to a bounded ~30 s cadence instead of exploding exponentially.
    /// </summary>
    public static TimeSpan LlmRetryDelay(int retryAttempt, TimeSpan baseDelay)
    {
        // Cap in double space BEFORE converting: 3^(n−2) seconds overflows TimeSpan around
        // retry 27, and a caller-configured budget is unbounded.
        var seconds = retryAttempt > 2
            ? baseDelay.TotalSeconds * Math.Pow(3, retryAttempt - 2)
            : baseDelay.TotalSeconds * retryAttempt;
        return seconds >= ResilienceDefaults.DefaultRetryMaxDelay.TotalSeconds
            ? ResilienceDefaults.DefaultRetryMaxDelay
            : TimeSpan.FromSeconds(seconds);
    }
}

internal static partial class ResiliencePoliciesLog
{
    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Redis retry {RetryCount} after {Delay}ms. Error: {Error}")]
    public static partial void LogRedisRetry(ILogger logger, int retryCount, double delay, string error);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "LLM API rate limited. Waiting {RetryAfter}s as requested by server")]
    public static partial void LogLlmApiRateLimited(ILogger logger, double retryAfter);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "LLM API retry {RetryCount} after {Delay}ms. Status: {Status}")]
    public static partial void LogLlmApiRetry(ILogger logger, int retryCount, double delay, string status);
}
