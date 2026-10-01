using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// GAP-10 over the wire: the skill id a peer reads from the agent card runs that agent,
/// <c>DELETE /a2a/tasks/{id}</c> interrupts a task the agent is still working on, and
/// <c>AddOrkeonA2A(EnableServer)</c> hosts the server as an <see cref="IHostedService"/>.
/// </summary>
public class A2AExecutionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private static int GetFreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task<(InMemoryAgentRepository Repo, Orkeon.Domain.Agent.Agent Agent)> RepositoryWithAgentAsync()
    {
        var repo = new InMemoryAgentRepository(new NullUnitOfWork());
        var agent = new AgentBuilder().Role("Researcher").Goal("Research topics").Build();
        await repo.AddAsync(agent, Ct);
        return (repo, agent);
    }

    private static StringContent TaskBody(string id, string skillId, string input)
        => new(JsonSerializer.Serialize(new { id, skillId, input }), Encoding.UTF8, "application/json");

    [Fact]
    public async Task SkillIdReadFromTheAgentCard_RunsTheAgentTheCardDescribes()
    {
        var (repo, agent) = await RepositoryWithAgentAsync();
        var execution = FakeAgentExecutionService.Answering("the report, summarised");
        var scopes = new StubServiceScopeFactory()
            .With<IAgentRepository>(repo)
            .With<IAgentExecutionService>(execution);
        var port = GetFreePort();
        await using var server = new A2AServer(new A2AOptions { Port = port }, new A2ATaskRouter(scopes), scopes);
        await server.StartAsync(Ct);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // A peer follows the card: it reads the published skill id ...
            var cardJson = await http.GetStringAsync($"http://localhost:{port}/.well-known/agent.json", Ct);
            using var card = JsonDocument.Parse(cardJson);
            var skill = Assert.Single(card.RootElement.GetProperty("skills").EnumerateArray());
            var skillId = skill.GetProperty("id").GetString()!;

            // ... and sends a task to it.
            using var body = TaskBody("card-1", skillId, "Summarise this report");
            using var response = await http.PostAsync($"http://localhost:{port}/a2a/tasks/send", body, Ct);
            var result = JsonSerializer.Deserialize<A2ATaskResponse>(
                await response.Content.ReadAsStringAsync(Ct), s_json)!;

            Assert.Equal(A2ATaskStatus.Completed, result.Status);
            Assert.Equal("the report, summarised", result.Output);
            var call = Assert.Single(execution.Calls);
            Assert.Equal(agent.Id, call.Agent.Id);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Delete_InterruptsAnInFlightTask_AndTheRecordEndsCancelled()
    {
        var (repo, agent) = await RepositoryWithAgentAsync();
        var execution = FakeAgentExecutionService.WaitingForCancellation();
        var scopes = new StubServiceScopeFactory()
            .With<IAgentRepository>(repo)
            .With<IAgentExecutionService>(execution);
        var store = new StateStoreA2ATaskStore(new InMemoryStateStore());
        var port = GetFreePort();
        await using var server = new A2AServer(
            new A2AOptions { Port = port }, new A2ATaskRouter(scopes), scopes, taskStore: store);
        await server.StartAsync(Ct);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var body = TaskBody("long-1", agent.Id.ToString(), "A long job");
#pragma warning disable CA2025 // `sending` is awaited below, inside the scope of `http` and `body`.
            var sending = http.PostAsync($"http://localhost:{port}/a2a/tasks/send", body, Ct);
#pragma warning restore CA2025

            // The agent is working: the record says so, and the server still takes requests.
            await execution.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            Assert.Equal(A2ATaskStatus.Working, (await store.GetAsync("long-1", Ct))!.Status);

            using var cancel = await http.DeleteAsync($"http://localhost:{port}/a2a/tasks/long-1", Ct);
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

            using var response = await sending.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            var result = JsonSerializer.Deserialize<A2ATaskResponse>(
                await response.Content.ReadAsStringAsync(Ct), s_json)!;
            Assert.Equal(A2ATaskStatus.Cancelled, result.Status);
            Assert.Equal(A2ATaskStatus.Cancelled, (await store.GetAsync("long-1", Ct))!.Status);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Delete_Returns404_ForATaskThatIsNotRunning_WithoutAStore()
    {
        var port = GetFreePort();
        await using var server = new A2AServer(
            new A2AOptions { Port = port }, new StubA2ATaskRouter(),
            new StubServiceScopeFactory().With<IAgentRepository>(new InMemoryAgentRepository(new NullUnitOfWork())));
        await server.StartAsync(Ct);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var cancel = await http.DeleteAsync($"http://localhost:{port}/a2a/tasks/ghost", Ct);
            Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // Hosted service
    // -------------------------------------------------------------------------

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        return services;
    }

    [Fact]
    public async Task AddOrkeonA2A_WithEnableServer_HostsTheServer_StartAndStopFollowTheHost()
    {
        var port = GetFreePort();
        var services = NewServices();
        services.AddOrkeonA2A(o =>
        {
            o.EnableServer = true;
            o.Port = port;
        });
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // The generic host starts every IHostedService, then stops them on shutdown.
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        var server = provider.GetRequiredService<IA2AServer>();

        await hosted.StartAsync(Ct);
        try
        {
            Assert.True(server.IsRunning);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var card = await http.GetAsync($"http://localhost:{port}/.well-known/agent.json", Ct);
            Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        Assert.False(server.IsRunning);
    }

    [Fact]
    public void AddOrkeonA2A_WithoutEnableServer_HostsNothing()
    {
        var services = NewServices();
        services.AddOrkeonA2A();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IA2AServer));
    }

    [Fact]
    public void AddOrkeonA2A_CalledTwice_RegistersOneServerAndOneHostedService()
    {
        var services = NewServices();
        services.AddOrkeonA2A(o => o.EnableServer = true);
        services.AddOrkeonA2A(o => o.EnableServer = true);

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Single(services, d => d.ServiceType == typeof(IA2AServer));
    }
}
