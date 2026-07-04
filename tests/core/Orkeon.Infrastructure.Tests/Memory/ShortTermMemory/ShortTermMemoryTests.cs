using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Memory;

public class ShortTermMemoryTests
{
    private static readonly float[] s_defaultEmbedding = [0.1f, 0.2f, 0.3f];

    #region Constructor Tests

    [Fact]
    public void ShouldCreateMemory_WhenConstructorWithDefaultMaxItems()
    {
        // Act
        var memory = new ShortTermMemory();

        // Assert
        Assert.NotNull(memory);
    }

    [Fact]
    public void ShouldCreateMemory_WhenConstructorWithCustomMaxItems()
    {
        // Act
        var memory = new ShortTermMemory(50);

        // Assert
        Assert.NotNull(memory);
    }

    [Fact]
    public void ShouldCreateMemory_WhenConstructorWithZeroMaxItems()
    {
        // Act
        var memory = new ShortTermMemory(0);

        // Assert
        Assert.NotNull(memory);
    }

    [Fact]
    public void ShouldCreateMemory_WhenConstructorWithNegativeMaxItems()
    {
        // Act
        var memory = new ShortTermMemory(-1);

        // Assert
        Assert.NotNull(memory);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task ShouldAddToMemory_WhenAddAsyncWithValidItem()
    {
        // Arrange
        var memory = new ShortTermMemory();
        var item = CreateMemoryItem(TestContent);

        // Act
        await memory.AddAsync(item);

        // Assert
        var recent = await memory.GetRecentAsync(1);
        Assert.Single(recent);
        Assert.Equal(TestContent, recent[0].Content);
    }

    [Fact]
    public async Task ShouldAddInOrder_WhenAddAsyncWithMultipleItems()
    {
        // Arrange
        var memory = new ShortTermMemory();
        var item1 = CreateMemoryItem("Content 1");
        var item2 = CreateMemoryItem("Content 2");
        var item3 = CreateMemoryItem("Content 3");

        // Act
        await memory.AddAsync(item1);
        await memory.AddAsync(item2);
        await memory.AddAsync(item3);

        // Assert
        var recent = await memory.GetRecentAsync(3);
        Assert.Equal(3, recent.Count);
        // Most recent first
        Assert.Equal("Content 3", recent[0].Content);
        Assert.Equal("Content 2", recent[1].Content);
        Assert.Equal("Content 1", recent[2].Content);
    }

    [Fact]
    public async Task ShouldMaintainSlidingWindow_WhenAddAsyncExceedingMaxItems()
    {
        // Arrange
        var memory = new ShortTermMemory(3);

        // Act
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));
        await memory.AddAsync(CreateMemoryItem("Item 3"));
        await memory.AddAsync(CreateMemoryItem("Item 4")); // Should evict Item 1
        await memory.AddAsync(CreateMemoryItem("Item 5")); // Should evict Item 2

        // Assert
        var recent = await memory.GetRecentAsync(10);
        Assert.Equal(3, recent.Count);
        Assert.Equal("Item 5", recent[0].Content);
        Assert.Equal("Item 4", recent[1].Content);
        Assert.Equal("Item 3", recent[2].Content);
    }

    [Fact]
    public async Task ShouldNotStoreItems_WhenAddAsyncWithMaxItemsZero()
    {
        // Arrange
        var memory = new ShortTermMemory(0);
        var item = CreateMemoryItem(TestContent);

        // Act
        await memory.AddAsync(item);

        // Assert
        var recent = await memory.GetRecentAsync(1);
        Assert.Empty(recent);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenAddAsyncConcurrentAdds()
    {
        // Arrange
        var memory = new ShortTermMemory(100);
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 50; i++)
        {
            int itemIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await memory.AddAsync(CreateMemoryItem($"Concurrent item {itemIndex}"));
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var recent = await memory.GetRecentAsync(100);
        Assert.Equal(50, recent.Count);
        // Should have all items with unique content
        var uniqueContents = recent.Select(r => r.Content).Distinct().Count();
        Assert.Equal(50, uniqueContents);
    }

    [Fact]
    public async Task ShouldNotThrow_WhenAddAsyncWithNullItem()
    {
        // Arrange
        var memory = new ShortTermMemory();

        // Act & Assert - Should not throw
        await memory.AddAsync(null!);

        // Verify it was added
        var recent = await memory.GetRecentAsync(1);
        Assert.Single(recent);
        Assert.Null(recent[0]);
    }

    #endregion

    #region GetRecentAsync Tests

    [Fact]
    public async Task ShouldReturnEmpty_WhenGetRecentAsyncEmptyMemory()
    {
        // Arrange
        var memory = new ShortTermMemory();

        // Act
        var recent = await memory.GetRecentAsync();

        // Assert
        Assert.Empty(recent);
    }

    [Fact]
    public async Task ShouldReturn10_WhenGetRecentAsyncWithDefaultCount()
    {
        // Arrange
        var memory = new ShortTermMemory();
        for (int i = 0; i < 15; i++)
        {
            await memory.AddAsync(CreateMemoryItem($"Item {i}"));
        }

        // Act
        var recent = await memory.GetRecentAsync();

        // Assert
        Assert.Equal(10, recent.Count);
        // Most recent items first
        Assert.Equal("Item 14", recent[0].Content);
        Assert.Equal("Item 5", recent[9].Content);
    }

    [Fact]
    public async Task ShouldReturnRequestedCount_WhenGetRecentAsyncWithSpecificCount()
    {
        // Arrange
        var memory = new ShortTermMemory();
        for (int i = 0; i < 10; i++)
        {
            await memory.AddAsync(CreateMemoryItem($"Item {i}"));
        }

        // Act
        var recent = await memory.GetRecentAsync(5);

        // Assert
        Assert.Equal(5, recent.Count);
        Assert.Equal("Item 9", recent[0].Content);
        Assert.Equal("Item 5", recent[4].Content);
    }

    [Fact]
    public async Task ShouldReturnAllItems_WhenGetRecentAsyncWithCountLargerThanMemory()
    {
        // Arrange
        var memory = new ShortTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));
        await memory.AddAsync(CreateMemoryItem("Item 3"));

        // Act
        var recent = await memory.GetRecentAsync(10);

        // Assert
        Assert.Equal(3, recent.Count);
        Assert.Equal("Item 3", recent[0].Content);
        Assert.Equal("Item 2", recent[1].Content);
        Assert.Equal("Item 1", recent[2].Content);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenGetRecentAsyncWithZeroCount()
    {
        // Arrange
        var memory = new ShortTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));

        // Act
        var recent = await memory.GetRecentAsync(0);

        // Assert
        Assert.Empty(recent);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenGetRecentAsyncWithNegativeCount()
    {
        // Arrange
        var memory = new ShortTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));

        // Act
        var recent = await memory.GetRecentAsync(-5);

        // Assert
        Assert.Empty(recent);
    }

    [Fact]
    public async Task ShouldReturnConsistentResults_WhenGetRecentAsyncCalledMultipleTimes()
    {
        // Arrange
        var memory = new ShortTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));

        // Act
        var recent1 = await memory.GetRecentAsync(2);
        var recent2 = await memory.GetRecentAsync(2);

        // Assert
        Assert.Equal(2, recent1.Count);
        Assert.Equal(2, recent2.Count);
        Assert.Equal(recent1[0].Content, recent2[0].Content);
        Assert.Equal(recent1[1].Content, recent2[1].Content);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task ShouldRemoveAllItems_WhenClearWithItems()
    {
        // Arrange
        var memory = new ShortTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));
        await memory.AddAsync(CreateMemoryItem("Item 3"));

        // Act
        await memory.ClearAsync();

        // Assert
        var recent = await memory.GetRecentAsync(10);
        Assert.Empty(recent);
    }

    [Fact]
    public async Task ShouldNotThrow_WhenClearEmptyMemory()
    {
        // Arrange
        var memory = new ShortTermMemory();

        // Act & Assert - Should not throw
        await memory.ClearAsync();

        var recent = await memory.GetRecentAsync();
        Assert.Empty(recent);
    }

    [Fact]
    public async Task ShouldAllowNewItems_WhenClearAfterClear()
    {
        // Arrange
        var memory = new ShortTermMemory();
        await memory.AddAsync(CreateMemoryItem("Old item"));
        await memory.ClearAsync();

        // Act
        await memory.AddAsync(CreateMemoryItem("New item"));

        // Assert
        var recent = await memory.GetRecentAsync(1);
        Assert.Single(recent);
        Assert.Equal("New item", recent[0].Content);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ShouldWorkCorrectly_WhenIntegrationTestAddGetClearCycle()
    {
        // Arrange
        var memory = new ShortTermMemory(5);

        // Act & Assert - Initial state
        var empty = await memory.GetRecentAsync();
        Assert.Empty(empty);

        // Add items
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));

        var twoItems = await memory.GetRecentAsync();
        Assert.Equal(2, twoItems.Count);

        // Exceed capacity
        for (int i = 3; i <= 7; i++)
        {
            await memory.AddAsync(CreateMemoryItem($"Item {i}"));
        }

        var fiveItems = await memory.GetRecentAsync(10);
        Assert.Equal(5, fiveItems.Count);
        Assert.Equal("Item 7", fiveItems[0].Content);
        Assert.Equal("Item 3", fiveItems[4].Content);

        // Clear
        await memory.ClearAsync();
        var afterClear = await memory.GetRecentAsync();
        Assert.Empty(afterClear);
    }

    [Fact]
    public async Task ShouldWorkCorrectly_WhenIntegrationTestSlidingWindowBehavior()
    {
        // Arrange
        var memory = new ShortTermMemory(3);

        // Act - Fill to capacity
        await memory.AddAsync(CreateMemoryItem("A"));
        await memory.AddAsync(CreateMemoryItem("B"));
        await memory.AddAsync(CreateMemoryItem("C"));

        var atCapacity = await memory.GetRecentAsync(5);
        Assert.Equal(3, atCapacity.Count);
        Assert.Equal("C", atCapacity[0].Content);
        Assert.Equal("A", atCapacity[2].Content);

        // Add one more - should evict A
        await memory.AddAsync(CreateMemoryItem("D"));

        var afterEviction = await memory.GetRecentAsync(5);
        Assert.Equal(3, afterEviction.Count);
        Assert.Equal("D", afterEviction[0].Content);
        Assert.Equal("C", afterEviction[1].Content);
        Assert.Equal("B", afterEviction[2].Content);

        // Add two more - should evict B and C
        await memory.AddAsync(CreateMemoryItem("E"));
        await memory.AddAsync(CreateMemoryItem("F"));

        var finalState = await memory.GetRecentAsync(5);
        Assert.Equal(3, finalState.Count);
        Assert.Equal("F", finalState[0].Content);
        Assert.Equal("E", finalState[1].Content);
        Assert.Equal("D", finalState[2].Content);
    }

    #endregion

    #region Helper Methods

    private static MemoryItem CreateMemoryItem(string content, float[]? embedding = null)
    {
        return MemoryItem.Create(
            content: content,
            embedding: embedding ?? s_defaultEmbedding,
            importance: 1.0f);
    }

    #endregion
}
