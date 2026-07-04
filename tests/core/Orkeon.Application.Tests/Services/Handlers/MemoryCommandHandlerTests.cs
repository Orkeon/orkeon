using Orkeon.Application.Memory.Commands.AddMemory;
using Orkeon.Application.Memory.Commands.CreateMemoryStore;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Tests.Services.Handlers;

/// <summary>
/// Unit tests for <see cref="AddMemoryCommandHandler"/> and <see cref="CreateMemoryStoreCommandHandler"/>.
/// </summary>
public sealed class MemoryCommandHandlerTests
{
    private static readonly string[] ProjectContextTags = ["project", "context"];
    private static readonly string[] ImportantTags = ["important"];

    private readonly TestAgentMemoryStoreRepository _repository;
    private readonly TestLogger<AddMemoryCommandHandler> _addMemoryLogger;
    private readonly TestLogger<CreateMemoryStoreCommandHandler> _createStoreLogger;
    private readonly AddMemoryCommandHandler _addMemoryHandler;
    private readonly CreateMemoryStoreCommandHandler _createStoreHandler;

    public MemoryCommandHandlerTests()
    {
        _repository = new TestAgentMemoryStoreRepository();
        _addMemoryLogger = new TestLogger<AddMemoryCommandHandler>();
        _createStoreLogger = new TestLogger<CreateMemoryStoreCommandHandler>();
        _addMemoryHandler = new AddMemoryCommandHandler(_repository, _addMemoryLogger);
        _createStoreHandler = new CreateMemoryStoreCommandHandler(_repository, _createStoreLogger);
    }

    // -- AddMemoryCommandHandler tests --

    [Fact]
    public async System.Threading.Tasks.Task ShouldAddMemory_WhenStoreExists()
    {
        // Arrange
        var agentId = AgentId.Create();
        var existingStore = AgentMemoryStore.Create(agentId);
        _repository.SeedStore(existingStore);

        var command = new AddMemoryCommand(
            AgentId: agentId.ToString(),
            Content: "Important context about the project",
            MemoryType: MemoryType.ShortTerm,
            Importance: 0.5f,
            Source: "test",
            Tags: ProjectContextTags);

        // Act
        var result = await _addMemoryHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.MemoryItemId);
        Assert.Equal(existingStore.Id.ToString(), result.MemoryStoreId);
        Assert.False(result.PromotedToLongTerm);
        // Should not have created a new store
        Assert.Equal(0, _repository.AddAsyncCallCount);
        // Should have updated the existing store
        Assert.Equal(1, _repository.UpdateAsyncCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateStore_WhenStoreDoesNotExist()
    {
        // Arrange
        var agentId = AgentId.Create();
        var command = new AddMemoryCommand(
            AgentId: agentId.ToString(),
            Content: "First memory for this agent",
            MemoryType: MemoryType.ShortTerm,
            Importance: 0.3f,
            Source: "test",
            Tags: null);

        // Act
        var result = await _addMemoryHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.MemoryItemId);
        Assert.NotEmpty(result.MemoryStoreId);
        Assert.False(result.PromotedToLongTerm);
        // Should have created a new store (AddAsync) then updated it
        Assert.Equal(1, _repository.AddAsyncCallCount);
        Assert.Equal(1, _repository.UpdateAsyncCallCount);
        Assert.Single(_repository.StoredStores);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPromoteToLongTerm_WhenMemoryTypeIsLongTerm()
    {
        // Arrange
        var agentId = AgentId.Create();
        var existingStore = AgentMemoryStore.Create(agentId);
        _repository.SeedStore(existingStore);

        var command = new AddMemoryCommand(
            AgentId: agentId.ToString(),
            Content: "Critical knowledge that must be retained",
            MemoryType: MemoryType.LongTerm,
            Importance: 0.9f,
            Source: "critical",
            Tags: ImportantTags);

        // Act
        var result = await _addMemoryHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.PromotedToLongTerm);
        // Verify the store has the memory in long-term
        var store = _repository.StoredStores.First(s => s.OwnerAgentId == agentId);
        Assert.Single(store.LongTermMemory);
        Assert.Equal("Critical knowledge that must be retained", store.LongTermMemory[0].Content);
    }

    // -- CreateMemoryStoreCommandHandler tests --

    [Fact]
    public async System.Threading.Tasks.Task ShouldCreateNewStore_WhenNotExisting()
    {
        // Arrange
        var agentId = AgentId.Create();
        var command = new CreateMemoryStoreCommand(
            AgentId: agentId.ToString(),
            ShortTermCapacity: 50,
            LongTermCapacity: 500);

        // Act
        var result = await _createStoreHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.MemoryStoreId);
        Assert.Equal(agentId.ToString(), result.AgentId);
        Assert.Equal(50, result.ShortTermCapacity);
        Assert.Equal(500, result.LongTermCapacity);
        Assert.Equal(1, _repository.AddAsyncCallCount);
        Assert.Single(_repository.StoredStores);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnExistingStore_WhenAlreadyExists()
    {
        // Arrange
        var agentId = AgentId.Create();
        var existingStore = AgentMemoryStore.Create(agentId, shortTermCapacity: 30, longTermCapacity: 300);
        _repository.SeedStore(existingStore);

        var command = new CreateMemoryStoreCommand(
            AgentId: agentId.ToString(),
            ShortTermCapacity: 50,
            LongTermCapacity: 500);

        // Act
        var result = await _createStoreHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(existingStore.Id.ToString(), result.MemoryStoreId);
        Assert.Equal(agentId.ToString(), result.AgentId);
        // Should return existing capacities, not the new requested ones
        Assert.Equal(30, result.ShortTermCapacity);
        Assert.Equal(300, result.LongTermCapacity);
        // Should NOT have called AddAsync since it already exists
        Assert.Equal(0, _repository.AddAsyncCallCount);
    }
}
