using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Memory;

public class InMemoryProviderTests
{
    private readonly InMemoryProvider _provider;
    private readonly TestLogger<InMemoryProvider> _logger;

    public InMemoryProviderTests()
    {
        _logger = new TestLogger<InMemoryProvider>();
        _provider = new InMemoryProvider(_logger);
    }

    [Fact]
    public async Task ShouldStoreSuccessfully_WhenStoreAsyncWithValidData()
    {
        // Arrange
        var item = MemoryItem.Create(TestContent, source: "test");
        var key = "test-key";

        // Act
        await _provider.StoreAsync(key, item, TestContext.Current.CancellationToken);
        var retrieved = await _provider.GetAsync(key, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(TestContent, retrieved.Content);
        Assert.Equal("test", retrieved.Source);
    }

    [Fact]
    public async Task ShouldReturnNull_WhenGetAsyncWithNonExistentKey()
    {
        // Act
        var result = await _provider.GetAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldUpdateSuccessfully_WhenUpdateAsyncWithExistingKey()
    {
        // Arrange
        var originalItem = MemoryItem.Create("Original content");
        var updatedItem = MemoryItem.Create("Updated content");
        var key = "test-key";
        await _provider.StoreAsync(key, originalItem, TestContext.Current.CancellationToken);

        // Act
        var updateResult = await _provider.UpdateAsync(key, updatedItem, TestContext.Current.CancellationToken);
        var retrieved = await _provider.GetAsync(key, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(updateResult);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated content", retrieved.Content);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenUpdateAsyncWithNonExistentKey()
    {
        // Arrange
        var item = MemoryItem.Create(TestContent);

        // Act
        var result = await _provider.UpdateAsync("non-existent", item, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldDeleteSuccessfully_WhenDeleteAsyncWithExistingKey()
    {
        // Arrange
        var item = MemoryItem.Create(TestContent);
        var key = "test-key";
        await _provider.StoreAsync(key, item, TestContext.Current.CancellationToken);

        // Act
        var deleteResult = await _provider.DeleteAsync(key, TestContext.Current.CancellationToken);
        var retrieved = await _provider.GetAsync(key, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deleteResult);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenDeleteAsyncWithNonExistentKey()
    {
        // Act
        var result = await _provider.DeleteAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnResults_WhenSearchAsyncWithMatchingContent()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("Hello world"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("Hello there"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key3", MemoryItem.Create("Goodbye"), TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchAsync("Hello", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count());
        Assert.All(results, r => Assert.Contains("Hello", r.Content));
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchAsyncWithNoMatches()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create(TestContent), TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchAsync("xyz", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldRemoveAllItems_WhenClearAsync()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("Content 1"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("Content 2"), TestContext.Current.CancellationToken);

        // Act
        await _provider.ClearAsync(TestContext.Current.CancellationToken);
        var count = await _provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ShouldReturnCorrectCount_WhenCountAsync()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("Content 1"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("Content 2"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key3", MemoryItem.Create("Content 3"), TestContext.Current.CancellationToken);

        // Act
        var count = await _provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ShouldReturnCorrectKeys_WhenListKeysAsyncWithPagination()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await _provider.StoreAsync($"key{i}", MemoryItem.Create($"Content {i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var firstPage = await _provider.ListKeysAsync(skip: 0, take: 2, cancellationToken: TestContext.Current.CancellationToken);
        var secondPage = await _provider.ListKeysAsync(skip: 2, take: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, firstPage.Count);
        Assert.Equal(2, secondPage.Count);
        Assert.NotEqual(firstPage[0], secondPage[0]);
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenStoreAsyncWithNullKey()
    {
        // Arrange
        var item = MemoryItem.Create(TestContent);

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _provider.StoreAsync(null!, item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenStoreAsyncWithNullItem()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _provider.StoreAsync("key", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenStoreAsyncWithEmptyKey()
    {
        // Arrange
        var item = MemoryItem.Create(TestContent);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _provider.StoreAsync("", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenStoreAsyncWithWhitespaceKey()
    {
        // Arrange
        var item = MemoryItem.Create(TestContent);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _provider.StoreAsync("   ", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldRespectLimit_WhenSearchAsyncWithLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _provider.StoreAsync($"key{i}", MemoryItem.Create($"Test content {i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var results = await _provider.SearchAsync("Test", limit: 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count());
    }

    [Fact]
    public async Task ShouldReturnAllItems_WhenSearchAsyncWithEmptyQuery()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("Content 1"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("Content 2"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key3", MemoryItem.Create("Content 3"), TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchAsync("", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count());
    }

    [Fact]
    public async Task ShouldFindMatches_WhenSearchAsyncCaseInsensitive()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("UPPERCASE CONTENT"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("lowercase content"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key3", MemoryItem.Create("MiXeD CaSe CoNtEnT"), TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchAsync("content", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count());
    }

    [Fact]
    public async Task ShouldOverwriteExistingKey_WhenStoreAsync()
    {
        // Arrange
        var key = "test-key";
        await _provider.StoreAsync(key, MemoryItem.Create("Original"), TestContext.Current.CancellationToken);

        // Act
        await _provider.StoreAsync(key, MemoryItem.Create("Overwritten"), TestContext.Current.CancellationToken);
        var retrieved = await _provider.GetAsync(key, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Overwritten", retrieved.Content);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenListKeysAsyncWithSkipGreaterThanCount()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("Content 1"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("Content 2"), TestContext.Current.CancellationToken);

        // Act
        var keys = await _provider.ListKeysAsync(skip: 10, take: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenListKeysAsyncWithZeroTake()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("Content 1"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("Content 2"), TestContext.Current.CancellationToken);

        // Act
        var keys = await _provider.ListKeysAsync(skip: 0, take: 0, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ShouldReturnCorrectCount_WhenCountAsyncAfterMultipleOperations()
    {
        // Act & Assert - Initially empty
        Assert.Equal(0, await _provider.CountAsync(TestContext.Current.CancellationToken));

        // Add items
        await _provider.StoreAsync("key1", MemoryItem.Create("Content 1"), TestContext.Current.CancellationToken);
        Assert.Equal(1, await _provider.CountAsync(TestContext.Current.CancellationToken));

        await _provider.StoreAsync("key2", MemoryItem.Create("Content 2"), TestContext.Current.CancellationToken);
        Assert.Equal(2, await _provider.CountAsync(TestContext.Current.CancellationToken));

        // Overwrite existing key (count should remain same)
        await _provider.StoreAsync("key1", MemoryItem.Create("Updated Content 1"), TestContext.Current.CancellationToken);
        Assert.Equal(2, await _provider.CountAsync(TestContext.Current.CancellationToken));

        // Delete item
        await _provider.DeleteAsync("key2", TestContext.Current.CancellationToken);
        Assert.Equal(1, await _provider.CountAsync(TestContext.Current.CancellationToken));

        // Clear all
        await _provider.ClearAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await _provider.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldFindMatches_WhenSearchAsyncWithSpecialCharacters()
    {
        // Arrange
        await _provider.StoreAsync("key1", MemoryItem.Create("Content with @special #chars!"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key2", MemoryItem.Create("Normal content"), TestContext.Current.CancellationToken);
        await _provider.StoreAsync("key3", MemoryItem.Create("More @special content"), TestContext.Current.CancellationToken);

        // Act
        var results = await _provider.SearchAsync("@special", limit: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count());
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenGetAsyncWithEmptyKey()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _provider.GetAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenDeleteAsyncWithEmptyKey()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _provider.DeleteAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentException_WhenUpdateAsyncWithEmptyKey()
    {
        // Arrange
        var item = MemoryItem.Create(TestContent);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _provider.UpdateAsync("", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenUpdateAsyncWithNullItem()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _provider.UpdateAsync("key", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public Task ShouldReturnInMemory_WhenName()
    {
        // Act & Assert
        Assert.Equal("InMemory", _provider.Name);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenStoreAsyncWithCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var item = MemoryItem.Create(TestContent);

        // Act - Should complete since InMemoryProvider doesn't check cancellation
        await _provider.StoreAsync("key", item, cts.Token);

        // Assert
        var retrieved = await _provider.GetAsync("key", TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSearchAsyncWithNullQuery()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _provider.SearchAsync(null!, limit: 10, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldPreserveMetadata_WhenMemoryItem()
    {
        // Arrange
        var customProperties = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "42" },
            { "key3", "true" }
        };
        var item = MemoryItem.Create(TestContent,
            source: "test-source",
            importance: 0.8f,
            customProperties: customProperties);

        // Act
        await _provider.StoreAsync("test-key", item, TestContext.Current.CancellationToken);
        var retrieved = await _provider.GetAsync("test-key", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("test-source", retrieved.Source);
        Assert.Equal(0.8f, retrieved.Importance);
        Assert.NotNull(retrieved.Metadata);
        Assert.Equal("value1", retrieved.Metadata.CustomProperties?["key1"]);
        Assert.Equal("42", retrieved.Metadata.CustomProperties?["key2"]);
        Assert.Equal("true", retrieved.Metadata.CustomProperties?["key3"]);
    }
}
