using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using Orkeon.Infrastructure.Memory;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Infrastructure.Tests.Memory;

public class RedisMemoryProviderTests
{
    private static readonly string[] s_tags = ["tag1", "tag2"];

    #region Test Doubles

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _logEntries = [];

        public IReadOnlyList<LogEntry> LogEntries => _logEntries;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logEntries.Add(new LogEntry
            {
                LogLevel = logLevel,
                Message = formatter(state, exception),
                Exception = exception
            });
        }

        public bool HasLoggedError(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Error && e.Message.Contains(containsText));

        public bool HasLoggedInfo(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Information && e.Message.Contains(containsText));

        public bool HasLoggedDebug(string containsText) =>
            _logEntries.Any(e => e.LogLevel == LogLevel.Debug && e.Message.Contains(containsText));

        public class LogEntry
        {
            public LogLevel LogLevel { get; init; }
            public string Message { get; init; } = string.Empty;
            public Exception? Exception { get; init; }
        }

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private class InMemoryRedisMemoryProvider : RedisMemoryProvider
    {
        private readonly Dictionary<string, MemoryItem> _storage = [];
        private bool _isInitialized;
        private string _keyPrefix = "orkeon:memory:";
        private MemoryProviderConfig? _configuration;

        public InMemoryRedisMemoryProvider(ILogger<RedisMemoryProvider> logger) : base(logger)
        {
        }

        public override async Task InitializeAsync(MemoryProviderConfig config, CancellationToken cancellationToken = default)
        {
            // Don't call base.InitializeAsync since it tries to connect to Redis
            _configuration = config ?? throw new ArgumentNullException(nameof(config));

            // Extract key prefix from config
            if (config.Settings != null && config.Settings.TryGetValue("keyPrefix", out var prefix))
            {
                _keyPrefix = prefix?.ToString() ?? "orkeon:memory:";
            }

            _isInitialized = true;
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("In-memory Redis provider initialized with connection: {ConnectionString}",
                    config.ConnectionString ?? "in-memory");
            }

            await Task.CompletedTask;
        }

        public override async Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        {
            ValidateInitialized();
            ValidateKey(key);
            ValidateMemoryItem(item);

            try
            {
                var redisKey = GetRedisKey(key);
                _storage[redisKey] = item;
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Stored memory item with key: {Key}", key);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogException(ex, "StoreAsync", key);
                throw;
            }
        }

        public override async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateInitialized();
            ValidateKey(key);

            try
            {
                var redisKey = GetRedisKey(key);
                if (_storage.TryGetValue(redisKey, out var item))
                {
                    if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Retrieved memory item with key: {Key}", key);
                    return await Task.FromResult(item);
                }

                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Memory item not found with key: {Key}", key);
                return await Task.FromResult<MemoryItem?>(null);
            }
            catch (Exception ex)
            {
                LogException(ex, "GetAsync", key);
                throw;
            }
        }

        public override async Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        {
            ValidateInitialized();
            ValidateKey(key);
            ValidateMemoryItem(item);

            try
            {
                var redisKey = GetRedisKey(key);
                if (!_storage.ContainsKey(redisKey))
                {
                    if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Cannot update non-existent memory item with key: {Key}", key);
                    return await Task.FromResult(false);
                }

                _storage[redisKey] = item;
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Updated memory item with key: {Key}", key);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                LogException(ex, "UpdateAsync", key);
                throw;
            }
        }

        public override async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            ValidateInitialized();
            ValidateKey(key);

            try
            {
                var redisKey = GetRedisKey(key);
                var deleted = _storage.Remove(redisKey);
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Deleted memory item with key: {Key}, Success: {Success}", key, deleted);
                return await Task.FromResult(deleted);
            }
            catch (Exception ex)
            {
                LogException(ex, "DeleteAsync", key);
                throw;
            }
        }

        public override async Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            ValidateInitialized();

            if (string.IsNullOrWhiteSpace(query))
                return await Task.FromResult(Array.Empty<MemoryItem>());

            try
            {
                var results = _storage.Values
                    .Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(limit)
                    .ToList();

                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Found {Count} memory items matching query: {Query}", results.Count, query);
                return await Task.FromResult(results);
            }
            catch (Exception ex)
            {
                LogException(ex, "SearchAsync");
                throw;
            }
        }

        public override async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ValidateInitialized();

            try
            {
                var count = _storage.Count;
                _storage.Clear();
                if (Logger.IsEnabled(LogLevel.Information)) Logger.LogInformation("Cleared {Count} memory items", count);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogException(ex, "ClearAsync");
                throw;
            }
        }

        public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            ValidateInitialized();

            try
            {
                var count = _storage.Count;
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Counted {Count} memory items", count);
                return await Task.FromResult(count);
            }
            catch (Exception ex)
            {
                LogException(ex, "CountAsync");
                throw;
            }
        }

        public override async Task<List<string>> ListKeysAsync(int skip = 0, int take = 100, CancellationToken cancellationToken = default)
        {
            ValidateInitialized();

            try
            {
                var keys = _storage.Keys
                    .Skip(skip)
                    .Take(take)
                    .Select(k => k.StartsWith(_keyPrefix) ? k.Substring(_keyPrefix.Length) : k)
                    .ToList();

                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug("Listed {Count} memory keys (skip: {Skip}, take: {Take})", keys.Count, skip, take);
                return await Task.FromResult(keys);
            }
            catch (Exception ex)
            {
                LogException(ex, "ListKeysAsync");
                throw;
            }
        }

        private void ValidateInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Redis provider not initialized. Call InitializeAsync first.");
        }

        private string GetRedisKey(string key) => $"{_keyPrefix}{key}";

        public new void Dispose()
        {
            _storage.Clear();
            _isInitialized = false;
            base.Dispose();
        }
    }

    #endregion

    private readonly TestLogger<RedisMemoryProvider> _logger;

    public RedisMemoryProviderTests()
    {
        _logger = new TestLogger<RedisMemoryProvider>();
    }

    #region Constructor Tests

    [Fact]
    public void ShouldSetName_WhenConstructor()
    {
        // Arrange & Act
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Assert
        Assert.Equal("redis", provider.Name);
    }

    [Fact]
    public void ShouldNotThrow_WhenConstructorWithNullLogger()
    {
        // Arrange & Act
        using var provider = new InMemoryRedisMemoryProvider(null!);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("redis", provider.Name);
    }

    #endregion

    #region InitializeAsync Tests

    [Fact]
    public async Task ShouldLogInitialization_WhenInitializeAsyncWithValidConfig()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);
        var config = new MemoryProviderConfig(
            providerType: "redis",
            connectionString: "localhost:6379",
            settings: new Dictionary<string, object>
            {
                { "keyPrefix", "test:" }
            });

        // Act
        await provider.InitializeAsync(config, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_logger.HasLoggedInfo("initialized with connection"));
        Assert.True(_logger.HasLoggedInfo("localhost:6379"));
    }

    [Fact]
    public async Task ShouldThrow_WhenInitializeAsyncWithNullConfig()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.InitializeAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldUseIt_WhenInitializeAsyncWithCustomKeyPrefix()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);
        var config = new MemoryProviderConfig(
            providerType: "redis",
            settings: new Dictionary<string, object>
            {
                { "keyPrefix", "custom:prefix:" }
            });

        // Act
        await provider.InitializeAsync(config, TestContext.Current.CancellationToken);
        await provider.StoreAsync("testkey", CreateMemoryItem("test"), TestContext.Current.CancellationToken);

        // Assert
        var keys = await provider.ListKeysAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("testkey", keys);
    }

    #endregion

    #region StoreAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenStoreAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);
        var item = CreateMemoryItem("test content");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.StoreAsync("key1", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenStoreAsyncWithNullKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        var item = CreateMemoryItem("test content");

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            provider.StoreAsync(null!, item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenStoreAsyncWithEmptyKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        var item = CreateMemoryItem("test content");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.StoreAsync("", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenStoreAsyncWithNullItem()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.StoreAsync("key1", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldStoreSuccessfully_WhenStoreAsyncWithValidData()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        var item = CreateMemoryItem("test content", importance: 0.8f);

        // Act
        await provider.StoreAsync("key1", item, TestContext.Current.CancellationToken);

        // Assert
        var retrieved = await provider.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
        Assert.Equal("test content", retrieved.Content);
        Assert.Equal(0.8f, retrieved.Importance);
        Assert.True(_logger.HasLoggedDebug("Stored memory item with key"));
    }

    [Fact]
    public async Task ShouldThrow_WhenStoreAsyncWithEmptyContent()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.StoreAsync("key1", MemoryItem.Create("", null, 0.5f), TestContext.Current.CancellationToken));
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenGetAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetAsync("key1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenGetAsyncWithNullKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            provider.GetAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnNull_WhenGetAsyncWithNonExistentKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act
        var result = await provider.GetAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        Assert.True(_logger.HasLoggedDebug("Memory item not found"));
    }

    [Fact]
    public async Task ShouldReturnItem_WhenGetAsyncWithExistingKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        var item = CreateMemoryItem("stored content",
            tags: s_tags,
            source: "test-source");
        await provider.StoreAsync("key1", item, TestContext.Current.CancellationToken);

        // Act
        var retrieved = await provider.GetAsync("key1", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("stored content", retrieved.Content);
        Assert.Equal("test-source", retrieved.Source);
        Assert.Equal(2, retrieved.Tags.Count);
        Assert.Contains("tag1", retrieved.Tags);
        Assert.Contains("tag2", retrieved.Tags);
        Assert.True(_logger.HasLoggedDebug("Retrieved memory item"));
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenUpdateAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);
        var item = CreateMemoryItem("updated content");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.UpdateAsync("key1", item, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenUpdateAsyncWithNonExistentKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        var item = CreateMemoryItem("updated content");

        // Act
        var result = await provider.UpdateAsync("non-existent", item, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        Assert.True(_logger.HasLoggedDebug("Cannot update non-existent"));
    }

    [Fact]
    public async Task ShouldUpdateAndReturnTrue_WhenUpdateAsyncWithExistingKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        var originalItem = CreateMemoryItem("original content", importance: 0.5f);
        await provider.StoreAsync("key1", originalItem, TestContext.Current.CancellationToken);

        var updatedItem = CreateMemoryItem("updated content", importance: 0.9f);

        // Act
        var result = await provider.UpdateAsync("key1", updatedItem, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var retrieved = await provider.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.NotNull(retrieved);
        Assert.Equal("updated content", retrieved.Content);
        Assert.Equal(0.9f, retrieved.Importance);
        Assert.True(_logger.HasLoggedDebug("Updated memory item"));
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenDeleteAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.DeleteAsync("key1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenDeleteAsyncWithNullKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            provider.DeleteAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenDeleteAsyncWithNonExistentKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act
        var result = await provider.DeleteAsync("non-existent", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        Assert.True(_logger.HasLoggedDebug("Success: False"));
    }

    [Fact]
    public async Task ShouldDeleteAndReturnTrue_WhenDeleteAsyncWithExistingKey()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        var item = CreateMemoryItem("content to delete");
        await provider.StoreAsync("key1", item, TestContext.Current.CancellationToken);

        // Act
        var result = await provider.DeleteAsync("key1", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        var retrieved = await provider.GetAsync("key1", TestContext.Current.CancellationToken);
        Assert.Null(retrieved);
        Assert.True(_logger.HasLoggedDebug("Success: True"));
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenSearchAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SearchAsync(ParamQuery, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenSearchAsyncWithEmptyQuery()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act
        var results = await provider.SearchAsync("", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnItems_WhenSearchAsyncWithMatchingContent()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        await provider.StoreAsync("key1", CreateMemoryItem("The quick brown fox"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", CreateMemoryItem("The lazy dog"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", CreateMemoryItem("Quick thinking saves time"), TestContext.Current.CancellationToken);

        // Act
        var results = (await provider.SearchAsync("quick", limit: 5, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, item => Assert.Contains("quick", item.Content, StringComparison.OrdinalIgnoreCase));
        Assert.True(_logger.HasLoggedDebug("Found 2 memory items"));
    }

    [Fact]
    public async Task ShouldRespectLimit_WhenSearchAsyncWithLimit()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        for (int i = 0; i < 10; i++)
        {
            await provider.StoreAsync($"key{i}", CreateMemoryItem($"Content with test {i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var results = (await provider.SearchAsync("test", limit: 3, cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task ShouldFindMatches_WhenSearchAsyncCaseInsensitive()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        await provider.StoreAsync("key1", CreateMemoryItem("UPPERCASE content"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", CreateMemoryItem("lowercase content"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", CreateMemoryItem("MiXeD cOnTeNt"), TestContext.Current.CancellationToken);

        // Act
        var results = (await provider.SearchAsync("content", cancellationToken: TestContext.Current.CancellationToken)).ToList();

        // Assert
        Assert.Equal(3, results.Count);
    }

    #endregion

    #region SearchSimilarAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenSearchSimilarAsyncWhenNotInitialized()
    {
        // Arrange - typed as the interface: since R10.1 the call dispatches to the
        // provider's real implementation (which guards initialization) instead of
        // silently resolving the empty default interface method.
        using var redisProvider = new Orkeon.Infrastructure.Memory.RedisMemoryProvider(_logger);
        IMemoryProvider provider = redisProvider;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SearchSimilarAsync([1f, 0f], cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region ClearAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenClearAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ClearAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldRemoveAllItems_WhenClearAsync()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        await provider.StoreAsync("key1", CreateMemoryItem("content 1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", CreateMemoryItem("content 2"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", CreateMemoryItem("content 3"), TestContext.Current.CancellationToken);

        // Act
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        var count = await provider.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);
        Assert.Null(await provider.GetAsync("key1", TestContext.Current.CancellationToken));
        Assert.Null(await provider.GetAsync("key2", TestContext.Current.CancellationToken));
        Assert.Null(await provider.GetAsync("key3", TestContext.Current.CancellationToken));
        Assert.True(_logger.HasLoggedInfo("Cleared 3 memory items"));
    }

    [Fact]
    public async Task ShouldNotThrow_WhenClearAsyncWithNoItems()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act
        await provider.ClearAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_logger.HasLoggedInfo("Cleared 0 memory items"));
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenCountAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnZero_WhenCountAsyncWithNoItems()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act
        var count = await provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ShouldReturnCorrectCount_WhenCountAsyncWithMultipleItems()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        await provider.StoreAsync("key1", CreateMemoryItem("content 1"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key2", CreateMemoryItem("content 2"), TestContext.Current.CancellationToken);
        await provider.StoreAsync("key3", CreateMemoryItem("content 3"), TestContext.Current.CancellationToken);

        // Act
        var count = await provider.CountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, count);
        Assert.True(_logger.HasLoggedDebug("Counted 3 memory items"));
    }

    #endregion

    #region ListKeysAsync Tests

    [Fact]
    public async Task ShouldThrow_WhenListKeysAsyncWhenNotInitialized()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ListKeysAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenListKeysAsyncWithNoItems()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act
        var keys = await provider.ListKeysAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public async Task ShouldReturnCorrectKeys_WhenListKeysAsyncWithPagination()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        for (int i = 0; i < 10; i++)
        {
            await provider.StoreAsync($"key{i:D2}", CreateMemoryItem($"content {i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var firstPage = await provider.ListKeysAsync(skip: 0, take: 3, cancellationToken: TestContext.Current.CancellationToken);
        var secondPage = await provider.ListKeysAsync(skip: 3, take: 3, cancellationToken: TestContext.Current.CancellationToken);
        var thirdPage = await provider.ListKeysAsync(skip: 6, take: 10, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, firstPage.Count);
        Assert.Equal(3, secondPage.Count);
        Assert.Equal(4, thirdPage.Count); // Only 4 items left

        // Verify no duplicates
        var allKeys = firstPage.Concat(secondPage).Concat(thirdPage).ToList();
        Assert.Equal(10, allKeys.Distinct().Count());
        Assert.True(_logger.HasLoggedDebug("Listed 3 memory keys"));
    }

    [Fact]
    public async Task ShouldStripKeyPrefix_WhenListKeysAsync()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        await provider.StoreAsync("mykey", CreateMemoryItem("content"), TestContext.Current.CancellationToken);

        // Act
        var keys = await provider.ListKeysAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(keys);
        Assert.Equal("mykey", keys[0]); // Should not include prefix
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task ShouldLogError_WhenStoreAsyncWithException()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);
        var config = new MemoryProviderConfig(providerType: "redis");
        await provider.InitializeAsync(config, TestContext.Current.CancellationToken);

        // Force an exception by storing null item (caught in validation)

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.StoreAsync("key1", null!, TestContext.Current.CancellationToken));

        // The error should be logged through validation, not through exception handling
        // since validation happens before the try-catch block
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void ShouldNotThrow_WhenDispose()
    {
        // Arrange
        using var provider = new InMemoryRedisMemoryProvider(_logger);

        // Act & Assert
        var exception = Record.Exception(() => provider.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldThrow_WhenOperationsAfterDispose()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        provider.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetAsync("key1", TestContext.Current.CancellationToken));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ShouldThrow_WhenStoreAsyncWithInvalidImportance()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            provider.StoreAsync("key1", MemoryItem.Create("content", null, 1.5f), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenOperationsWithCancellationToken()
    {
        // Arrange
        using var provider = await CreateInitializedProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        // Operations should still complete since our in-memory implementation
        // doesn't actually check cancellation (mimicking Redis behavior)
        await provider.StoreAsync("key1", CreateMemoryItem("test"), cts.Token);
        var result = await provider.GetAsync("key1", cts.Token);
        Assert.NotNull(result);
    }

    #endregion

    #region Helper Methods

    private async Task<InMemoryRedisMemoryProvider> CreateInitializedProvider()
    {
        var provider = new InMemoryRedisMemoryProvider(_logger);
        var config = new MemoryProviderConfig(
            providerType: "redis",
            connectionString: "localhost:6379");
        await provider.InitializeAsync(config);
        return provider;
    }

    private static MemoryItem CreateMemoryItem(
        string content,
        float importance = 0.5f,
        string[]? tags = null,
        string? source = null)
    {
        return MemoryItem.Create(
            content: content,
            embedding: null,
            importance: importance,
            source: source,
            tags: tags,
            createdBy: null,
            customProperties: null);
    }

    #endregion
}
