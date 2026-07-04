using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Infrastructure.Tests.A2A;

public class A2ATaskRouterTests
{
    private readonly InMemoryAgentRepository _repo;
    private readonly StubServiceScopeFactory _scopes;
    private readonly A2ATaskRouter _router;

    public A2ATaskRouterTests()
    {
        // ANT-001: the router resolves IAgentRepository through a scope per request;
        // the stub factory hands the same repository to every scope.
        _repo = new InMemoryAgentRepository(new NullUnitOfWork());
        _scopes = new StubServiceScopeFactory().With<IAgentRepository>(_repo);
        _router = new A2ATaskRouter(_scopes);
    }

    [Fact]
    public async Task RouteTaskAsync_ShouldReturnCompleted_WhenAgentMatches()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Researcher")
            .Goal("Research AI topics")
            .Build();
        await _repo.AddAsync(agent, TestContext.Current.CancellationToken);

        var request = new A2ATaskRequest
        {
            Id = TaskId1,
            SkillId = "Researcher",
            Input = "Research the latest AI trends"
        };

        // Act
        var response = await _router.RouteTaskAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TaskId1, response.TaskId);
        Assert.Equal(A2ATaskStatus.Completed, response.Status);
        Assert.Contains("Researcher", response.Output!);
        Assert.Null(response.Error);
    }

    [Fact]
    public async Task RouteTaskAsync_ShouldReturnFailed_WhenNoAgentMatches()
    {
        // Arrange — no agents in repository
        var request = new A2ATaskRequest
        {
            Id = TaskId2,
            SkillId = "NonExistentSkill",
            Input = "Do something"
        };

        // Act
        var response = await _router.RouteTaskAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TaskId2, response.TaskId);
        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.NotNull(response.Error);
        Assert.Contains("No agent found", response.Error!);
    }

    [Fact]
    public async Task RouteTaskAsync_ShouldMatchPartialSkillName()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Senior Data Analyst")
            .Goal("Analyze data sets")
            .Build();
        await _repo.AddAsync(agent, TestContext.Current.CancellationToken);

        var request = new A2ATaskRequest
        {
            Id = TaskId3,
            SkillId = RoleDataAnalyst,
            Input = "Analyze Q4 sales data"
        };

        // Act
        var response = await _router.RouteTaskAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TaskId3, response.TaskId);
        Assert.Equal(A2ATaskStatus.Completed, response.Status);
        Assert.Contains("Senior Data Analyst", response.Output!);
    }

    [Fact]
    public async Task RouteTaskAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        var agent = new AgentBuilder()
            .Role("Translator")
            .Goal("Translate text")
            .Build();
        await _repo.AddAsync(agent, TestContext.Current.CancellationToken);

        var request = new A2ATaskRequest
        {
            Id = "task-4",
            SkillId = "translator",
            Input = "Translate this to French"
        };

        // Act
        var response = await _router.RouteTaskAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(A2ATaskStatus.Completed, response.Status);
    }

    [Fact]
    public async Task RouteTaskAsync_ShouldCreateOneScopePerRequest()
    {
        // Arrange — R4.6 / ANT-001: the singleton router must open a fresh DI scope
        // for every routed task instead of capturing a scoped repository.
        var request1 = new A2ATaskRequest { Id = "scope-1", SkillId = "Researcher", Input = "a" };
        var request2 = new A2ATaskRequest { Id = "scope-2", SkillId = "Researcher", Input = "b" };

        // Act
        await _router.RouteTaskAsync(request1, TestContext.Current.CancellationToken);
        await _router.RouteTaskAsync(request2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _scopes.CreatedScopeCount);
    }
}
