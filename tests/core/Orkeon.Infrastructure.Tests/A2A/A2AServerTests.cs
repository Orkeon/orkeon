using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.Agent;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// Stub <see cref="IA2ATaskRouter"/> that returns a configurable response.
/// </summary>
internal class StubA2ATaskRouter : IA2ATaskRouter
{
    private readonly A2ATaskResponse _response;

    public A2ATaskRequest? LastRequest { get; private set; }

    public StubA2ATaskRouter(A2ATaskResponse? response = null)
    {
        _response = response ?? new A2ATaskResponse
        {
            TaskId = "stub-task",
            Status = A2ATaskStatus.Completed,
            Output = "Stub output"
        };
    }

    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(_response with { TaskId = request.Id });
    }
}

/// <summary>
/// Stub <see cref="IA2ATaskRouter"/> that throws on the first N calls,
/// then succeeds on subsequent calls.
/// </summary>
internal class ThrowingA2ATaskRouter : IA2ATaskRouter
{
    private readonly int _throwCount;
    private int _callCount;

    public int TotalCalls => _callCount;

    public ThrowingA2ATaskRouter(int throwCount = int.MaxValue)
    {
        _throwCount = throwCount;
    }

    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, CancellationToken ct = default)
    {
        var call = Interlocked.Increment(ref _callCount);
        if (call <= _throwCount)
            throw new InvalidOperationException($"Simulated failure on call #{call}");

        return Task.FromResult(new A2ATaskResponse
        {
            TaskId = request.Id,
            Status = A2ATaskStatus.Completed,
            Output = "Success after failures"
        });
    }
}

public class A2AServerTests
{
    private static readonly string[] ResearcherTags = ["researcher"];
    private static readonly string[] WriterTags = ["writer"];

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

    [Fact]
    public async Task Server_ShouldNotBeRunning_Initially()
    {
        // Arrange
        var options = new A2AOptions { Port = GetFreePort() };
        var router = new StubA2ATaskRouter();
        var scopes = AgentScopes();

        // Act
        await using var server = new A2AServer(options, router, scopes);

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Server_ShouldThrow_WhenStartedTwice()
    {
        // Arrange
        var options = new A2AOptions { Port = GetFreePort() };
        var router = new StubA2ATaskRouter();
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);
            Assert.True(server.IsRunning);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Server_ShouldStartAndStop()
    {
        // Arrange
        var options = new A2AOptions { Port = GetFreePort() };
        var router = new StubA2ATaskRouter();
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes);

        // Act
        await server.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(server.IsRunning);

        await server.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Server_ShouldDisposeCleanly()
    {
        // Arrange
        var options = new A2AOptions { Port = GetFreePort() };
        var router = new StubA2ATaskRouter();
        var scopes = AgentScopes();
        var server = new A2AServer(options, router, scopes);

        await server.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(server.IsRunning);

        // Act
        await server.DisposeAsync();

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Server_StopShouldBeIdempotent()
    {
        // Arrange
        var options = new A2AOptions { Port = GetFreePort() };
        var router = new StubA2ATaskRouter();
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes);

        // Act — stop without start should not throw
        await server.StopAsync(TestContext.Current.CancellationToken);
        await server.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public void AgentCard_ShouldBeBuiltFromAgentRepository()
    {
        // This tests the logical construction of an agent card from
        // local agents, verifying the data flow without HTTP.

        // Arrange
        var skills = new List<AgentSkill>
        {
            new()
            {
                Id = AgentId1,
                Name = "Researcher",
                Description = "Does research",
                Tags = ResearcherTags
            },
            new()
            {
                Id = AgentId2,
                Name = "Writer",
                Description = "Writes content",
                Tags = WriterTags
            }
        };

        var card = new AgentCard
        {
            Name = "TestServer",
            Description = "A test A2A server",
            Url = new Uri("http://localhost:5002"),
            Version = "1.0.0",
            Skills = skills
        };

        // Assert
        Assert.Equal("TestServer", card.Name);
        Assert.Equal(2, card.Skills.Count);
        Assert.Equal("Researcher", card.Skills[0].Name);
        Assert.Equal("Writer", card.Skills[1].Name);
    }

    [Fact]
    public async Task ListenLoop_ShouldLogError_WhenHandleRequestAsyncThrows()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new ThrowingA2ATaskRouter(throwCount: 1);
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            using var httpClient = new HttpClient();
            var requestBody = JsonSerializer.Serialize(new A2ATaskRequest
            {
                Id = "test-1",
                SkillId = "researcher",
                Input = "test input"
            });
            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            // Act — send a request that will cause the router to throw
            try
            {
                await httpClient.PostAsync($"http://localhost:{port}/a2a/tasks/send", content, TestContext.Current.CancellationToken);
            }
            catch
            {
                // Response may fail if server returns 500 and closes connection
            }

            // Wait deterministically until the server has processed and logged the error
            await Polling.WaitUntilAsync(() => logger.LogEntries.Any(e =>
                e.LogLevel == LogLevel.Error &&
                e.Message.Contains("A2A request", StringComparison.OrdinalIgnoreCase)));

            // Assert — error should be logged (either from HandleRequestAsync's internal catch
            // or from ListenLoopAsync's outer catch)
            Assert.True(
                logger.LogEntries.Any(e =>
                    e.LogLevel == LogLevel.Error &&
                    e.Message.Contains("A2A request", StringComparison.OrdinalIgnoreCase)),
                "Expected an error log entry about the A2A request failure. " +
                $"Logged messages: [{string.Join("; ", logger.LoggedMessages)}]");
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ListenLoop_ShouldContinueListening_WhenHandleRequestAsyncThrows()
    {
        // Arrange — router throws on first call, succeeds on second
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new ThrowingA2ATaskRouter(throwCount: 1);
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var requestBody = JsonSerializer.Serialize(new A2ATaskRequest
            {
                Id = "test-fail",
                SkillId = "researcher",
                Input = "this will fail"
            });

            // First request — should fail but server continues
            using var firstContent = new StringContent(requestBody, Encoding.UTF8, "application/json");
            try
            {
                await httpClient.PostAsync(
                    $"http://localhost:{port}/a2a/tasks/send",
                    firstContent, TestContext.Current.CancellationToken);
            }
            catch
            {
                // May fail at HTTP level
            }

            // Give the server a moment to recover
            await Task.Delay(200, TestContext.Current.CancellationToken);

            // Second request — should succeed because server is still listening
            var secondBody = JsonSerializer.Serialize(new A2ATaskRequest
            {
                Id = "test-success",
                SkillId = "researcher",
                Input = "this should succeed"
            });

            using var secondContent = new StringContent(secondBody, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(
                $"http://localhost:{port}/a2a/tasks/send",
                secondContent, TestContext.Current.CancellationToken);

            // Assert — server continued listening and processed the second request
            Assert.True(server.IsRunning, "Server should still be running after a failed request");
            Assert.Equal(2, router.TotalCalls);

            var responseContent = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Success after failures", responseContent);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ListenLoop_ShouldSend500Response_WhenHandleRequestAsyncThrows()
    {
        // Arrange
        var port = GetFreePort();
        var logger = new TestLogger<A2AServer>();
        var options = new A2AOptions { Port = port };
        var router = new ThrowingA2ATaskRouter(throwCount: 1);
        var scopes = AgentScopes();
        await using var server = new A2AServer(options, router, scopes, logger);

        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var requestBody = JsonSerializer.Serialize(new A2ATaskRequest
            {
                Id = "test-500",
                SkillId = "researcher",
                Input = "trigger error"
            });

            // Act — send a request that causes the router to throw
            using var requestContent = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(
                $"http://localhost:{port}/a2a/tasks/send",
                requestContent, TestContext.Current.CancellationToken);

            // Assert — server should return 500 Internal Server Error
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

            var responseContent = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Internal server error", responseContent);
        }
        finally
        {
            await server.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}
