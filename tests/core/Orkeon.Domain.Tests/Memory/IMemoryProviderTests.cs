using System.Collections.Concurrent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Domain.Tests.Memory;

public class IMemoryProviderTests
{
    private static readonly string[] ImportantTaskTags = ["important", "task"];
    private static readonly string[] RoutineTags = ["routine"];

    // Test implementation of IMemoryProvider
    private class TestMemoryProvider : IMemoryProvider
    {
        private readonly ConcurrentDictionary<string, MemoryItem> _storage = new();
        private readonly Func<string, int, IEnumerable<MemoryItem>>? _searchFunc;
        private int _storeCallCount;
        private int _getCallCount;
        private int _searchCallCount;
        private int _deleteCallCount;
        private int _clearCallCount;
        public int StoreCallCount => _storeCallCount;
        public int GetCallCount => _getCallCount;
        public int SearchCallCount => _searchCallCount;
        public int DeleteCallCount => _deleteCallCount;
        public int ClearCallCount => _clearCallCount;
        public bool ShouldThrowOnStore { get; set; }
        public bool ShouldThrowOnGet { get; set; }
        public bool ShouldThrowOnSearch { get; set; }

        public TestMemoryProvider(Func<string, int, IEnumerable<MemoryItem>>? searchFunc = null)
        {
            _searchFunc = searchFunc;
        }

        public async System.Threading.Tasks.Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeCallCount);

            if (ShouldThrowOnStore)
                throw new InvalidOperationException("Store operation failed");

            cancellationToken.ThrowIfCancellationRequested();

            await System.Threading.Tasks.Task.Delay(10, cancellationToken); // Simulate async work
            _storage[key] = item;
        }

        public async System.Threading.Tasks.Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getCallCount);

            if (ShouldThrowOnGet)
                throw new InvalidOperationException("Get operation failed");

            cancellationToken.ThrowIfCancellationRequested();

            await System.Threading.Tasks.Task.Delay(10, cancellationToken); // Simulate async work
            return _storage.TryGetValue(key, out var item) ? item : null;
        }

        public async System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _searchCallCount);

            if (ShouldThrowOnSearch)
                throw new InvalidOperationException("Search operation failed");

            cancellationToken.ThrowIfCancellationRequested();

            await System.Threading.Tasks.Task.Delay(10, cancellationToken); // Simulate async work

            if (_searchFunc != null)
                return _searchFunc(query, limit);

            // Default search: return items containing query in content
            return _storage.Values
                .Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList(); // Materialize the results immediately
        }

        public async System.Threading.Tasks.Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _deleteCallCount);
            cancellationToken.ThrowIfCancellationRequested();

            await System.Threading.Tasks.Task.Delay(10, cancellationToken); // Simulate async work
            return _storage.TryRemove(key, out _);
        }

        public async System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _clearCallCount);
            cancellationToken.ThrowIfCancellationRequested();

            await System.Threading.Tasks.Task.Delay(10, cancellationToken); // Simulate async work
            _storage.Clear();
        }

        // Helper methods for testing
        public int ItemCount => _storage.Count;
        public bool ContainsKey(string key) => _storage.ContainsKey(key);
        public MemoryItem? GetItemDirectly(string key) => _storage.TryGetValue(key, out var item) ? item : null;
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreSuccessfully_WhenStoringAsyncWithValidItem()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var item = MemoryItem.Create(TestContent, importance: 0.8f);
        var key = "test-key";

        // Act
        await provider.StoreAsync(key, item, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.StoreCallCount);
        Assert.True(provider.ContainsKey(key));
        Assert.Equal(item, provider.GetItemDirectly(key));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOverwrite_WhenStoringAsyncWithExistingKey()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var item1 = MemoryItem.Create("First content");
        var item2 = MemoryItem.Create("Second content");
        var key = "test-key";

        // Act
        await provider.StoreAsync(key, item1, TestContext.Current.CancellationToken);
        await provider.StoreAsync(key, item2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, provider.StoreCallCount);
        Assert.Equal(1, provider.ItemCount);
        Assert.Equal(item2, provider.GetItemDirectly(key));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenStoringAsyncWithCancellation()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var item = MemoryItem.Create(TestContent);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.StoreAsync("key", item, cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnItem_WhenGettingAsyncWithExistingKey()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var item = MemoryItem.Create(TestContent, importance: 0.9f);
        var key = "test-key";
        await provider.StoreAsync(key, item, TestContext.Current.CancellationToken);

        // Act
        var retrieved = await provider.GetAsync(key, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(item.Content, retrieved.Content);
        Assert.Equal(item.Importance, retrieved.Importance);
        Assert.Equal(1, provider.GetCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNull_WhenGettingAsyncWithNonExistingKey()
    {
        // Arrange
        var provider = new TestMemoryProvider();

        // Act
        var retrieved = await provider.GetAsync("non-existing-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(retrieved);
        Assert.Equal(1, provider.GetCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenGettingAsyncWithCancellation()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetAsync("key", cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnResults_WhenSearchingAsyncWithMatchingQuery()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("The quick brown fox"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Fox jumps over the lazy dog"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", MemoryItem.Create("The cat sleeps"), TestContext.Current.CancellationToken);

        // Act
        var results = await provider.SearchAsync("fox", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var resultList = results.ToList();
        Assert.Equal(2, resultList.Count);
        Assert.All(resultList, item => Assert.Contains("fox", item.Content, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, provider.SearchCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectLimit_WhenSearchingAsyncWithLimit()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        for (int i = 0; i < 20; i++)
        {
            await provider.StoreAsync($"key{i}", MemoryItem.Create($"Test content {i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var results = await provider.SearchAsync("Test", limit: 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyCollection_WhenSearchingAsyncWithNoMatches()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Apple"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Banana"), TestContext.Current.CancellationToken);

        // Act
        var results = await provider.SearchAsync("Orange", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseCustomLogic_WhenSearchingAsyncWithCustomSearchFunc()
    {
        // Arrange
        var customResults = new List<MemoryItem>
        {
            MemoryItem.Create("Custom result 1"),
            MemoryItem.Create("Custom result 2")
        };

        var provider = new TestMemoryProvider(
            searchFunc: (query, limit) => customResults.Take(limit));

        await provider.StoreAsync("key1", MemoryItem.Create("Regular item"), TestContext.Current.CancellationToken);

        // Act
        var results = await provider.SearchAsync("any query", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(customResults, results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnTrueAndRemoveItem_WhenDeletingAsyncWithExistingKey()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var key = "test-key";
        await provider.StoreAsync(key, MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        // Act
        var result = await provider.DeleteAsync(key, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.False(provider.ContainsKey(key));
        Assert.Equal(0, provider.ItemCount);
        Assert.Equal(1, provider.DeleteCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnFalse_WhenDeletingAsyncWithNonExistingKey()
    {
        // Arrange
        var provider = new TestMemoryProvider();

        // Act
        var result = await provider.DeleteAsync("non-existing-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        Assert.Equal(1, provider.DeleteCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenDeletingAsyncWithCancellation()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.DeleteAsync("key", cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRemoveAllItems_WhenClearingAsync()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Item 1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Item 2"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", MemoryItem.Create("Item 3"), TestContext.Current.CancellationToken);

        // Act
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, provider.ItemCount);
        Assert.Equal(1, provider.ClearCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenClearingAsyncOnEmptyProvider()
    {
        // Arrange
        var provider = new TestMemoryProvider();

        // Act & Assert (should not throw)
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, provider.ItemCount);
        Assert.Equal(1, provider.ClearCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenClearingAsyncWithCancellation()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.ClearAsync(cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleComplexScenario_WhenUsingIMemoryProvider()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var agentId = AgentId.From(Guid.NewGuid());

        // Act - Store multiple items with different properties
        var item1 = MemoryItem.Create(
            "First item with memory in content",
            importance: 0.9f,
            source: "agent",
            tags: ImportantTaskTags,
            createdBy: agentId);

        var item2 = MemoryItem.Create(
            "Second item also has memory word",
            importance: 0.5f,
            source: "system",
            tags: RoutineTags);

        var item3 = MemoryItem.Create(
            "Third MEMORY with special content",
            importance: 0.7f,
            source: "user");

        await provider.StoreAsync("memory1", item1, TestContext.Current.CancellationToken);
        await provider.StoreAsync("memory2", item2, TestContext.Current.CancellationToken);
        await provider.StoreAsync("memory3", item3, TestContext.Current.CancellationToken);

        // Verify items are stored before search
        Assert.Equal(3, provider.ItemCount);

        // Search for specific content
        var searchResults = await provider.SearchAsync("memory", limit: 2, TestContext.Current.CancellationToken);
        var searchResultsList = searchResults.ToList(); // Evaluate once

        // Get specific item
        var retrieved = await provider.GetAsync("memory1", TestContext.Current.CancellationToken);

        // Delete an item
        var deleted = await provider.DeleteAsync("memory2", TestContext.Current.CancellationToken);

        // Clear remaining
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, searchResultsList.Count);
        Assert.NotNull(retrieved);
        Assert.Equal(agentId, retrieved.Metadata.CreatedBy);
        Assert.True(deleted);
        Assert.Equal(0, provider.ItemCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowAppropriateExceptions_WhenUsingIMemoryProviderWithErrorHandling()
    {
        // Arrange
        var provider = new TestMemoryProvider
        {
            ShouldThrowOnStore = true,
            ShouldThrowOnGet = true,
            ShouldThrowOnSearch = true
        };

        var item = MemoryItem.Create(TestContent);

        // Act & Assert - Store should throw
        var storeException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.StoreAsync("key", item, TestContext.Current.CancellationToken));
        Assert.Contains("Store operation failed", storeException.Message);

        // Act & Assert - Get should throw
        provider.ShouldThrowOnStore = false;
        await provider.StoreAsync("key", item, TestContext.Current.CancellationToken);

        var getException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAsync("key", TestContext.Current.CancellationToken));
        Assert.Contains("Get operation failed", getException.Message);

        // Act & Assert - Search should throw
        var searchException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchAsync(ParamQuery, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Search operation failed", searchException.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingIMemoryProviderWithConcurrentOperations()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var tasks = new List<System.Threading.Tasks.Task>();

        // Act - Perform concurrent operations
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                var item = MemoryItem.Create($"Concurrent content {index}");
                await provider.StoreAsync($"concurrent-key-{index}", item);
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert - With thread-safe implementation, all operations should succeed
        Assert.Equal(10, provider.ItemCount);
        Assert.Equal(10, provider.StoreCallCount);

        // Verify all stored items are correct
        for (int i = 0; i < 10; i++)
        {
            var retrieved = await provider.GetAsync($"concurrent-key-{i}", TestContext.Current.CancellationToken);
            Assert.NotNull(retrieved);
            Assert.Contains($"Concurrent content {i}", retrieved.Content);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreAndRetrieve_WhenUsingIMemoryProviderWithEmbeddings()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        var embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var item = MemoryItem.Create(
            "Content with embedding",
            embedding: embedding,
            importance: 0.85f);

        // Act
        await provider.StoreAsync("embedded-key", item, TestContext.Current.CancellationToken);
        var retrieved = await provider.GetAsync("embedded-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Embedding);
        Assert.Equal(embedding.Length, retrieved.Embedding.Count);
        Assert.Equal(embedding, retrieved.Embedding);
    }

    #region Default Interface Method Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldSetEmbeddingAndStore_WhenUsingStoreWithEmbeddingAsyncDefaultMethod()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        IMemoryProvider iProvider = provider; // Use interface reference for default methods
        var item = MemoryItem.Create("Content for embedding", importance: 0.7f);
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        // Act
        await iProvider.StoreWithEmbeddingAsync("emb-key", item, embedding, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.StoreCallCount);
        var retrieved = await provider.GetAsync("emb-key", TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Embedding);
        Assert.Equal(embedding, retrieved.Embedding);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyResults_WhenUsingSearchSimilarAsyncDefaultMethod()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        IMemoryProvider iProvider = provider;
        var queryEmbedding = new float[] { 0.5f, 0.5f, 0.5f };

        // Store some items first
        await provider.StoreAsync("key1", MemoryItem.Create("Item 1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Item 2"), TestContext.Current.CancellationToken);

        // Act
        var results = await iProvider.SearchSimilarAsync(queryEmbedding, topK: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyResults_WhenUsingSearchSimilarAsyncWithFilter()
    {
        // Arrange
        IMemoryProvider provider = new TestMemoryProvider();
        var queryEmbedding = new float[] { 0.1f, 0.2f };
        var filter = new Dictionary<string, object> { { "type", "important" } };

        // Act
        var results = await provider.SearchSimilarAsync(
            queryEmbedding, topK: 10, minScore: 0.5f, filter: filter, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellation_WhenUsingStoreWithEmbeddingAsyncWithCancelledToken()
    {
        // Arrange
        var provider = new TestMemoryProvider();
        IMemoryProvider iProvider = provider;
        var item = MemoryItem.Create(TestContent);
        var embedding = new float[] { 0.1f, 0.2f };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => iProvider.StoreWithEmbeddingAsync("key", item, embedding, cts.Token));
    }

    #endregion
}
