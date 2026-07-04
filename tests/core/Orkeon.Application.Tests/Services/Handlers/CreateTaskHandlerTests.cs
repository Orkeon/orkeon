using Orkeon.Application.Task.Commands.CreateTask;
using Orkeon.Application.Task.DTOs;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.Fixtures;

namespace Orkeon.Application.Tests.Services.Handlers;

/// <summary>
/// Unit tests for <see cref="CreateTaskHandler"/>.
/// </summary>
public sealed class CreateTaskHandlerTests
{
    private readonly TestTaskRepository _repository;
    private readonly TestLogger<CreateTaskHandler> _logger;
    private readonly CreateTaskHandler _handler;

    public CreateTaskHandlerTests()
    {
        _repository = new TestTaskRepository();
        _logger = new TestLogger<CreateTaskHandler>();
        _handler = new CreateTaskHandler(_repository, _logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateTask_WhenValidCommandProvided()
    {
        // Arrange
        var command = new CreateTaskCommand(
            Description: "Analyze the market trends for Q4",
            ExpectedOutput: "A detailed report with charts",
            AgentId: null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Analyze the market trends for Q4", result.Description);
        Assert.Equal("A detailed report with charts", result.ExpectedOutput);
        Assert.NotEmpty(result.Id);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSaveToRepository_WhenTaskCreated()
    {
        // Arrange
        var command = new CreateTaskCommand(
            Description: "Write unit tests",
            ExpectedOutput: "All tests passing",
            AgentId: null);

        // Act
        await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(1, _repository.AddAsyncCallCount);
        Assert.Single(_repository.StoredTasks);
        var storedTask = _repository.StoredTasks[0];
        Assert.Equal("Write unit tests", storedTask.Description.Value);
        Assert.Equal("All tests passing", storedTask.ExpectedOutput.Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnCorrectDto_WhenTaskCreated()
    {
        // Arrange
        var agentGuid = Guid.NewGuid();
        var command = new CreateTaskCommand(
            Description: "Review pull request",
            ExpectedOutput: "Approved or feedback provided",
            AgentId: agentGuid.ToString());

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.IsType<TaskDto>(result);
        Assert.Equal("Review pull request", result.Description);
        Assert.Equal("Approved or feedback provided", result.ExpectedOutput);
        // The handler converts Guid -> AgentId (ULID) -> ToString(), so
        // we verify the agent was assigned (non-null) rather than exact format
        Assert.NotNull(result.AssignedAgent);
        Assert.Equal("Pending", result.Status);
        Assert.Equal("Normal", result.Priority);
        Assert.NotEqual(default, result.CreatedAt);
        // Also verify the domain entity was properly assigned
        var storedTask = _repository.StoredTasks[0];
        Assert.NotNull(storedTask.AssignedAgent);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSetExpectedOutput_WhenProvided()
    {
        // Arrange
        var command = new CreateTaskCommand(
            Description: "Generate documentation",
            ExpectedOutput: "Markdown files with API docs",
            AgentId: null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal("Markdown files with API docs", result.ExpectedOutput);
        var storedTask = _repository.StoredTasks[0];
        Assert.Equal("Markdown files with API docs", storedTask.ExpectedOutput.Value);
    }
}
