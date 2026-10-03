using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Stubs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// GAP-10: the router runs the agent whose published skill id (the agent id the card lists)
/// equals the request's <c>skillId</c>, and answers with what the agent produced — never a
/// routing acknowledgement.
/// </summary>
public class A2ATaskRouterTests
{
    private readonly InMemoryAgentRepository _repo = new(new NullUnitOfWork());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private A2ATaskRouter RouterWith(IAgentExecutionService execution, out StubServiceScopeFactory scopes)
    {
        // ANT-001: the router resolves the repository and the execution service through a
        // scope per request; the stub factory hands the same instances to every scope.
        scopes = new StubServiceScopeFactory()
            .With<IAgentRepository>(_repo)
            .With<IAgentExecutionService>(execution);
        return new A2ATaskRouter(scopes);
    }

    private async Task<Orkeon.Domain.Agent.Agent> AddAgentAsync(string role)
    {
        var agent = new AgentBuilder().Role(role).Goal($"Act as {role}").Build();
        await _repo.AddAsync(agent, Ct);
        return agent;
    }

    [Fact]
    public async Task GetSkillsAsync_ListsOneSkillPerAvailableAgent_KeyedByTheIdItRoutes()
    {
        // The card publishes these (GAP-23): the id a peer reads is the id RouteTaskAsync compares.
        var agent = await AddAgentAsync("Researcher");
        var router = RouterWith(FakeAgentExecutionService.Answering("unused"), out _);

        var skill = Assert.Single(await router.GetSkillsAsync(Ct));

        Assert.Equal(agent.Id.ToString(), skill.Id);
        Assert.Equal("Researcher", skill.Name);
        Assert.Equal("Act as Researcher", skill.Description);
        Assert.Equal("researcher", Assert.Single(skill.Tags));
    }

    [Fact]
    public async Task RouteTaskAsync_RunsTheMatchedAgent_AndReturnsItsOutput()
    {
        var agent = await AddAgentAsync("Researcher");
        var execution = FakeAgentExecutionService.Answering("42");
        var router = RouterWith(execution, out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = TaskId1,
            SkillId = agent.Id.ToString(),
            Input = "Summarise this report"
        }, Ct);

        Assert.Equal(TaskId1, response.TaskId);
        Assert.Equal(A2ATaskStatus.Completed, response.Status);
        Assert.Equal("42", response.Output);
        Assert.Null(response.Error);

        var call = Assert.Single(execution.Calls);
        Assert.Equal(agent.Id, call.Agent.Id);
        Assert.Equal("Summarise this report", call.Task.Description.Value);
    }

    [Fact]
    public async Task RouteTaskAsync_ReturnsFailed_WhenTheExecutionFails()
    {
        var agent = await AddAgentAsync("Researcher");
        var router = RouterWith(FakeAgentExecutionService.Failing("the model refused"), out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = TaskId2,
            SkillId = agent.Id.ToString(),
            Input = "Do it"
        }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Equal("the model refused", response.Error);
        Assert.Null(response.Output);
    }

    [Fact]
    public async Task RouteTaskAsync_ReturnsFailed_WhenTheExecutionThrows()
    {
        var agent = await AddAgentAsync("Researcher");
        var router = RouterWith(new FakeAgentExecutionService(
            (_, _, _, _) => throw new InvalidOperationException("provider unreachable")), out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = TaskId3,
            SkillId = agent.Id.ToString(),
            Input = "Do it"
        }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Contains("provider unreachable", response.Error!);
    }

    [Fact]
    public async Task RouteTaskAsync_ReturnsCancelled_WhenTheTokenFiresDuringTheExecution()
    {
        var agent = await AddAgentAsync("Researcher");
        var execution = FakeAgentExecutionService.WaitingForCancellation();
        var router = RouterWith(execution, out _);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var routing = router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = "cancel-1",
            SkillId = agent.Id.ToString(),
            Input = "Long job"
        }, cts.Token);
        await execution.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await cts.CancelAsync();

        var response = await routing.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(A2ATaskStatus.Cancelled, response.Status);
    }

    [Fact]
    public async Task RouteTaskAsync_DoesNotMatchARoleSubstring()
    {
        await AddAgentAsync("Ghostwriter");
        var execution = FakeAgentExecutionService.Answering("ghost text");
        var router = RouterWith(execution, out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = "exact-1",
            SkillId = "writer",
            Input = "Write"
        }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Empty(execution.Calls);
    }

    [Fact]
    public async Task RouteTaskAsync_DoesNotMatchTheRole_OnlyThePublishedId()
    {
        await AddAgentAsync("Researcher");
        var execution = FakeAgentExecutionService.Answering("unused");
        var router = RouterWith(execution, out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = "role-1",
            SkillId = "Researcher",
            Input = "Find papers"
        }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Contains("/.well-known/agent.json", response.Error!);
        Assert.Empty(execution.Calls);
    }

    [Fact]
    public async Task RouteTaskAsync_ReturnsFailed_WhenNoAgentMatches()
    {
        var router = RouterWith(FakeAgentExecutionService.Answering("unused"), out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = TaskId2,
            SkillId = "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            Input = "Do something"
        }, Ct);

        Assert.Equal(TaskId2, response.TaskId);
        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Contains("01ARZ3NDEKTSV4RRFFQ69G5FAV", response.Error!);
    }

    [Fact]
    public async Task RouteTaskAsync_ReturnsFailed_NamingAddOrkeonApplication_WhenOnlyTheNullServiceIsRegistered()
    {
        var agent = await AddAgentAsync("Researcher");
        var router = RouterWith(
            new NullAgentExecutionService(NullLogger<NullAgentExecutionService>.Instance), out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = "null-1",
            SkillId = agent.Id.ToString(),
            Input = "Do it"
        }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Contains("AddOrkeonApplication()", response.Error!);
    }

    [Fact]
    public async Task RouteTaskAsync_ReturnsFailed_NamingAddOrkeonApplication_WhenNoExecutionServiceIsRegistered()
    {
        var agent = await AddAgentAsync("Researcher");
        var router = new A2ATaskRouter(new StubServiceScopeFactory().With<IAgentRepository>(_repo));

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = "none-1",
            SkillId = agent.Id.ToString(),
            Input = "Do it"
        }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Contains("AddOrkeonApplication()", response.Error!);
    }

    [Fact]
    public async Task RouteTaskAsync_ReturnsFailed_WhenTheInputIsEmpty()
    {
        var agent = await AddAgentAsync("Researcher");
        var execution = FakeAgentExecutionService.Answering("unused");
        var router = RouterWith(execution, out _);

        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = "empty-1",
            SkillId = agent.Id.ToString(),
            Input = "   "
        }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Empty(execution.Calls);
    }

    [Fact]
    public async Task RouteTaskAsync_ShouldCreateOneScopePerRequest()
    {
        // R4.6 / ANT-001: the singleton router opens a fresh DI scope for every routed task
        // instead of capturing a scoped repository or execution service.
        var agent = await AddAgentAsync("Researcher");
        var router = RouterWith(FakeAgentExecutionService.Answering("ok"), out var scopes);

        await router.RouteTaskAsync(new A2ATaskRequest { Id = "scope-1", SkillId = agent.Id.ToString(), Input = "a" }, Ct);
        await router.RouteTaskAsync(new A2ATaskRequest { Id = "scope-2", SkillId = agent.Id.ToString(), Input = "b" }, Ct);

        Assert.Equal(2, scopes.CreatedScopeCount);
    }
}
