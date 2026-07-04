using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// Task router that throws a configurable exception type.
/// </summary>
internal class ExceptionThrowingTaskRouter<TException> : IA2ATaskRouter
    where TException : Exception
{
    private readonly TException _exception;

    public ExceptionThrowingTaskRouter(TException exception)
    {
        _exception = exception;
    }

    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, CancellationToken ct = default)
    {
        throw _exception;
    }
}

/// <summary>
/// Tests for granular exception handling in A2AServer.
/// Verifies that different exception types are logged at appropriate levels
/// and produce correct HTTP status codes.
/// </summary>
public class A2AServerErrorHandlingTests
{
    /// <summary>
    /// Asks the OS for a free ephemeral port on loopback. Hardcoded ports leak across
    /// runs on Windows (HTTP.sys namespace reservation) and collide with other processes.
    /// </summary>
    private static int GetFreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>
    /// ANT-001: the server resolves the scoped <see cref="IAgentRepository"/> through a
    /// per-request DI scope; the stub factory hands a shared in-memory repository to every scope.
    /// </summary>
    private static StubServiceScopeFactory AgentScopes()
        => new StubServiceScopeFactory()
            .With<IAgentRepository>(new InMemoryAgentRepository(new NullUnitOfWork()));

    private static async Task<HttpResponseMessage> SendTaskRequest(int port, string taskId = "test-err")
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var body = JsonSerializer.Serialize(new A2ATaskRequest
        {
            Id = taskId,
            SkillId = "researcher",
            Input = "test input"
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await httpClient.PostAsync($"http://localhost:{port}/a2a/tasks/send", content);
    }

    [Fact]
    public async Task HandleRequest_HttpRequestException_LogsWarningAndReturns502()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var exception = new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable);
        var router = new ExceptionThrowingTaskRouter<HttpRequestException>(exception);
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            // Act
            var response = await SendTaskRequest(port);
            await Polling.WaitUntilAsync(() => logger.HasLoggedWarning("HTTP request error"));

            // Assert — HTTP 502 Bad Gateway
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            // Assert — logged at Warning level
            Assert.True(
                logger.HasLoggedWarning("HTTP request error"),
                $"Expected a Warning log about HTTP request error. Logged: [{string.Join("; ", logger.LoggedMessages)}]");

            // Assert — no Error-level log for this exception type
            Assert.False(
                logger.LogEntries.Any(e => e.LogLevel == LogLevel.Error && e.Message.Contains("handling A2A request")),
                "HttpRequestException should NOT be logged at Error level");

            // Assert — server continues running
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task HandleRequest_TimeoutException_LogsWarningAndReturns504()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new ExceptionThrowingTaskRouter<TimeoutException>(new TimeoutException("Request timed out"));
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            // Act
            var response = await SendTaskRequest(port);
            await Polling.WaitUntilAsync(() => logger.HasLoggedWarning("timed out"));

            // Assert — HTTP 504 Gateway Timeout
            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);

            // Assert — logged at Warning level
            Assert.True(
                logger.HasLoggedWarning("timed out"),
                $"Expected a Warning log about timeout. Logged: [{string.Join("; ", logger.LoggedMessages)}]");

            // Assert — server continues running
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task HandleRequest_JsonException_LogsErrorAndReturns400()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new ExceptionThrowingTaskRouter<JsonException>(new JsonException("Unexpected JSON token"));
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            // Act
            var response = await SendTaskRequest(port);
            await Polling.WaitUntilAsync(() => logger.HasLoggedError("JSON parse error"));

            // Assert — HTTP 400 Bad Request
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // Assert — logged at Error level
            Assert.True(
                logger.HasLoggedError("JSON parse error"),
                $"Expected an Error log about JSON parse error. Logged: [{string.Join("; ", logger.LoggedMessages)}]");

            // Assert — server continues running
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task HandleRequest_UnexpectedException_LogsErrorAndReturns500()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new ExceptionThrowingTaskRouter<InvalidOperationException>(
            new InvalidOperationException("Unexpected logic error"));
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            // Act
            var response = await SendTaskRequest(port);
            await Polling.WaitUntilAsync(() => logger.LogEntries.Any(e => e.LogLevel == LogLevel.Error && e.Message.Contains("A2A request", StringComparison.OrdinalIgnoreCase)));

            // Assert — HTTP 500 Internal Server Error
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            // Assert — logged at Error level (from HandleRequestAsync rethrow + ListenLoop catch)
            Assert.True(
                logger.LogEntries.Any(e =>
                    e.LogLevel == LogLevel.Error &&
                    e.Message.Contains("A2A request", StringComparison.OrdinalIgnoreCase)),
                $"Expected an Error log about A2A request. Logged: [{string.Join("; ", logger.LoggedMessages)}]");

            // Assert — server continues running (listen loop catches and continues)
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task HandleRequest_OperationCanceledException_LogsDebug()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new ExceptionThrowingTaskRouter<OperationCanceledException>(
            new OperationCanceledException("Cancelled"));
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            // Act — send a request that triggers OperationCanceledException
            try
            {
                await SendTaskRequest(port);
            }
            catch
            {
                // Connection may be reset since server breaks out of listen loop
            }

            await Polling.WaitUntilAsync(() => logger.HasLoggedDebug("cancelled"));

            // Assert — logged at Debug level (expected during shutdown)
            Assert.True(
                logger.HasLoggedDebug("cancelled"),
                $"Expected a Debug log about cancellation. Logged: [{string.Join("; ", logger.LoggedMessages)}]");

            // Assert — no Error-level log for cancellation
            Assert.False(
                logger.LogEntries.Any(e =>
                    e.LogLevel == LogLevel.Error &&
                    e.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)),
                "OperationCanceledException should NOT be logged at Error level");
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task HandleRequest_SuccessfulRequest_NoErrorOrWarningLogs()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new StubA2ATaskRouter();
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            // Act
            var response = await SendTaskRequest(port);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            // Assert — HTTP 200
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Assert — no Error or Warning logs
            Assert.False(
                logger.LogEntries.Any(e => e.LogLevel == LogLevel.Error),
                $"Expected no Error logs. Logged: [{string.Join("; ", logger.LoggedMessages)}]");

            Assert.False(
                logger.LogEntries.Any(e => e.LogLevel == LogLevel.Warning),
                $"Expected no Warning logs. Logged: [{string.Join("; ", logger.LoggedMessages)}]");
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ListenLoop_ContinuesAfterGranularExceptions()
    {
        // Arrange — router throws HttpRequestException on first call, succeeds on second
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var sequenceRouter = new SequenceTaskRouter(
            new HttpRequestException("Transient error", null, HttpStatusCode.ServiceUnavailable),
            successAfterFailures: true);
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, sequenceRouter, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            // First request — triggers HttpRequestException, handled gracefully
            var response1 = await SendTaskRequest(port, "req-1");
            Assert.Equal(HttpStatusCode.BadGateway, response1.StatusCode);

            await Task.Delay(100, TestContext.Current.CancellationToken);

            // Second request — should succeed, proving server continues
            var response2 = await SendTaskRequest(port, "req-2");
            Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

            var body = await response2.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Success after failure", body);

            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>
/// Task router that throws on the first call and succeeds on subsequent calls.
/// Supports throwing a specific exception type.
/// </summary>
internal class SequenceTaskRouter : IA2ATaskRouter
{
    private readonly Exception _firstCallException;
    private readonly bool _successAfterFailures;
    private int _callCount;

    public SequenceTaskRouter(Exception firstCallException, bool successAfterFailures = true)
    {
        _firstCallException = firstCallException;
        _successAfterFailures = successAfterFailures;
    }

    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, CancellationToken ct = default)
    {
        var call = Interlocked.Increment(ref _callCount);
        if (call == 1)
            throw _firstCallException;

        if (_successAfterFailures)
        {
            return Task.FromResult(new A2ATaskResponse
            {
                TaskId = request.Id,
                Status = A2ATaskStatus.Completed,
                Output = "Success after failure"
            });
        }

        throw _firstCallException;
    }
}
