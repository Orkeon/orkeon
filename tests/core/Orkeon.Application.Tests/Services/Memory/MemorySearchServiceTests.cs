using Orkeon.Application.Services.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Application.Tests.Services.Memory;

public class MemorySearchServiceTests
{
    private readonly MemorySearchService _sut = new();

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldFilterByKeyword_CaseInsensitive()
    {
        // Arrange
        var store = CreateStoreWithShortTermItems(
            ("Important meeting notes about AI", 0.8f),
            ("Research findings on neural networks", 0.7f));

        // Act
        var results = await _sut.SearchAsync(store, "ai", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.Contains("AI", results[0].Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldReturnEmpty_WhenNoKeywordMatch()
    {
        // Arrange
        var store = CreateStoreWithShortTermItems(
            ("Meeting notes about design patterns", 0.5f));

        // Act
        var results = await _sut.SearchAsync(store, "quantum computing", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldFilterByShortTermOnly()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);
        store.AddShortTermMemory(MemoryItem.Create("Short-term data point", importance: 0.5f, source: "test"));
        store.PromoteToLongTermMemory(MemoryItem.Create("Long-term data point", importance: 0.9f, source: "test"));

        // Act
        var results = await _sut.SearchAsync(store, "data", MemoryType.ShortTerm, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.Contains("Short-term", results[0].Content);
        Assert.False(results[0].IsLongTerm);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldFilterByLongTermOnly()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);
        store.AddShortTermMemory(MemoryItem.Create("Short-term data point", importance: 0.5f, source: "test"));
        store.PromoteToLongTermMemory(MemoryItem.Create("Long-term data point", importance: 0.9f, source: "test"));

        // Act
        var results = await _sut.SearchAsync(store, "data", MemoryType.LongTerm, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.Contains("Long-term", results[0].Content);
        Assert.True(results[0].IsLongTerm);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldSearchBothTiers_WhenMemoryTypeIsNull()
    {
        // Arrange
        var agentId = AgentId.Create();
        var store = AgentMemoryStore.Create(agentId);
        store.AddShortTermMemory(MemoryItem.Create("Short-term data point", importance: 0.5f, source: "test"));
        store.PromoteToLongTermMemory(MemoryItem.Create("Long-term data point", importance: 0.9f, source: "test"));

        // Act
        var results = await _sut.SearchAsync(store, "data", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldOrderByImportanceDescending()
    {
        // Arrange
        var store = CreateStoreWithShortTermItems(
            ("Data item low", 0.3f),
            ("Data item high", 0.9f),
            ("Data item medium", 0.6f));

        // Act
        var results = await _sut.SearchAsync(store, "Data", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(0.9f, results[0].Importance);
        Assert.Equal(0.6f, results[1].Importance);
        Assert.Equal(0.3f, results[2].Importance);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldRespectMaxResults()
    {
        // Arrange
        var store = CreateStoreWithShortTermItems(
            ("Data analysis report #0", 0.50f),
            ("Data analysis report #1", 0.55f),
            ("Data analysis report #2", 0.60f),
            ("Data analysis report #3", 0.65f),
            ("Data analysis report #4", 0.70f));

        // Act
        var results = await _sut.SearchAsync(store, "analysis", null, 2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        // Should return the 2 highest importance items
        Assert.True(results[0].Importance >= results[1].Importance);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldReturnEmpty_WhenStoreHasNoItems()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.Create());

        // Act
        var results = await _sut.SearchAsync(store, "anything", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldReturnAllItems_WhenSearchTermIsEmpty()
    {
        // Arrange
        var store = CreateStoreWithShortTermItems(
            ("First item", 0.5f),
            ("Second item", 0.7f));

        // Act
        var results = await _sut.SearchAsync(store, "", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldReturnAllItems_WhenSearchTermIsWhitespace()
    {
        // Arrange
        var store = CreateStoreWithShortTermItems(
            ("First item", 0.5f),
            ("Second item", 0.7f));

        // Act
        var results = await _sut.SearchAsync(store, "   ", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldThrow_WhenStoreIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.SearchAsync(null!, "term", null, 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchAsync_ShouldMapDtoFieldsCorrectly()
    {
        // Arrange
        var store = CreateStoreWithShortTermItems((TestContent, 0.75f));

        // Act
        var results = await _sut.SearchAsync(store, "Test", null, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        var dto = results[0];
        Assert.Equal(TestContent, dto.Content);
        Assert.Equal(0.75f, dto.Importance);
        Assert.False(dto.IsLongTerm);
        Assert.NotNull(dto.Id);
    }

    private static AgentMemoryStore CreateStoreWithShortTermItems(
        params (string Content, float Importance)[] items)
    {
        var store = AgentMemoryStore.Create(AgentId.Create());
        foreach (var (content, importance) in items)
        {
            store.AddShortTermMemory(MemoryItem.Create(content, importance: importance, source: "test"));
        }
        return store;
    }
}
