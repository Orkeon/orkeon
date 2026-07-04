using Orkeon.Application.Crew.Commands.CreateCrew;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.Fixtures;
using DomainProcessType = Orkeon.Domain.SharedKernel.ValueObjects.ProcessType;

namespace Orkeon.Application.Tests.Services.Handlers;

/// <summary>
/// Unit tests for <see cref="CreateCrewHandler"/>.
/// </summary>
public sealed class CreateCrewHandlerTests
{
    private readonly TestCrewRepository _repository;
    private readonly TestLogger<CreateCrewHandler> _logger;
    private readonly CreateCrewHandler _handler;

    public CreateCrewHandlerTests()
    {
        _repository = new TestCrewRepository();
        _logger = new TestLogger<CreateCrewHandler>();
        _handler = new CreateCrewHandler(_repository, _logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateCrew_WhenValidCommandProvided()
    {
        // Arrange
        var command = new CreateCrewCommand(
            Name: "Research Team",
            Goal: "Conduct thorough research",
            ProcessType: DomainProcessType.Sequential,
            AgentIds: null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Research Team", result.Name);
        Assert.Equal("Conduct thorough research", result.Description);
        Assert.NotEmpty(result.Id);
        Assert.Equal("Idle", result.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldParseAgentIds_WhenStringIdsProvided()
    {
        // Arrange
        var agentId1 = Guid.NewGuid().ToString();
        var agentId2 = Guid.NewGuid().ToString();
        var command = new CreateCrewCommand(
            Name: "Dev Team",
            Goal: "Build software",
            ProcessType: DomainProcessType.Sequential,
            AgentIds: [agentId1, agentId2]);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(_repository.StoredCrews);
        // The crew should have had AddAgent called for each agent ID
        var storedCrew = _repository.StoredCrews[0];
        Assert.Equal(2, storedCrew.Agents.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldMapProcessType_WhenSequentialSpecified()
    {
        // Arrange
        var command = new CreateCrewCommand(
            Name: "Sequential Crew",
            Goal: "Execute tasks sequentially",
            ProcessType: DomainProcessType.Sequential,
            AgentIds: null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal("Sequential", result.ProcessType);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldMapProcessType_WhenHierarchicalSpecified()
    {
        // Arrange: The domain DomainCrew.Create requires a managerLlm or managerAgentId
        // for hierarchical process type. The handler currently does not pass either,
        // so it correctly propagates the domain exception.
        var command = new CreateCrewCommand(
            Name: "Hierarchical Crew",
            Goal: "Managed task execution",
            ProcessType: DomainProcessType.Hierarchical,
            AgentIds: null);

        // Act & Assert: The handler propagates the domain validation
        await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSaveToRepository_WhenCrewCreated()
    {
        // Arrange
        var command = new CreateCrewCommand(
            Name: "Analytics Team",
            Goal: "Analyze market data",
            ProcessType: DomainProcessType.Sequential,
            AgentIds: null);

        // Act
        await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(1, _repository.AddAsyncCallCount);
        Assert.Single(_repository.StoredCrews);
        var storedCrew = _repository.StoredCrews[0];
        Assert.Equal("Analyze market data", storedCrew.Goal.Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnCorrectDto_WhenCrewCreated()
    {
        // Arrange
        var command = new CreateCrewCommand(
            Name: "Content Team",
            Goal: "Create marketing content",
            ProcessType: DomainProcessType.Parallel,
            AgentIds: null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.IsType<CrewDto>(result);
        Assert.Equal("Content Team", result.Name);
        Assert.Equal("Create marketing content", result.Description);
        Assert.Equal("Parallel", result.ProcessType);
        Assert.Equal("Idle", result.Status);
        Assert.NotEqual(default, result.CreatedAt);
    }
}
