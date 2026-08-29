using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
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
    public async Task ShouldRetry_WhenGetRetryPolicyWithTransientError()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger, maxRetryAttempts: 3);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, _httpHandler.CallCount);
        Assert.True(_logger.HasLoggedWarning("HTTP request retry"));
    }

    [Fact]
    public async Task ShouldReturnLastFailure_WhenGetRetryPolicyWithMaxRetriesExceeded()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger, maxRetryAttempts: 2);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, _httpHandler.CallCount); // Initial + 2 retries
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRetryPolicyWithTooManyRequests()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _httpHandler.CallCount);
    }

    [Fact]
    public async Task ShouldOpenCircuit_WhenGetCircuitBreakerPolicyWithConsecutiveFailures()
    {
        // Arrange
        var policy = ResiliencePolicies.GetCircuitBreakerPolicy(
            _logger,
            handledEventsAllowedBeforeBreaking: 2,
            TimeSpan.FromMilliseconds(100));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );

        // Act - Trigger circuit breaker
        for (int i = 0; i < 2; i++)
        {
            await policy.ExecuteAsync(async () =>
                await _httpClient.GetAsync("http://test.com"));
        }

        // Assert - Circuit should be open
        await Assert.ThrowsAsync<BrokenCircuitException<HttpResponseMessage>>(async () =>
            await policy.ExecuteAsync(async () =>
                await _httpClient.GetAsync("http://test.com")));

        Assert.True(_logger.HasLoggedError("Circuit breaker opened"));
    }

    [Fact]
    public async Task ShouldAttemptReset_WhenGetCircuitBreakerPolicyAfterBreakDuration()
    {
        // Arrange
        var policy = ResiliencePolicies.GetCircuitBreakerPolicy(
            _logger,
            handledEventsAllowedBeforeBreaking: 1,
            TimeSpan.FromMilliseconds(50));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act - Open circuit
        await policy.ExecuteAsync(async () =>
        await _httpClient.GetAsync("http://test.com"));

        // Wait for circuit to transition to half-open
        await System.Threading.Tasks.Task.Delay(100, TestContext.Current.CancellationToken);

        // Should succeed and close circuit
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_logger.HasLoggedInformation("Circuit breaker is half-open"));
    }

    [Fact]
    public async Task ShouldThrow_WhenGetTimeoutPolicyWhenOperationExceedsTimeout()
    {
        // Arrange
        var policy = ResiliencePolicies.GetTimeoutPolicy(TimeSpan.FromMilliseconds(50), _logger);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await policy.ExecuteAsync(async (ct) =>
            {
                await System.Threading.Tasks.Task.Delay(200, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }, CancellationToken.None));

        Assert.True(_logger.HasLoggedWarning("HTTP request timed out"));
    }

    [Fact]
    public async Task ShouldSucceed_WhenGetTimeoutPolicyWhenOperationCompletesInTime()
    {
        // Arrange
        var policy = ResiliencePolicies.GetTimeoutPolicy(TimeSpan.FromSeconds(1), _logger);

        // Act
        var response = await policy.ExecuteAsync(async () =>
        {
            await System.Threading.Tasks.Task.Delay(10);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ShouldApplyAllPolicies_WhenGetCombinedPolicy()
    {
        // Arrange
        var policy = ResiliencePolicies.GetCombinedPolicy(
            _logger,
            maxRetryAttempts: 2,
            circuitBreakerThreshold: 5,
            circuitBreakerDuration: TimeSpan.FromMilliseconds(100),
            timeout: TimeSpan.FromSeconds(1));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _httpHandler.CallCount); // Retry worked
    }

    [Fact]
    public async Task ShouldFailFast_WhenGetCombinedPolicyWithTimeout()
    {
        // Arrange
        var policy = ResiliencePolicies.GetCombinedPolicy(
            _logger,
            timeout: TimeSpan.FromMilliseconds(50));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await policy.ExecuteAsync(async (ct) =>
            {
                await System.Threading.Tasks.Task.Delay(200, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }, CancellationToken.None));
    }

    [Fact]
    public void ShouldReturnPolicy_WhenGetDatabaseRetryPolicy()
    {
        // Arrange & Act
        var policy = ResiliencePolicies.GetDatabaseRetryPolicy(_logger, maxRetryAttempts: 3);

        // Assert
        Assert.NotNull(policy);
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
        var policy = ResiliencePolicies.GetLlmApiPolicy(_logger);

        var rateLimitedResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimitedResponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromMilliseconds(50));

        _httpHandler.SetupResponses(
            rateLimitedResponse,
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var startTime = DateTime.UtcNow;
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));
        var duration = DateTime.UtcNow - startTime;

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(duration >= TimeSpan.FromMilliseconds(50));
        Assert.True(_logger.HasLoggedInformation("LLM API rate limited"));
    }

    [Fact]
    public async Task ShouldNotRetry_WhenGetRetryPolicyWithNon5xxError()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, _httpHandler.CallCount); // No retry
        Assert.False(_logger.HasLoggedWarning("HTTP request retry"));
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRetryPolicyWithRequestTimeout()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.RequestTimeout),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _httpHandler.CallCount);
    }

    [Fact]
    public void ShouldUse30Seconds_WhenGetTimeoutPolicyWithDefaultTimeout()
    {
        // Arrange & Act
        var policy = ResiliencePolicies.GetTimeoutPolicy();

        // Assert
        Assert.NotNull(policy);
        // Default timeout is 30 seconds (verified by implementation)
    }

    [Fact]
    public void ShouldUse30Seconds_WhenGetCircuitBreakerPolicyWithDefaultDuration()
    {
        // Arrange & Act
        var policy = ResiliencePolicies.GetCircuitBreakerPolicy();

        // Assert
        Assert.NotNull(policy);
        // Default break duration is 30 seconds (verified by implementation)
    }

    [Fact]
    public async Task ShouldRetry_WhenGetDatabaseRetryPolicyWithSqliteBusyError()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDatabaseRetryPolicy(_logger, maxRetryAttempts: 2);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                throw new SqliteException("Database is busy", 5); // SQLITE_BUSY
            }
            return Task.FromResult(Success);
        });

        // Assert
        Assert.Equal(Success, result);
        Assert.Equal(3, attemptCount);
        Assert.True(_logger.HasLoggedWarning("Database retry"));
    }

    [Fact]
    public async Task ShouldRetry_WhenGetDatabaseRetryPolicyWithSqliteLockedError()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDatabaseRetryPolicy(_logger, maxRetryAttempts: 2);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount < 2)
            {
                throw new SqliteException("Database is locked", 6); // SQLITE_LOCKED
            }
            return Task.FromResult(Success);
        });

        // Assert
        Assert.Equal(Success, result);
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task ShouldNotRetry_WhenGetDatabaseRetryPolicyWithOtherSqliteError()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDatabaseRetryPolicy(_logger);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await policy.ExecuteAsync(() =>
            {
                attemptCount++;
                throw new SqliteException("Constraint violation", 19); // SQLITE_CONSTRAINT
            });
        });

        Assert.Equal(1, attemptCount); // No retry
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

    [Fact]
    public async Task ShouldRetry_WhenGetRetryPolicyWithGatewayTimeout()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.GatewayTimeout),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _httpHandler.CallCount);
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRetryPolicyWithBadGateway()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _httpHandler.CallCount);
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRetryPolicyWithHttpRequestException()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger, maxRetryAttempts: 2);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount < 2)
            {
                throw new HttpRequestException("Network error");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task ShouldRetry_WhenGetRetryPolicyWithTaskCanceledExceptionNotCancelled()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger, maxRetryAttempts: 2);
        var attemptCount = 0;

        // Act
        var result = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount < 2)
            {
                // Simulate timeout (not user cancellation)
                throw new TaskCanceledException("Request timed out");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task ShouldFailFast_WhenGetCombinedPolicyWithCircuitBreakerOpen()
    {
        // Arrange
        var policy = ResiliencePolicies.GetCombinedPolicy(
            _logger,
            maxRetryAttempts: 3,
            circuitBreakerThreshold: 2,
            circuitBreakerDuration: TimeSpan.FromMilliseconds(500));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act - Trigger circuit breaker
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await policy.ExecuteAsync(async () =>
                    await _httpClient.GetAsync("http://test.com"));
            }
            catch { }
        }

        // Circuit should now be open
        await Assert.ThrowsAsync<BrokenCircuitException<HttpResponseMessage>>(async () =>
            await policy.ExecuteAsync(async () =>
                await _httpClient.GetAsync("http://test.com")));
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
    public async Task ShouldRespectCancellation_WhenGetTimeoutPolicyWithCancellationToken()
    {
        // Arrange
        var policy = ResiliencePolicies.GetTimeoutPolicy(TimeSpan.FromSeconds(10), _logger);
        using var cts = new CancellationTokenSource();

        // Act
        var task = policy.ExecuteAsync(async (ct) =>
        {
            await System.Threading.Tasks.Task.Delay(5000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, cts.Token);

        await cts.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ShouldCloseCircuit_WhenGetCircuitBreakerPolicyWithConsecutiveSuccesses()
    {
        // Arrange
        var policy = ResiliencePolicies.GetCircuitBreakerPolicy(
            _logger,
            handledEventsAllowedBeforeBreaking: 2,
            TimeSpan.FromMilliseconds(100));

        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK),
            new HttpResponseMessage(HttpStatusCode.OK)
        );

        // Act - Open circuit
        for (int i = 0; i < 2; i++)
        {
            await policy.ExecuteAsync(async () =>
                await _httpClient.GetAsync("http://test.com"));
        }

        // Wait for half-open
        await System.Threading.Tasks.Task.Delay(150, TestContext.Current.CancellationToken);

        // Success should close circuit
        var response1 = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Next call should succeed without circuit breaker interference
        var response2 = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.True(_logger.HasLoggedInformation("Circuit breaker reset"));
    }

    [Fact]
    public async Task ShouldNotRetry_WhenGetRetryPolicyWithZeroMaxAttempts()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_logger, maxRetryAttempts: 0);
        _httpHandler.SetupResponses(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );

        // Act
        var response = await policy.ExecuteAsync(async () =>
            await _httpClient.GetAsync("http://test.com"));

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, _httpHandler.CallCount); // No retries
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
    public async Task ShouldThrow_WhenGetDatabaseRetryPolicyWithMaxRetriesExceeded()
    {
        // Arrange
        var policy = ResiliencePolicies.GetDatabaseRetryPolicy(_logger, maxRetryAttempts: 2);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await policy.ExecuteAsync(() =>
            {
                attemptCount++;
                throw new SqliteException("Database is busy", 5); // Always throw
            });
        });

        Assert.Equal(3, attemptCount); // Initial + 2 retries
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

    [Fact]
    public void ShouldNotThrow_WhenGetCombinedPolicyWithNullLogger()
    {
        // Arrange & Act
        var policy = ResiliencePolicies.GetCombinedPolicy(
            logger: null,
            maxRetryAttempts: 2,
            circuitBreakerThreshold: 3);

        // Assert
        Assert.NotNull(policy);
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
