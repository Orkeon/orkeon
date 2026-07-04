using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Memory;

public class LongTermMemoryTests
{
    private static readonly float[] s_defaultEmbedding = [0.1f, 0.2f, 0.3f];

    #region Constructor Tests

    [Fact]
    public void ShouldCreateMemory_WhenConstructorWithDefaultParameters()
    {
        // Act
        using var memory = new LongTermMemory();

        // Assert
        Assert.NotNull(memory);
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task ShouldAddToMemory_WhenAddAsyncWithValidItem()
    {
        // Arrange
        using var memory = new LongTermMemory();
        var item = CreateMemoryItem(TestContent);

        // Act
        await memory.AddAsync(item);

        // Assert
        var allItems = await memory.GetAllAsync();
        Assert.Single(allItems);
        Assert.Equal(TestContent, allItems[0].Content);
    }

    [Fact]
    public async Task ShouldAddAllItems_WhenAddAsyncWithMultipleItems()
    {
        // Arrange
        using var memory = new LongTermMemory();
        var item1 = CreateMemoryItem("Content 1");
        var item2 = CreateMemoryItem("Content 2");
        var item3 = CreateMemoryItem("Content 3");

        // Act
        await memory.AddAsync(item1);
        await memory.AddAsync(item2);
        await memory.AddAsync(item3);

        // Assert
        var allItems = await memory.GetAllAsync();
        Assert.Equal(3, allItems.Count);
        // Items should be ordered by timestamp descending (most recent first)
        Assert.Equal("Content 3", allItems[0].Content);
        Assert.Equal("Content 2", allItems[1].Content);
        Assert.Equal("Content 1", allItems[2].Content);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenAddAsyncConcurrentAdds()
    {
        // Arrange
        using var memory = new LongTermMemory();
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
        var allItems = await memory.GetAllAsync();
        Assert.Equal(50, allItems.Count);
        // Should have all items with unique content
        var uniqueContents = allItems.Select(r => r.Content).Distinct().Count();
        Assert.Equal(50, uniqueContents);
    }

    [Fact]
    public async Task ShouldNotThrow_WhenAddAsyncWithNullItem()
    {
        // Arrange
        using var memory = new LongTermMemory();

        // Act & Assert - Should not throw
        await memory.AddAsync(null!);

        // Verify it was added
        var allItems = await memory.GetAllAsync();
        Assert.Single(allItems);
        Assert.Null(allItems[0]);
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchAsyncEmptyMemory()
    {
        // Arrange
        using var memory = new LongTermMemory();

        // Act
        var results = await memory.SearchAsync("test query");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnMatchingItems_WhenSearchAsyncWithMatchingKeyword()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("This is a test document"));
        await memory.AddAsync(CreateMemoryItem("Another example content"));
        await memory.AddAsync(CreateMemoryItem("Test case number two"));

        // Act
        var results = await memory.SearchAsync("test");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, item =>
            Assert.Contains("test", item.Content.ToLowerInvariant()));
    }

    [Fact]
    public async Task ShouldReturnAnyMatch_WhenSearchAsyncWithMultipleKeywords()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("This is a test document"));
        await memory.AddAsync(CreateMemoryItem("Another example content"));
        await memory.AddAsync(CreateMemoryItem("Sample text file"));

        // Act
        var results = await memory.SearchAsync("test example");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, item => item.Content.Contains("test"));
        Assert.Contains(results, item => item.Content.Contains("example"));
    }

    [Fact]
    public async Task ShouldMatchRegardlessOfCase_WhenSearchAsyncCaseInsensitive()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("TEST Document"));
        await memory.AddAsync(CreateMemoryItem("test document"));
        await memory.AddAsync(CreateMemoryItem("Test Document"));

        // Act
        var results = await memory.SearchAsync("test");

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, item =>
            Assert.Contains("test", item.Content.ToLowerInvariant()));
    }

    [Fact]
    public async Task ShouldLimitResults_WhenSearchAsyncWithMaxResults()
    {
        // Arrange
        using var memory = new LongTermMemory();
        for (int i = 0; i < 10; i++)
        {
            await memory.AddAsync(CreateMemoryItem($"Test document {i}"));
        }

        // Act
        var results = await memory.SearchAsync("test", maxResults: 3);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, item =>
            Assert.Contains("test", item.Content.ToLowerInvariant()));
    }

    [Fact]
    public async Task ShouldOrderByTimestampDescending_WhenSearchAsync()
    {
        // Arrange
        using var memory = new LongTermMemory();
        var item1 = CreateMemoryItem("First test", DateTime.UtcNow.AddMinutes(-10));
        var item2 = CreateMemoryItem("Second test", DateTime.UtcNow.AddMinutes(-5));
        var item3 = CreateMemoryItem("Third test", DateTime.UtcNow);

        await memory.AddAsync(item1);
        await memory.AddAsync(item2);
        await memory.AddAsync(item3);

        // Act
        var results = await memory.SearchAsync("test");

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("Third test", results[0].Content);
        Assert.Equal("Second test", results[1].Content);
        Assert.Equal("First test", results[2].Content);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchAsyncWithEmptyQuery()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Some content"));

        // Act
        var results = await memory.SearchAsync("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchAsyncWithWhitespaceQuery()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Some content"));

        // Act
        var results = await memory.SearchAsync("   ");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchAsyncWithNoMatches()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Document about cats"));
        await memory.AddAsync(CreateMemoryItem("Article about dogs"));

        // Act
        var results = await memory.SearchAsync("elephants");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenSearchAsyncConcurrentSearches()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Test document 1"));
        await memory.AddAsync(CreateMemoryItem("Test document 2"));
        await memory.AddAsync(CreateMemoryItem("Test document 3"));

        var tasks = new List<Task<IReadOnlyList<MemoryItem>>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () => await memory.SearchAsync("test")));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result => Assert.Equal(3, result.Count));
        // All concurrent searches should return the same results
        var firstResult = results[0];
        Assert.All(results, result =>
        {
            Assert.Equal(firstResult.Count, result.Count);
            for (int i = 0; i < firstResult.Count; i++)
            {
                Assert.Equal(firstResult[i].Content, result[i].Content);
            }
        });
    }

    #endregion

    #region GetAllAsync Tests

    [Fact]
    public async Task ShouldReturnEmpty_WhenGetAllAsyncEmptyMemory()
    {
        // Arrange
        using var memory = new LongTermMemory();

        // Act
        var results = await memory.GetAllAsync();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnAllItemsOrderedByTimestamp_WhenGetAllAsyncWithItems()
    {
        // Arrange
        using var memory = new LongTermMemory();
        var item1 = CreateMemoryItem("First item", DateTime.UtcNow.AddMinutes(-10));
        var item2 = CreateMemoryItem("Second item", DateTime.UtcNow.AddMinutes(-5));
        var item3 = CreateMemoryItem("Third item", DateTime.UtcNow);

        await memory.AddAsync(item1);
        await memory.AddAsync(item2);
        await memory.AddAsync(item3);

        // Act
        var results = await memory.GetAllAsync();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("Third item", results[0].Content);
        Assert.Equal("Second item", results[1].Content);
        Assert.Equal("First item", results[2].Content);
    }

    [Fact]
    public async Task ShouldReturnConsistentResults_WhenGetAllAsyncCalledMultipleTimes()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));

        // Act
        var results1 = await memory.GetAllAsync();
        var results2 = await memory.GetAllAsync();

        // Assert
        Assert.Equal(results1.Count, results2.Count);
        for (int i = 0; i < results1.Count; i++)
        {
            Assert.Equal(results1[i].Content, results2[i].Content);
            Assert.Equal(results1[i].Timestamp, results2[i].Timestamp);
        }
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenGetAllAsyncConcurrentCalls()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));

        var tasks = new List<Task<IReadOnlyList<MemoryItem>>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () => await memory.GetAllAsync()));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result => Assert.Equal(2, result.Count));
        // All concurrent calls should return the same results
        var firstResult = results[0];
        Assert.All(results, result =>
        {
            Assert.Equal(firstResult.Count, result.Count);
            for (int i = 0; i < firstResult.Count; i++)
            {
                Assert.Equal(firstResult[i].Content, result[i].Content);
                Assert.Equal(firstResult[i].Timestamp, result[i].Timestamp);
            }
        });
    }

    #endregion

    #region ClearAsync Tests

    [Fact]
    public async Task ShouldRemoveAllItems_WhenClearAsyncWithItems()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));
        await memory.AddAsync(CreateMemoryItem("Item 3"));

        // Act
        await memory.ClearAsync();

        // Assert
        var results = await memory.GetAllAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldNotThrow_WhenClearAsyncEmptyMemory()
    {
        // Arrange
        using var memory = new LongTermMemory();

        // Act & Assert - Should not throw
        await memory.ClearAsync();

        var results = await memory.GetAllAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldAllowNewItems_WhenClearAsyncAfterClear()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Old item"));
        await memory.ClearAsync();

        // Act
        await memory.AddAsync(CreateMemoryItem("New item"));

        // Assert
        var results = await memory.GetAllAsync();
        Assert.Single(results);
        Assert.Equal("New item", results[0].Content);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenClearAsyncConcurrentCalls()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Item 1"));
        await memory.AddAsync(CreateMemoryItem("Item 2"));

        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () => await memory.ClearAsync(), TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var results = await memory.GetAllAsync();
        Assert.Empty(results);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ShouldWorkCorrectly_WhenIntegrationTestAddSearchClearCycle()
    {
        // Arrange
        using var memory = new LongTermMemory();

        // Act & Assert - Initial state
        var empty = await memory.GetAllAsync();
        Assert.Empty(empty);

        // Add items
        await memory.AddAsync(CreateMemoryItem("Test document 1"));
        await memory.AddAsync(CreateMemoryItem("Another document"));
        await memory.AddAsync(CreateMemoryItem("Test document 2"));

        var allItems = await memory.GetAllAsync();
        Assert.Equal(3, allItems.Count);

        // Search for items
        var searchResults = await memory.SearchAsync("test");
        Assert.Equal(2, searchResults.Count);

        var searchResults2 = await memory.SearchAsync("document");
        Assert.Equal(3, searchResults2.Count);

        // Clear
        await memory.ClearAsync();
        var afterClear = await memory.GetAllAsync();
        Assert.Empty(afterClear);

        var searchAfterClear = await memory.SearchAsync("test");
        Assert.Empty(searchAfterClear);
    }

    [Fact]
    public async Task ShouldWorkCorrectly_WhenIntegrationTestComplexSearchScenario()
    {
        // Arrange
        using var memory = new LongTermMemory();
        await memory.AddAsync(CreateMemoryItem("Machine learning algorithms", DateTime.UtcNow.AddMinutes(-10)));
        await memory.AddAsync(CreateMemoryItem("Deep learning neural networks", DateTime.UtcNow.AddMinutes(-5)));
        await memory.AddAsync(CreateMemoryItem("Natural language processing", DateTime.UtcNow.AddMinutes(-2)));
        await memory.AddAsync(CreateMemoryItem("Computer vision techniques", DateTime.UtcNow));

        // Act & Assert - Search for "learning"
        var learningResults = await memory.SearchAsync("learning");
        Assert.Equal(2, learningResults.Count);  // "Machine learning" and "Deep learning"
        Assert.Equal("Deep learning neural networks", learningResults[0].Content); // Most recent first

        // Search for multiple keywords
        var multiKeywordResults = await memory.SearchAsync("machine neural");
        Assert.Equal(2, multiKeywordResults.Count);

        // Search with no matches
        var noMatches = await memory.SearchAsync("quantum computing");
        Assert.Empty(noMatches);

        // Search with limit
        var limitedResults = await memory.SearchAsync("learning", maxResults: 2);
        Assert.Equal(2, limitedResults.Count);
    }

    [Fact]
    public async Task ShouldMaintainConsistency_WhenIntegrationTestConcurrentOperations()
    {
        // Arrange
        using var memory = new LongTermMemory();
        var tasks = new List<Task>();

        // Act - Concurrent adds
        for (int i = 0; i < 20; i++)
        {
            int itemIndex = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await memory.AddAsync(CreateMemoryItem($"Document {itemIndex} with test content"));
            }, TestContext.Current.CancellationToken));
        }

        // Concurrent searches while adding
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await memory.SearchAsync("test");
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var allItems = await memory.GetAllAsync();
        Assert.Equal(20, allItems.Count);

        var searchResults = await memory.SearchAsync("test", maxResults: 25); // Higher than 20 to ensure we get all results
        Assert.Equal(20, searchResults.Count);
    }

    #endregion

    #region Helper Methods

    private static MemoryItem CreateMemoryItem(string content, DateTime? timestamp = null, float[]? embedding = null)
    {
        return MemoryItem.Create(
            content: content,
            embedding: embedding ?? s_defaultEmbedding,
            importance: 1.0f);
    }

    #endregion
}
