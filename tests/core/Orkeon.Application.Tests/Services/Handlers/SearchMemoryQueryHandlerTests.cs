using Orkeon.Application.Memory.Queries.SearchMemory;
using Orkeon.Application.Services.Memory;
using Orkeon.Application.Tests.Fixtures;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Tests.Services.Handlers;

public class SearchMemoryQueryHandlerTests
{
    private readonly StubAgentMemoryStoreRepository _repository;
    private readonly TestLogger<SearchMemoryQueryHandler> _logger;
    private readonly SearchMemoryQueryHandler _sut;

    public SearchMemoryQueryHandlerTests()
    {
        _repository = new StubAgentMemoryStoreRepository();
        _logger = new TestLogger<SearchMemoryQueryHandler>();
        _sut = new SearchMemoryQueryHandler(_repository, new MemorySearchService(), _logger);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnResults_WhenMemoriesMatch()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);
        var memory1 = MemoryItem.Create("Important meeting notes about AI", importance: 0.8f, source: "meeting");
        var memory2 = MemoryItem.Create("Research findings on neural networks", importance: 0.7f, source: "research");
        store.AddShortTermMemory(memory1);
        store.AddShortTermMemory(memory2);
        _repository.SeedStore(agentId, store);

        var query = new SearchMemoryQuery(
            AgentId: agentId.ToString(),
            SearchTerm: "AI",
            MemoryType: null,
            MaxResults: 10);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Contains("AI", result.Items[0].Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmpty_WhenNoMatch()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);
        var memory = MemoryItem.Create("Meeting notes about design patterns", importance: 0.5f, source: "meeting");
        store.AddShortTermMemory(memory);
        _repository.SeedStore(agentId, store);

        var query = new SearchMemoryQuery(
            AgentId: agentId.ToString(),
            SearchTerm: "quantum computing",
            MemoryType: null,
            MaxResults: 10);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectMaxResults_WhenLimiting()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);
        for (int i = 0; i < 5; i++)
        {
            var memory = MemoryItem.Create(
                $"Data analysis report #{i}",
                importance: 0.5f + (i * 0.05f),
                source: "analysis");
            store.AddShortTermMemory(memory);
        }
        _repository.SeedStore(agentId, store);

        var query = new SearchMemoryQuery(
            AgentId: agentId.ToString(),
            SearchTerm: "analysis",
            MemoryType: null,
            MaxResults: 2);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmpty_WhenStoreNotFound()
    {
        // Arrange
        var agentId = AgentId.Create();
        var query = new SearchMemoryQuery(
            AgentId: agentId.ToString(),
            SearchTerm: "anything",
            MemoryType: null,
            MaxResults: 10);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSearchShortTermOnly_WhenMemoryTypeSpecified()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);

        var shortTermItem = MemoryItem.Create("Short-term data point", importance: 0.5f, source: "test");
        store.AddShortTermMemory(shortTermItem);

        var longTermItem = MemoryItem.Create("Long-term data point", importance: 0.9f, source: "test");
        store.PromoteToLongTermMemory(longTermItem);

        _repository.SeedStore(agentId, store);

        var query = new SearchMemoryQuery(
            AgentId: agentId.ToString(),
            SearchTerm: "data",
            MemoryType: MemoryType.ShortTerm,
            MaxResults: 10);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Contains("Short-term", result.Items[0].Content);
        Assert.False(result.Items[0].IsLongTerm);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSearchLongTermOnly_WhenMemoryTypeSpecified()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);

        var shortTermItem = MemoryItem.Create("Short-term data point", importance: 0.5f, source: "test");
        store.AddShortTermMemory(shortTermItem);

        var longTermItem = MemoryItem.Create("Long-term data point", importance: 0.9f, source: "test");
        store.PromoteToLongTermMemory(longTermItem);

        _repository.SeedStore(agentId, store);

        var query = new SearchMemoryQuery(
            AgentId: agentId.ToString(),
            SearchTerm: "data",
            MemoryType: MemoryType.LongTerm,
            MaxResults: 10);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Contains("Long-term", result.Items[0].Content);
        Assert.True(result.Items[0].IsLongTerm);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOrderByImportanceThenTimestamp()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);

        var lowImportance = MemoryItem.Create("Data item low", importance: 0.3f, source: "test");
        var highImportance = MemoryItem.Create("Data item high", importance: 0.9f, source: "test");
        store.AddShortTermMemory(lowImportance);
        store.AddShortTermMemory(highImportance);
        _repository.SeedStore(agentId, store);

        var query = new SearchMemoryQuery(
            AgentId: agentId.ToString(),
            SearchTerm: "Data",
            MemoryType: null,
            MaxResults: 10);

        // Act
        var result = await _sut.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].Importance >= result.Items[1].Importance);
    }

    /// <summary>
    /// Stub repository for AgentMemoryStore.
    /// </summary>
    private sealed class StubAgentMemoryStoreRepository : IAgentMemoryStoreRepository
    {
        private readonly Dictionary<string, AgentMemoryStore> _stores = [];

        public void SeedStore(AgentId agentId, AgentMemoryStore store)
            => _stores[agentId.ToString()] = store;

        public System.Threading.Tasks.Task<AgentMemoryStore?> GetByAgentIdAsync(AgentId agentId, CancellationToken cancellationToken = default)
        {
            _stores.TryGetValue(agentId.ToString(), out var store);
            return System.Threading.Tasks.Task.FromResult(store);
        }

        public System.Threading.Tasks.Task<AgentMemoryStore?> GetByIdAsync(MemoryStoreId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<AgentMemoryStore?>(null);
        public System.Threading.Tasks.Task AddAsync(AgentMemoryStore aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task UpdateAsync(AgentMemoryStore aggregate, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task DeleteAsync(MemoryStoreId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task<bool> ExistsAsync(MemoryStoreId id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<IReadOnlyList<AgentMemoryStore>> FindAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentMemoryStore>>([]);
        public System.Threading.Tasks.Task<IReadOnlyList<AgentMemoryStore>> FindAsync(ISpecification<AgentMemoryStore> specification, int skip, int take, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentMemoryStore>>([]);
        public System.Threading.Tasks.Task<int> CountAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
        public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(false);
    }
}
