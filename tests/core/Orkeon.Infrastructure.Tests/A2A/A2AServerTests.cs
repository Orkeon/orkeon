using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
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
    private readonly IReadOnlyList<AgentSkill> _skills;

    public A2ATaskRequest? LastRequest { get; private set; }

    public StubA2ATaskRouter(A2ATaskResponse? response = null, IReadOnlyList<AgentSkill>? skills = null)
    {
        _response = response ?? new A2ATaskResponse
        {
            TaskId = "stub-task",
            Status = A2ATaskStatus.Completed,
            Output = "Stub output"
        };
        _skills = skills ?? [];
    }

    public Task<IReadOnlyList<AgentSkill>> GetSkillsAsync(CancellationToken ct = default) => Task.FromResult(_skills);

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

    public Task<IReadOnlyList<AgentSkill>> GetSkillsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentSkill>>([]);

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


    [Fact]
    public async Task Server_ShouldNotBeRunning_Initially()
    {
        // Arrange
        var options = new A2AOptions { Port = GetFreePort() };
        var router = new StubA2ATaskRouter();

        // Act
        await using var server = new A2AServer(options, router);

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Server_ShouldThrow_WhenStartedTwice()
    {
        // Arrange
        var options = new A2AOptions { Port = GetFreePort() };
        var router = new StubA2ATaskRouter();
        await using var server = new A2AServer(options, router);

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
        await using var server = new A2AServer(options, router);

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
        var server = new A2AServer(options, router);

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
        await using var server = new A2AServer(options, router);

        // Act — stop without start should not throw
        await server.StopAsync(TestContext.Current.CancellationToken);
        await server.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task TheCard_PublishesTheSkillsItsRouterAnswers()
    {
        // GAP-23: a host that routes differently (orkeon-host routes crews) registers its own
        // router, and the card publishes what that router answers — never an agent directory
        // the router does not read: the published key and the compared key are one (GAP-10).
        var veille = new AgentSkill { Id = "veille", Name = "veille", Description = "Weekly technology watch." };
        var port = GetFreePort();
        await using var server = new A2AServer(new A2AOptions { Port = port }, new StubA2ATaskRouter(skills: [veille]));
        await server.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var card = JsonDocument.Parse(await http.GetStringAsync(
                $"http://localhost:{port}/.well-known/agent.json", TestContext.Current.CancellationToken));

            var skill = Assert.Single(card.RootElement.GetProperty("skills").EnumerateArray());
            Assert.Equal("veille", skill.GetProperty("id").GetString());
            Assert.Equal("Weekly technology watch.", skill.GetProperty("description").GetString());
            Assert.Equal(new Uri($"http://localhost:{port}"), new Uri(card.RootElement.GetProperty("url").GetString()!));
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("http://+")]
    [InlineData("http://*")]
    public void TheCardUrl_OfAWildcardListener_IsTheAddressThePeerReachedTheCardAt(string host)
    {
        // A wildcard listens on every interface and names no address a peer can call: the card
        // endpoint used to throw on it (UriFormatException), so a server listening beyond the
        // loopback served no card at all.
        var url = A2AServer.CardUrl(
            new A2AOptions { Host = host, Port = 5002 },
            new Uri("http://10.0.0.7:5002/.well-known/agent.json"));

        Assert.Equal(new Uri("http://10.0.0.7:5002/"), url);
    }

    [Fact]
    public void TheCardUrl_OfANamedListener_IsTheListener()
    {
        var url = A2AServer.CardUrl(
            new A2AOptions { Host = "https://a2a.example.org", Port = 8443 },
            new Uri("http://10.0.0.7:8443/.well-known/agent.json"));

        Assert.Equal(new Uri("https://a2a.example.org:8443"), url);
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
        await using var server = new A2AServer(options, router, logger);

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
        await using var server = new A2AServer(options, router, logger);

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
        await using var server = new A2AServer(options, router, logger);

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
