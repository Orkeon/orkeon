using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Net;
using Orkeon.Infrastructure.Resilience;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Infrastructure.Tests.Resilience;

public sealed class ResiliencePoliciesTests : IDisposable
{
    private readonly TestLogger _logger;
    private readonly TestHttpMessageHandler _httpHandler;
    private readonly HttpClient _httpClient;

    public ResiliencePoliciesTests()
    {
        _logger = new TestLogger();
        _httpHandler = new TestHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);
    }

    [Fact]
    public void ShouldReturnPolicy_WhenGetRedisRetryPolicy()
    {
        // Arrange & Act
        var policy = ResiliencePolicies.GetRedisRetryPolicy(_logger, maxRetryAttempts: 3);

        // Assert
        Assert.NotNull(policy);
    }

    [Fact]
    public async Task ShouldRetryWithDelay_WhenGetLlmApiPolicyWithRateLimiting()
    {
        // Arrange
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger,
            maxRetryAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(10));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, _httpHandler.CallCount);
        Assert.True(_logger.HasLoggedWarning("LLM API retry"));
    }

    [Fact]
    public void ShouldCapEveryWait_WhenLlmRetryDelayLadderGrows()
    {
        // Linear for the first two retries, ×3 after, capped at 30 s — a 10-retry budget
        // (LlmDefaults.DefaultMaxRetries) degrades to a bounded cadence, never 3^8 seconds.
        var baseDelay = TimeSpan.FromSeconds(1);

        Assert.Equal(TimeSpan.FromSeconds(1), ResiliencePolicies.LlmRetryDelay(1, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(2), ResiliencePolicies.LlmRetryDelay(2, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(3), ResiliencePolicies.LlmRetryDelay(3, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(9), ResiliencePolicies.LlmRetryDelay(4, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(27), ResiliencePolicies.LlmRetryDelay(5, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(30), ResiliencePolicies.LlmRetryDelay(6, baseDelay));  // 81 s → capped
        Assert.Equal(TimeSpan.FromSeconds(30), ResiliencePolicies.LlmRetryDelay(10, baseDelay)); // stays capped
    }

    [Fact]
    public void ShouldStayCapped_WhenTheLadderWouldOverflowTimeSpan()
    {
        // Llm:MaxRetries is user-configured and unbounded: 3^(n−2) seconds exceeds
        // TimeSpan.MaxValue around retry 27 — the cap must apply BEFORE the TimeSpan
        // conversion, never throw OverflowException mid-retry.
        var baseDelay = TimeSpan.FromSeconds(1);

        Assert.Equal(TimeSpan.FromSeconds(30), ResiliencePolicies.LlmRetryDelay(50, baseDelay));
        Assert.Equal(TimeSpan.FromSeconds(30), ResiliencePolicies.LlmRetryDelay(int.MaxValue, baseDelay));
    }

    [Fact]
    public async Task ShouldNotifyTheHook_WhenGetLlmApiPolicyRetries()
    {
        // The onRetry hook is what feeds ILlmRetryObserver (the "reconnecting…" banner).
        var notified = new List<(int Attempt, TimeSpan Delay, string Reason)>();
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger,
            maxRetryAttempts: 2,
            baseDelay: TimeSpan.FromMilliseconds(10),
            onRetry: (attempt, delay, reason) => notified.Add((attempt, delay, reason)));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK));

        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retry = Assert.Single(notified);
        Assert.Equal(1, retry.Attempt);
        Assert.Contains("ServiceUnavailable", retry.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRespectServerDelay_WhenGetLlmApiPolicyWithRetryAfterHeader()
    {
        // Arrange
        var serverDelay = TimeSpan.FromMilliseconds(50);
        var scheduled = new List<(int Attempt, TimeSpan Delay, string Reason)>();
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger,
            onRetry: (attempt, delay, reason) => scheduled.Add((attempt, delay, reason)));

        var rateLimitedResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimitedResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(serverDelay);

        _httpHandler.SetupResponses(
            rateLimitedResponse,
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act. Stopwatch, not DateTime.UtcNow: the wall clock is not monotonic and a step
        // backwards (NTP, a VM resuming) once measured this wait as shorter than the
        // header asked for.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));
        clock.Stop();

        // Assert. The contract is that the server's delay is honoured on top of the
        // ladder: the hook reports the effective wait, and the monotonic clock confirms
        // the call did not return before it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retry = Assert.Single(scheduled);
        Assert.Equal(1, retry.Attempt);
        Assert.True(retry.Delay >= serverDelay, $"effective delay {retry.Delay} < Retry-After {serverDelay}");
        Assert.True(clock.Elapsed >= serverDelay, $"returned after {clock.Elapsed}, before the {serverDelay} the server asked for");
        Assert.True(_logger.HasLoggedInformation("LLM API rate limited"));
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRedisRetryPolicyWithRedisException()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRedisRetryPolicy(_logger, maxRetryAttempts: 2);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount < 2)
            {
                throw new RedisException("Connection failed");
            }
            return Task.FromResult(Success);
        });

        // Assert
        Assert.Equal(Success, result);
        Assert.Equal(2, attemptCount);
        Assert.True(_logger.HasLoggedWarning("Redis retry"));
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRedisRetryPolicyWithRedisTimeoutException()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRedisRetryPolicy(_logger, maxRetryAttempts: 3);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                throw new RedisTimeoutException(CommandFlags.None, "Operation timed out", CommandStatus.Sent);
            }
            return Task.FromResult(Success);
        });

        // Assert
        Assert.Equal(Success, result);
        Assert.Equal(3, attemptCount);
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRedisRetryPolicyWithRedisConnectionException()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRedisRetryPolicy(_logger, maxRetryAttempts: 2);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect, CommandFlags.None, "Cannot connect",
                    innerException: null, CommandStatus.Unknown);
            }
            return Task.FromResult(Success);
        });

        // Assert
        Assert.Equal(Success, result);
        Assert.Equal(2, attemptCount);
    }

    // ── LLM-11: an HttpClient timeout is a TaskCanceledException whose token IS cancelled ──

    /// <summary>
    /// What <see cref="HttpClient"/> throws once its <c>Timeout</c> elapses: the message names
    /// the timeout, a <see cref="TimeoutException"/> is nested, and the token is HttpClient's
    /// own linked source — cancelled. The clause written as
    /// <c>!ex.CancellationToken.IsCancellationRequested</c> never matched this shape.
    /// </summary>
    private static TaskCanceledException HttpClientTimeout(int seconds = 180)
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        return new TaskCanceledException(
            $"The request was canceled due to the configured HttpClient.Timeout of {seconds} seconds elapsing.",
            new TimeoutException("The operation was canceled."),
            cts.Token);
    }

    /// <summary>A caller's own cancellation: a cancelled token and no <see cref="TimeoutException"/>.</summary>
    private static TaskCanceledException CallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        return new TaskCanceledException("The operation was canceled.", null, cts.Token);
    }

    [Fact]
    public void IsHttpClientTimeout_RecognisesTheTimeoutShapeOnly()
    {
        Assert.True(ResiliencePolicies.IsHttpClientTimeout(HttpClientTimeout()));
        Assert.False(ResiliencePolicies.IsHttpClientTimeout(CallerCancellation()));
        Assert.False(ResiliencePolicies.IsHttpClientTimeout(new TaskCanceledException("bare")));
        Assert.False(ResiliencePolicies.IsHttpClientTimeout(new HttpRequestException("refused")));
        Assert.False(ResiliencePolicies.IsHttpClientTimeout(null));
    }

    [Fact]
    public async Task ShouldRetryATimeoutOnce_WhenGetLlmApiPolicyKeepsTimingOut()
    {
        // A timed-out LLM call costs the whole Llm:TimeoutSeconds per attempt, so it gets one
        // more try (ResilienceDefaults.LlmTimeoutRetries), not the ten of the ordinary budget.
        var notified = new List<(int Attempt, string Reason)>();
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 10, baseDelay: TimeSpan.FromMilliseconds(5),
            onRetry: (attempt, _, reason) => notified.Add((attempt, reason)));
        var attemptCount = 0;

        var failure = await Assert.ThrowsAsync<TaskCanceledException>(() => policy.ExecuteAsync(() =>
        {
            attemptCount++;
            throw HttpClientTimeout(seconds: 180);
        }));

        Assert.Equal(1 + Orkeon.Domain.Constants.Resilience.ResilienceDefaults.LlmTimeoutRetries, attemptCount);
        Assert.True(ResiliencePolicies.IsHttpClientTimeout(failure));
        var (attempt, reason) = Assert.Single(notified);
        Assert.Equal(1, attempt);
        Assert.Contains("HttpClient.Timeout of 180 seconds", reason, StringComparison.Ordinal);
        Assert.True(_logger.HasLoggedWarning("LLM API retry"));
    }

    [Fact]
    public async Task ShouldSucceed_WhenGetLlmApiPolicyTimesOutOnceThenAnswers()
    {
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(5));
        var attemptCount = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
                throw HttpClientTimeout();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task ShouldNotRetryATimeout_WhenGetLlmApiPolicyHasNoRetryBudget()
    {
        // Llm:MaxRetries = 0 means "never retry" — the timeout retry obeys it too.
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 0, baseDelay: TimeSpan.FromMilliseconds(5));
        var attemptCount = 0;

        await Assert.ThrowsAsync<TaskCanceledException>(() => policy.ExecuteAsync(() =>
        {
            attemptCount++;
            throw HttpClientTimeout();
        }));

        Assert.Equal(1, attemptCount);
    }

    [Fact]
    public async Task ShouldNotRetry_WhenGetLlmApiPolicyMeetsTheCallersCancellation()
    {
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(5));
        var attemptCount = 0;

        await Assert.ThrowsAsync<TaskCanceledException>(() => policy.ExecuteAsync(() =>
        {
            attemptCount++;
            throw CallerCancellation();
        }));

        Assert.Equal(1, attemptCount);
    }

    [Fact]
    public async Task ShouldStillTakeTheWholeBudget_WhenGetLlmApiPolicyMeetsServerErrors()
    {
        // The split into two policies must not shorten the ordinary ladder.
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(5));
        var attemptCount = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            return Task.FromResult(new HttpResponseMessage(
                attemptCount < 4 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(4, attemptCount);
    }

    [Fact]
    public async Task ShouldUseExponentialBackoff_WhenGetLlmApiPolicyWithLongRetrySequence()
    {
        // Arrange
        var policy = ResiliencePolicies.GetLlmApiPolicy(
            _logger,
            maxRetryAttempts: 4,
            baseDelay: TimeSpan.FromMilliseconds(10));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var startTime = DateTime.UtcNow;
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));
        var duration = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4, _httpHandler.CallCount);
        // Should have exponential delays (10ms, 20ms, 30ms for first 3 retries)
        Assert.True(duration >= TimeSpan.FromMilliseconds(60));
    }

    [Fact]
    public async Task ShouldLogWarning_WhenGetLlmApiPolicyWithRetryAfterDate()
    {
        // Arrange
        var policy = ResiliencePolicies.GetLlmApiPolicy(_logger);

        var rateLimitedResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimitedResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(1));

        _httpHandler.SetupResponses(
            rateLimitedResponse,
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Should log info about the retry
        Assert.True(_logger.HasLoggedInformation("LLM API rate limited") || _logger.HasLoggedWarning("retry"));
    }

    [Fact]
    public async Task ShouldThrow_WhenGetRedisRetryPolicyWithMaxRetriesExceeded()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRedisRetryPolicy(_logger, maxRetryAttempts: 1);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<RedisException>(async () =>
        {
            await policy.ExecuteAsync(() =>
            {
                attemptCount++;
                throw new RedisException("Connection failed"); // Always throw
            });
        });

        Assert.Equal(2, attemptCount); // Initial + 1 retry
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _httpHandler.Dispose();
        GC.SuppressFinalize(this);
    }
}

// Test helpers
public class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public int CallCount { get; private set; }

    public void SetupResponses(params HttpResponseMessage[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;

        if (_responses.Count > 0)
        {
            return Task.FromResult(_responses.Dequeue());
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}

public class TestLogger : ILogger
{
    private readonly List<LogEntry> _logs = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _logs.Add(new LogEntry(logLevel, message));
    }

    public bool HasLoggedWarning(string containing) =>
        _logs.Any(log => log.Level == LogLevel.Warning && log.Message.Contains(containing));

    public bool HasLoggedError(string containing) =>
        _logs.Any(log => log.Level == LogLevel.Error && log.Message.Contains(containing));

    public bool HasLoggedInformation(string containing) =>
        _logs.Any(log => log.Level == LogLevel.Information && log.Message.Contains(containing));

    private record LogEntry(LogLevel Level, string Message);
}
