using Orkeon.Application.Agent.Commands.CreateAgent;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.Fixtures;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Services.Handlers;

/// <summary>
/// Unit tests for <see cref="CreateAgentHandler"/>.
/// </summary>
public sealed class CreateAgentHandlerTests
{
    private readonly TestAgentRepository _repository;
    private readonly TestLogger<CreateAgentHandler> _logger;
    private readonly CreateAgentHandler _handler;

    public CreateAgentHandlerTests()
    {
        _repository = new TestAgentRepository();
        _logger = new TestLogger<CreateAgentHandler>();
        _handler = new CreateAgentHandler(_repository, _logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateAgent_WhenValidCommandProvided()
    {
        // Arrange
        var command = new CreateAgentCommand(
            Role: RoleSeniorDeveloper,
            Goal: "Write clean code",
            Backstory: "10 years of experience in C#",
            Tools: ["file_read", "code_review"]);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(RoleSeniorDeveloper, result.Role);
        Assert.Equal("Write clean code", result.Goal);
        Assert.Equal("10 years of experience in C#", result.Backstory);
        Assert.True(result.Verbose);
        Assert.NotEmpty(result.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSaveToRepository_WhenAgentCreated()
    {
        // Arrange
        var command = new CreateAgentCommand(
            Role: RoleAnalyst,
            Goal: GoalAnalyzeData,
            Backstory: "Expert analyst",
            Tools: null);

        // Act
        await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(1, _repository.AddAsyncCallCount);
        Assert.Single(_repository.StoredAgents);
        var storedAgent = _repository.StoredAgents[0];
        Assert.Equal(RoleAnalyst, storedAgent.Role.Value);
        Assert.Equal(GoalAnalyzeData, storedAgent.Goal.Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnCorrectDto_WhenAgentCreated()
    {
        // Arrange
        var command = new CreateAgentCommand(
            Role: "Researcher",
            Goal: "Find information",
            Backstory: "PhD in Information Science",
            Tools: ["web_search"]);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.IsType<AgentDto>(result);
        Assert.Equal("Researcher", result.Role);
        Assert.Equal("Find information", result.Goal);
        Assert.Equal("PhD in Information Science", result.Backstory);
        Assert.Single(result.Tools);
        Assert.Equal("web_search", result.Tools[0]);
        Assert.Equal("Idle", result.Status);
        Assert.NotEqual(default, result.CreatedAt);
        Assert.NotEqual(default, result.UpdatedAt);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleNullBackstory_WhenNotProvided()
    {
        // Arrange
        var command = new CreateAgentCommand(
            Role: "Writer",
            Goal: "Write articles",
            Backstory: null,
            Tools: null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Backstory);
        Assert.Equal("Writer", result.Role);
        Assert.Equal("Write articles", result.Goal);
        // Verify that the domain agent's backstory is null
        var storedAgent = _repository.StoredAgents[0];
        Assert.Null(storedAgent.Backstory);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleEmptyTools_WhenNoToolsSpecified()
    {
        // Arrange
        var command = new CreateAgentCommand(
            Role: "Reviewer",
            Goal: "Review documents",
            Backstory: "Expert reviewer",
            Tools: []);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Tools);
        Assert.Equal(1, _repository.AddAsyncCallCount);
    }
}
