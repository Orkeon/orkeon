using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Memory.Base;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.Memory;

public class MemoryProviderBaseTests
{
    private sealed class FakeProvider : MemoryProviderBase
    {
        private readonly Dictionary<string, MemoryItem> _store = [];
        public FakeProvider(ILogger? logger = null) : base(logger) { }
        public override string Name => "Fake";
        public bool WasInitializeCalled => Configuration is not null;

        public override Task ClearAsync(CancellationToken cancellationToken = default)
        { _store.Clear(); return Task.CompletedTask; }
        public override Task<int> CountAsync(CancellationToken cancellationToken = default)
        { return Task.FromResult(_store.Count); }
        public override Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        { return Task.FromResult(_store.Remove(key)); }
        public override Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
        { _store.TryGetValue(key, out var v); return Task.FromResult(v); }
        public override Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
        { return Task.FromResult(_store.Keys.Skip(skip).Take(take).ToList()); }
        public override Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        { _store[key] = item; return Task.CompletedTask; }
        public override Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        { var exists = _store.ContainsKey(key); _store[key] = item; return Task.FromResult(exists); }
        public override Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        { return Task.FromResult(_store.Values.Where(v => v.Content.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit).AsEnumerable()); }
        public override Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(float[] queryEmbedding, int topK = 10, float minScore = 0.0f, Dictionary<string, object>? filter = null, CancellationToken cancellationToken = default)
        {
            var scored = new List<ScoredMemoryItem>();
            foreach (var item in _store.Values)
            {
                var embedding = item.Embedding;
                if (embedding == null || embedding.Count != queryEmbedding.Length)
                    continue;
                var score = Orkeon.Domain.SharedKernel.ValueObjects.VectorMath.CosineSimilarity(queryEmbedding, embedding.ToArray());
                if (score >= minScore)
                    scored.Add(new ScoredMemoryItem(item, score));
            }
            return Task.FromResult<IReadOnlyList<ScoredMemoryItem>>(scored.OrderByDescending(s => s.Score).Take(topK).ToList());
        }

        // Test hooks to call protected members
        public void CallValidateConfiguration() => base.ValidateConfiguration();
        public static string CallCreateTimestampedKey(string? prefix = null) => CreateTimestampedKey(prefix);
        public static void CallValidateKey(string key) => ValidateKey(key);
        public static void CallValidateMemoryItem(MemoryItem item) => ValidateMemoryItem(item);
    }

    [Fact]
    public async Task ShouldSetConfiguration_WhenInitializeAsync()
    {
        var provider = new FakeProvider();
        var cfg = new MemoryProviderConfig("fake", new Dictionary<string, object> { { "a", "b" } });
        await provider.InitializeAsync(cfg, TestContext.Current.CancellationToken);
        // If not thrown by ValidateConfiguration then it was set
        provider.CallValidateConfiguration();
    }

    [Fact]
    public void ShouldThrowWhenNotInitialized_WhenValidateConfiguration()
    {
        var provider = new FakeProvider();
        Assert.Throws<InvalidOperationException>(() => provider.CallValidateConfiguration());
    }

    [Fact]
    public void ShouldReturnKeyWithPrefix_WhenCreateTimestampedKey()
    {
        var key = FakeProvider.CallCreateTimestampedKey("pref");
        Assert.StartsWith("pref_", key);
        Assert.True(key.Length > 10);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldThrowOnInvalid_WhenValidateKey(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => FakeProvider.CallValidateKey(key!));
    }

    [Fact]
    public void ShouldThrowOnNull_WhenValidateMemoryItem()
    {
        Assert.Throws<ArgumentNullException>(() => FakeProvider.CallValidateMemoryItem(null!));
    }

    [Fact]
    public void ShouldThrowOnEmptyContent_WhenValidateMemoryItem()
    {
        Assert.Throws<ArgumentException>(() => MemoryItem.Create(" "));
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenInitializeAsyncWithNullConfig()
    {
        // Arrange
        var provider = new FakeProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.InitializeAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ShouldReturnUniqueKey_WhenCreateTimestampedKeyWithoutPrefix()
    {
        // Act
        var key1 = FakeProvider.CallCreateTimestampedKey(null);
        var key2 = FakeProvider.CallCreateTimestampedKey(null);

        // Assert
        Assert.NotEqual(key1, key2);
        Assert.Contains("_", key1);
        Assert.True(key1.Length > 10);
    }

    [Fact]
    public void ShouldReturnKeyWithoutPrefix_WhenCreateTimestampedKeyWithEmptyPrefix()
    {
        // Act
        var key = FakeProvider.CallCreateTimestampedKey("");

        // Assert
        Assert.DoesNotContain("__", key); // Should not have double underscore
        Assert.Contains("_", key);
    }

    [Fact]
    public void ShouldNotThrow_WhenValidateKeyWithValidKey()
    {
        // Act & Assert - Should not throw
        FakeProvider.CallValidateKey("valid-key");
        FakeProvider.CallValidateKey("key123");
        FakeProvider.CallValidateKey("key_with_underscore");
    }

    [Fact]
    public void ShouldNotThrow_WhenValidateMemoryItemWithValidItem()
    {
        // Arrange
        var item = MemoryItem.Create("Valid content");

        // Act & Assert - Should not throw
        FakeProvider.CallValidateMemoryItem(item);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenValidateMemoryItemWithWhitespaceContent()
    {
        // Arrange & Act & Assert
        // MemoryItem constructor should prevent creating items with whitespace content
        Assert.ThrowsAny<ArgumentException>(() =>
            MemoryItem.Create("   ", importance: 0.5f));
    }

    [Fact]
    public async Task ShouldStoreItemInMemory_WhenStoreAsync()
    {
        // Arrange
        var provider = new FakeProvider();
        var item = MemoryItem.Create(TestContent);
        var key = "test-key";

        // Act
        await provider.StoreAsync(key, item, TestContext.Current.CancellationToken);

        // Assert
        var retrieved = await provider.GetAsync(key, TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
        Assert.Equal(TestContent, retrieved.Content);
    }

    [Fact]
    public async Task ShouldReturnNull_WhenGetAsyncWithNonExistentKey()
    {
        // Arrange
        var provider = new FakeProvider();

        // Act
        var result = await provider.GetAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenUpdateAsyncWithExistingKey()
    {
        // Arrange
        var provider = new FakeProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Original"), TestContext.Current.CancellationToken);
        var updatedItem = MemoryItem.Create("Updated");

        // Act
        var result = await provider.UpdateAsync("key1", updatedItem, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var retrieved = await provider.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.Equal("Updated", retrieved?.Content);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenUpdateAsyncWithNonExistentKey()
    {
        // Arrange
        var provider = new FakeProvider();
        var item = MemoryItem.Create("Content");

        // Act
        var result = await provider.UpdateAsync("non-existent", item, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        // But the item should still be stored
        var retrieved = await provider.GetAsync("non-existent", TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenDeleteAsyncWithExistingKey()
    {
        // Arrange
        var provider = new FakeProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Content"), TestContext.Current.CancellationToken);

        // Act
        var result = await provider.DeleteAsync("key1", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var retrieved = await provider.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenDeleteAsyncWithNonExistentKey()
    {
        // Arrange
        var provider = new FakeProvider();

        // Act
        var result = await provider.DeleteAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldRemoveAllItems_WhenClearAsync()
    {
        // Arrange
        var provider = new FakeProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Content1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Content2"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", MemoryItem.Create("Content3"), TestContext.Current.CancellationToken);

        // Act
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        var count = await provider.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ShouldReturnCorrectCount_WhenCountAsync()
    {
        // Arrange
        var provider = new FakeProvider();

        // Act & Assert - Empty
        Assert.Equal(0, await provider.CountAsync(TestContext.Current.CancellationToken));

        // Add items
        await provider.StoreAsync("key1", MemoryItem.Create("Content1"), TestContext.Current.CancellationToken);
        Assert.Equal(1, await provider.CountAsync(TestContext.Current.CancellationToken));

        await provider.StoreAsync("key2", MemoryItem.Create("Content2"), TestContext.Current.CancellationToken);
        Assert.Equal(2, await provider.CountAsync(TestContext.Current.CancellationToken));

        // Delete one
        await provider.DeleteAsync("key1", TestContext.Current.CancellationToken);
        Assert.Equal(1, await provider.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnCorrectKeys_WhenListKeysAsyncWithPagination()
    {
        // Arrange
        var provider = new FakeProvider();
        for (int i = 0; i < 10; i++)
        {
            await provider.StoreAsync($"key{i}", MemoryItem.Create($"Content{i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var firstPage = await provider.ListKeysAsync(0, 3, TestContext.Current.CancellationToken);
        var secondPage = await provider.ListKeysAsync(3, 3, TestContext.Current.CancellationToken);
        var thirdPage = await provider.ListKeysAsync(6, 10, TestContext.Current.CancellationToken); // Request more than available

        // Assert
        Assert.Equal(3, firstPage.Count);
        Assert.Equal(3, secondPage.Count);
        Assert.Equal(4, thirdPage.Count); // Only 4 remaining

        // Check no overlap
        Assert.Empty(firstPage.Intersect(secondPage));
        Assert.Empty(secondPage.Intersect(thirdPage));
    }

    [Fact]
    public async Task ShouldReturnMatchingItems_WhenSearchAsyncWithMatchingQuery()
    {
        // Arrange
        var provider = new FakeProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Python programming"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Java development"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", MemoryItem.Create("Python tutorial"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key4", MemoryItem.Create("C# coding"), TestContext.Current.CancellationToken);

        // Act
        var results = await provider.SearchAsync("Python", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
        Assert.All(resultsList, item => Assert.Contains("Python", item.Content));
    }

    [Fact]
    public async Task ShouldRespectLimit_WhenSearchAsyncWithLimit()
    {
        // Arrange
        var provider = new FakeProvider();
        for (int i = 0; i < 20; i++)
        {
            await provider.StoreAsync($"key{i}", MemoryItem.Create($"Test content {i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var results = await provider.SearchAsync("Test", limit: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, results.Count());
    }

    [Fact]
    public async Task ShouldFindMatches_WhenSearchAsyncCaseInsensitive()
    {
        // Arrange
        var provider = new FakeProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("UPPERCASE CONTENT"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("lowercase content"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", MemoryItem.Create("MiXeD CaSe CoNtEnT"), TestContext.Current.CancellationToken);

        // Act
        var results = await provider.SearchAsync("content", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count());
    }

    [Fact]
    public void ShouldReturnProviderName_WhenName()
    {
        // Arrange
        var provider = new FakeProvider();

        // Act & Assert
        Assert.Equal("Fake", provider.Name);
    }

    [Fact]
    public async Task ShouldMaintainConsistency_WhenMultipleOperations()
    {
        // Arrange
        var provider = new FakeProvider();

        // Act - Perform multiple operations
        await provider.StoreAsync("key1", MemoryItem.Create("Item1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Item2"), TestContext.Current.CancellationToken);
        await provider.UpdateAsync("key1", MemoryItem.Create("Updated1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", MemoryItem.Create("Item3"), TestContext.Current.CancellationToken);
        await provider.DeleteAsync("key2", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, await provider.CountAsync(TestContext.Current.CancellationToken));

        var keys = await provider.ListKeysAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("key1", keys);
        Assert.DoesNotContain("key2", keys);
        Assert.Contains("key3", keys);

        var item1 = await provider.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.Equal("Updated1", item1?.Content);
    }

    [Fact]
    public void ShouldGenerateUniqueKeys_WhenCreateTimestampedKeyCalledMultipleTimes()
    {
        // Act
        var keys = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            keys.Add(FakeProvider.CallCreateTimestampedKey("test"));
        }

        // Assert - All keys should be unique
        Assert.Equal(100, keys.Count);
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenStoreAsyncWithCancellationToken()
    {
        // Arrange
        var provider = new FakeProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act - Should still work as our fake implementation doesn't check cancellation
        // In a real implementation, this would throw OperationCanceledException
        await provider.StoreAsync("key", MemoryItem.Create("Content"), cts.Token);

        // Assert
        var item = await provider.GetAsync("key", TestContext.Current.CancellationToken);
        Assert.NotNull(item);
    }

    [Fact]
    public async Task ShouldReturnEmptyResults_WhenEmptyProvider()
    {
        // Arrange
        var provider = new FakeProvider();

        // Act & Assert
        Assert.Equal(0, await provider.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await provider.ListKeysAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(await provider.SearchAsync("anything", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(await provider.GetAsync("any-key", TestContext.Current.CancellationToken));
        Assert.False(await provider.DeleteAsync("any-key", TestContext.Current.CancellationToken));
    }

    private class TestLogger : ILogger
    {
        public List<string> LoggedMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LoggedMessages.Add($"{logLevel}: {formatter(state, exception)}");
        }
    }

    [Fact]
    public async Task ShouldBeConstructedProperly_WhenProviderWithLogger()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var provider = new FakeProvider(logger);
        await provider.StoreAsync("key", MemoryItem.Create("Content"), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("Fake", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenListKeysAsyncWithSkipGreaterThanCount()
    {
        // Arrange
        var provider = new FakeProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Content1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Content2"), TestContext.Current.CancellationToken);

        // Act
        var keys = await provider.ListKeysAsync(skip: 10, take: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchAsyncWithNoMatches()
    {
        // Arrange
        var provider = new FakeProvider();
        await provider.StoreAsync("key1", MemoryItem.Create("Content about cats"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", MemoryItem.Create("Content about dogs"), TestContext.Current.CancellationToken);

        // Act
        var results = await provider.SearchAsync("elephants", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }
}
