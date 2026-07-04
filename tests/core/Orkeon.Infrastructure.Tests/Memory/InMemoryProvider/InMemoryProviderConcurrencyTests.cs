using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// Concurrency tests for <see cref="InMemoryProvider"/> to verify thread-safety
/// of the underlying <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
/// operations, particularly the atomic <c>AddOrUpdate</c> in <c>UpdateAsync</c>.
/// </summary>
public class InMemoryProviderConcurrencyTests
{
    private readonly InMemoryProvider _provider;

    public InMemoryProviderConcurrencyTests()
    {
        var logger = new TestLogger<InMemoryProvider>();
        _provider = new InMemoryProvider(logger);
    }

    [Fact]
    public async Task UpdateAsync_ShouldHandleConcurrentUpdates()
    {
        // Arrange
        var key = "concurrent-update-key";
        var initialItem = MemoryItem.Create("initial", source: "test");
        await _provider.StoreAsync(key, initialItem, TestContext.Current.CancellationToken);

        // Act — 10 threads perform concurrent updates on the same key
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _provider.UpdateAsync(key,
                MemoryItem.Create($"update-{i}", source: "test")))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert — all updates should succeed since the key exists
        Assert.True(results.All(r => r), "All concurrent updates should succeed");

        var finalItem = await _provider.GetAsync(key, TestContext.Current.CancellationToken);
        Assert.NotNull(finalItem);
        Assert.StartsWith("update-", finalItem.Content);
    }

    [Fact]
    public async Task ConcurrentStoreAndGet_ShouldBeThreadSafe()
    {
        // Arrange & Act — mix store, get, update concurrently
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(_provider.StoreAsync($"key-{index}", MemoryItem.Create($"item-{index}", source: "test"), TestContext.Current.CancellationToken));
        }

        // Wait for all stores to complete before mixing in gets/updates
        await Task.WhenAll(tasks);
        tasks.Clear();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(_provider.GetAsync($"key-{index}", TestContext.Current.CancellationToken));
            tasks.Add(_provider.UpdateAsync($"key-{index}", MemoryItem.Create($"updated-{index}", source: "test"), TestContext.Current.CancellationToken));
        }

        // Assert — no exceptions should be thrown
        await Task.WhenAll(tasks);

        // Verify state is consistent
        var count = await _provider.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(100, count);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnFalse_WhenKeyDoesNotExist_UnderConcurrency()
    {
        // Act — multiple threads try to update a non-existent key concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _provider.UpdateAsync("non-existent-key",
                MemoryItem.Create($"value-{i}", source: "test")))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert — all should return false since the key never existed
        Assert.True(results.All(r => !r), "All updates to non-existent key should return false");

        // The dictionary should remain empty (no phantom entries from AddOrUpdate)
        var count = await _provider.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ConcurrentStoreDeleteAndUpdate_ShouldMaintainConsistency()
    {
        // Arrange — pre-populate some keys
        for (int i = 0; i < 50; i++)
        {
            await _provider.StoreAsync($"key-{i}", MemoryItem.Create($"content-{i}", source: "test"), TestContext.Current.CancellationToken);
        }

        // Act — concurrent stores, deletes, and updates
        var tasks = new List<Task<bool>>();

        for (int i = 0; i < 50; i++)
        {
            var index = i;
            // Update existing keys
            tasks.Add(_provider.UpdateAsync($"key-{index}",
                MemoryItem.Create($"updated-{index}", source: "test"), TestContext.Current.CancellationToken));
            // Delete some keys
            tasks.Add(_provider.DeleteAsync($"key-{index}", TestContext.Current.CancellationToken));
        }

        // Assert — no exceptions should be thrown
        await Task.WhenAll(tasks);

        // Count should be consistent (some deleted, some not)
        var count = await _provider.CountAsync(TestContext.Current.CancellationToken);
        Assert.True(count >= 0 && count <= 50,
            $"Count should be between 0 and 50, but was {count}");
    }

    [Fact]
    public async Task ConcurrentUpdates_ShouldNotLeavePhantomEntries()
    {
        // Arrange — store a single key
        var key = "phantom-test";
        await _provider.StoreAsync(key, MemoryItem.Create("original", source: "test"), TestContext.Current.CancellationToken);

        // Act — concurrent updates and deletes on the same key
        var updateTasks = Enumerable.Range(0, 20)
            .Select(i => _provider.UpdateAsync(key,
                MemoryItem.Create($"update-{i}", source: "test")));

        await Task.WhenAll(updateTasks);

        // Delete the key
        await _provider.DeleteAsync(key, TestContext.Current.CancellationToken);

        // Try more updates on the now-deleted key
        var postDeleteUpdates = Enumerable.Range(0, 10)
            .Select(i => _provider.UpdateAsync(key,
                MemoryItem.Create($"post-delete-{i}", source: "test")))
            .ToList();

        var results = await Task.WhenAll(postDeleteUpdates);

        // Assert — updates after deletion should all fail
        Assert.True(results.All(r => !r),
            "Updates after deletion should return false");

        // No phantom entries should remain
        var item = await _provider.GetAsync(key, TestContext.Current.CancellationToken);
        Assert.Null(item);
    }

    [Fact]
    public async Task HighContention_StoreAndSearch_ShouldBeThreadSafe()
    {
        // Act — many concurrent stores followed by concurrent searches
        var storeTasks = Enumerable.Range(0, 200)
            .Select(i => _provider.StoreAsync($"search-key-{i}",
                MemoryItem.Create($"searchable content number {i}", source: "test")))
            .ToList();

        await Task.WhenAll(storeTasks);

        var searchTasks = Enumerable.Range(0, 50)
            .Select(_ => _provider.SearchAsync("searchable", limit: 100))
            .ToList();

        var searchResults = await Task.WhenAll(searchTasks);

        // Assert — all searches should return consistent results
        Assert.All(searchResults, results =>
        {
            var count = results.Count();
            Assert.True(count > 0, "Search should find results");
            Assert.True(count <= 200, "Search should not exceed stored count");
        });
    }
}
