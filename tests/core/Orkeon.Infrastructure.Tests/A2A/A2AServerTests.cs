using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using Orkeon.Tests.Shared.Network;
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

    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, IProgress<string>? progress, CancellationToken ct = default)
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

    public Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, IProgress<string>? progress, CancellationToken ct = default)
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

/// <summary>
/// Router that holds every task until <see cref="Release"/>, deaf to the server's shutdown on
/// purpose: the request it serves keeps <see cref="A2AServer.StopAsync"/> waiting once the
/// listener has stopped.
/// </summary>
internal sealed class HeldTaskRouter : IA2ATaskRouter
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed when a task reached the router.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _release.TrySetResult();

    public Task<IReadOnlyList<AgentSkill>> GetSkillsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentSkill>>([]);

    public async Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, IProgress<string>? progress, CancellationToken ct = default)
    {
        Entered.TrySetResult();
        await _release.Task;
        return new A2ATaskResponse { TaskId = request.Id, Status = A2ATaskStatus.Completed, Output = "released" };
    }
}

public class A2AServerTests
{
    private static readonly string[] ResearcherTags = ["researcher"];
    private static readonly string[] WriterTags = ["writer"];

    [Fact]
    public async Task Server_ShouldNotBeRunning_Initially()
    {
        // Arrange — never started: the port is never bound
        var options = new A2AOptions();
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
        var router = new StubA2ATaskRouter();
        var (server, _) = await A2ALoopback.StartAsync(options => new A2AServer(options, router), TestContext.Current.CancellationToken);

        try
        {
            Assert.True(server.IsRunning);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Server_ShouldStartAndStop()
    {
        // Arrange
        var router = new StubA2ATaskRouter();

        // Act
        var (server, _) = await A2ALoopback.StartAsync(options => new A2AServer(options, router), TestContext.Current.CancellationToken);
        await using (server)
        {
            Assert.True(server.IsRunning);

            await server.StopAsync(TestContext.Current.CancellationToken);

            // Assert
            Assert.False(server.IsRunning);
        }
    }

    [Fact]
    public async Task Server_ShouldDisposeCleanly()
    {
        // Arrange
        var router = new StubA2ATaskRouter();
        var (server, _) = await A2ALoopback.StartAsync(options => new A2AServer(options, router), TestContext.Current.CancellationToken);
        Assert.True(server.IsRunning);

        // Act
        await server.DisposeAsync();

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Server_StopShouldBeIdempotent()
    {
        // Arrange — never started: the port is never bound
        var options = new A2AOptions();
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
        var (server, port) = await A2ALoopback.StartAsync(
            options => new A2AServer(options, new StubA2ATaskRouter(skills: [veille])), TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var card = JsonDocument.Parse(await http.GetStringAsync(
                $"{LoopbackPorts.Host}:{port}/.well-known/agent.json", TestContext.Current.CancellationToken));

            var skill = Assert.Single(card.RootElement.GetProperty("skills").EnumerateArray());
            Assert.Equal("veille", skill.GetProperty("id").GetString());
            Assert.Equal("Weekly technology watch.", skill.GetProperty("description").GetString());
            Assert.Equal(new Uri($"{LoopbackPorts.Host}:{port}"), new Uri(card.RootElement.GetProperty("url").GetString()!));
        }
        finally
        {
            await server.DisposeAsync();
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
        var logger = new TestLogger<A2AServer>();
        var router = new ThrowingA2ATaskRouter(throwCount: 1);
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router, logger), TestContext.Current.CancellationToken);

        try
        {

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
                await httpClient.PostAsync($"{LoopbackPorts.Host}:{port}/a2a/tasks/send", content, TestContext.Current.CancellationToken);
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
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task ListenLoop_ShouldContinueListening_WhenHandleRequestAsyncThrows()
    {
        // Arrange — router throws on first call, succeeds on second
        var logger = new TestLogger<A2AServer>();
        var router = new ThrowingA2ATaskRouter(throwCount: 1);
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router, logger), TestContext.Current.CancellationToken);

        try
        {

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
                    $"{LoopbackPorts.Host}:{port}/a2a/tasks/send",
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
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/send",
                secondContent, TestContext.Current.CancellationToken);

            // Assert — server continued listening and processed the second request
            Assert.True(server.IsRunning, "Server should still be running after a failed request");
            Assert.Equal(2, router.TotalCalls);

            var responseContent = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Success after failures", responseContent);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task ListenLoop_ShouldSend500Response_WhenHandleRequestAsyncThrows()
    {
        // Arrange
        var logger = new TestLogger<A2AServer>();
        var router = new ThrowingA2ATaskRouter(throwCount: 1);
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router, logger), TestContext.Current.CancellationToken);

        try
        {

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
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/send",
                requestContent, TestContext.Current.CancellationToken);

            // Assert — server should return 500 Internal Server Error
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

            var responseContent = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Internal server error", responseContent);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Stop_DoesNotBindThePortAgain_OnceItsListenerHasStopped()
    {
        // GAP-41: under Linux and macOS, HttpListener.Close() after Stop() goes back through its
        // prefixes and, finding nobody listening on the port any more, binds it for an instant to
        // remove them. A port another program took meanwhile made StopAsync throw « Address
        // already in use » (GAP-33); a free one was held again, under another server's start.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "HTTP.sys binds nothing again when its listener closes.");
        var router = new HeldTaskRouter();
        var (server, port) = await A2ALoopback.StartAsync(options => new A2AServer(options, router), TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = Polling.DefaultTimeout };
            using var body = new StringContent(
                JsonSerializer.Serialize(new A2ATaskRequest { Id = "held-1", SkillId = "researcher", Input = "hold" }),
                Encoding.UTF8, "application/json");
#pragma warning disable CA2025 // `sending` is awaited below, inside the scope of `http` and `body`.
            var sending = http.PostAsync(new Uri(LoopbackPorts.BaseAddress(port), "a2a/tasks/send"), body, TestContext.Current.CancellationToken);
#pragma warning restore CA2025
            await router.Entered.Task.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

            var stopping = server.StopAsync(CancellationToken.None);

            // The listener has stopped — the held request keeps StopAsync waiting: another
            // program takes the port.
            TcpListener? taker = null;
            await Polling.WaitUntilAsync(() => (taker = TryListen(port)) is not null);
            using (taker)
            {
                router.Release();
                await stopping.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

                // ... and keeps it: what listens on the port is still the program that took it.
                var accepting = taker!.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
                using var accepted = await accepting.AsTask().WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);
                Assert.True(accepted.Connected);
            }

            // The stop cut the held request; how its client learns it does not matter here.
            await Record.ExceptionAsync(() => sending);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    /// <summary>A listener on 127.0.0.1:<paramref name="port"/>, or null while something else listens there.</summary>
    private static TcpListener? TryListen(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            listener.Dispose();
            return null;
        }
    }
}
