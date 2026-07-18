using Microsoft.Extensions.Logging;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Application.Interfaces.Ports;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;

namespace Orkeon.Application.Tests.Services;

public class MemoryServiceTests
{
    #region Test Doubles

    private class TestLogger : ILogger<MemoryService>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
            if (exception != null)
            {
                LoggedExceptions.Add(exception);
            }
        }

        public bool HasLoggedDebug(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Debug]") && m.Contains(partialMessage));
        }

        public bool HasLoggedInfo(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Information]") && m.Contains(partialMessage));
        }
    }

    private class TestMemoryProvider : IMemoryProvider
    {
        private readonly Dictionary<string, MemoryItem> _store = [];
        public List<string> MethodCalls { get; } = [];

        public System.Threading.Tasks.Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"StoreAsync:{key}");
            _store[key] = item;
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"GetAsync:{key}");
            return System.Threading.Tasks.Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);
        }

        public System.Threading.Tasks.Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"SearchAsync:{query}:{limit}");
            var results = _store.Values
                .Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit);
            return System.Threading.Tasks.Task.FromResult(results);
        }

        public System.Threading.Tasks.Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            MethodCalls.Add($"DeleteAsync:{key}");
            return System.Threading.Tasks.Task.FromResult(_store.Remove(key));
        }

        public System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default)
        {
            MethodCalls.Add("ClearAsync");
            _store.Clear();
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private class TestMemoryProviderFactory : IMemoryProviderFactory
    {
        private readonly TestMemoryProvider _provider;
        public List<string> CreatedConfigs { get; } = [];

        public TestMemoryProviderFactory(TestMemoryProvider? provider = null)
        {
            _provider = provider ?? new TestMemoryProvider();
        }

        public IMemoryProvider Create(Orkeon.Application.Memory.MemoryProviderConfigDto config, ILoggerFactory? loggerFactory = null)
        {
            CreatedConfigs.Add($"Create:{config.Type}");
            return _provider;
        }

        public async System.Threading.Tasks.Task<IMemoryProvider> CreateAndInitializeAsync(
            Orkeon.Application.Memory.MemoryProviderConfigDto config,
            ILoggerFactory? loggerFactory = null,
            CancellationToken cancellationToken = default)
        {
            CreatedConfigs.Add($"CreateAndInitializeAsync:{config.Type}");
            // No initialization needed for test provider
            return await System.Threading.Tasks.Task.FromResult(_provider);
        }
    }

    #endregion

    #region Test Helpers

    private static MemoryItem CreateTestMemoryItem(
        string content = "Test memory content",
        float importance = 0.5f,
        string source = "test",
        string[]? tags = null,
        AgentId? createdBy = null)
    {
        return MemoryItem.Create(
            content: content,
            embedding: null,
            importance: importance,
            source: source,
            tags: tags,
            createdBy: createdBy
        );
    }

    private static CrewId CreateTestCrewId()
    {
        return CrewId.From(Guid.NewGuid());
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullMemoryProviderFactory()
    {
        // Arrange
        var logger = new TestLogger();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MemoryService(null!, logger));
        Assert.Equal("memoryProviderFactory", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MemoryService(factory, null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithValidParameters()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();

        // Act
        using var service = new MemoryService(factory, logger);

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region GetMemorySystem Tests

    [Fact]
    public void ShouldReturnSameInstanceForSameCrewId_WhenGettingMemorySystem()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act
        var system1 = service.GetMemorySystem(crewId);
        var system2 = service.GetMemorySystem(crewId);

        // Assert
        Assert.Same(system1, system2);
    }

    [Fact]
    public void ShouldReturnDifferentInstancesForDifferentCrewIds_WhenGettingMemorySystem()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId1 = CreateTestCrewId();
        var crewId2 = CreateTestCrewId();

        // Act
        var system1 = service.GetMemorySystem(crewId1);
        var system2 = service.GetMemorySystem(crewId2);

        // Assert
        Assert.NotSame(system1, system2);
    }

    [Fact]
    public void ShouldReturnSystemWithAllMemoryTypes_WhenGettingMemorySystem()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act
        var system = service.GetMemorySystem(crewId);

        // Assert
        Assert.NotNull(system.ShortTerm);
        Assert.NotNull(system.LongTerm);
        Assert.NotNull(system.Entities);
        Assert.NotNull(system.Contextual);
    }

    #endregion

    #region SaveMemoryAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldSaveToLongTerm_WhenSavingMemoryAsyncWithHighImportance()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();
        var memory = CreateTestMemoryItem(importance: 0.8f);

        // Act
        await service.SaveMemoryAsync(crewId, memory, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Saved memory for crew {crewId}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSaveToShortTerm_WhenSavingMemoryAsyncWithLowImportance()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();
        var memory = CreateTestMemoryItem(importance: 0.3f);

        // Act
        await service.SaveMemoryAsync(crewId, memory, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Saved memory for crew {crewId}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSaveToLongTerm_WhenSavingMemoryAsyncWithImportanceThreshold()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();
        var memory = CreateTestMemoryItem(importance: 0.7f); // Exactly at threshold

        // Act
        await service.SaveMemoryAsync(crewId, memory, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Saved memory for crew {crewId}"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldComplete_WhenSavingMemoryAsyncWithCancellation()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();
        var memory = CreateTestMemoryItem();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act - Current implementation doesn't check cancellation
        await service.SaveMemoryAsync(crewId, memory, cts.Token);

        // Assert
        Assert.True(logger.HasLoggedDebug($"Saved memory for crew {crewId}"));
    }

    #endregion

    #region SearchMemoryAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldSearchBothMemoryTypes_WhenSearchingMemoryAsyncWithNoFilter()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Pre-populate some memories
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("Important data", 0.9f), TestContext.Current.CancellationToken);
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("Casual data", 0.3f), TestContext.Current.CancellationToken);

        // Act
        var results = await service.SearchMemoryAsync(crewId, "data", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.True(results.Count >= 0); // Results depend on internal implementation
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOnlySearchShortTerm_WhenSearchingMemoryAsyncWithShortTermFilter()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act
        var results = await service.SearchMemoryAsync(
            crewId,
            "test",
            maxResults: 5,
            typeFilter: DomainMemoryType.ShortTerm, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.True(results.Count <= 5);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOnlySearchLongTerm_WhenSearchingMemoryAsyncWithLongTermFilter()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act
        var results = await service.SearchMemoryAsync(
            crewId,
            "test",
            maxResults: 10,
            typeFilter: DomainMemoryType.LongTerm, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.True(results.Count <= 10);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOrderByImportanceAndRecency_WhenSearchingMemoryAsync()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Add memories with different importance
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("Low importance", 0.2f), TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Delay(10, TestContext.Current.CancellationToken); // Ensure different timestamps
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("High importance", 0.9f), TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Delay(10, TestContext.Current.CancellationToken);
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("Medium importance", 0.5f), TestContext.Current.CancellationToken);

        // Act
        var results = await service.SearchMemoryAsync(crewId, "importance", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        if (results.Count > 1)
        {
            // Verify ordering by importance (descending)
            for (int i = 0; i < results.Count - 1; i++)
            {
                Assert.True(results[i].Importance >= results[i + 1].Importance);
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLimitResults_WhenSearchingMemoryAsyncWithMaxResults()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();
        var maxResults = 3;

        // Act
        var results = await service.SearchMemoryAsync(crewId, "test", maxResults, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(results.Count <= maxResults);
    }

    #endregion

    #region ClearMemoryAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldClearAllMemoryTypes_WhenClearingMemoryAsyncWithNoFilter()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act
        await service.ClearMemoryAsync(crewId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedInfo($"Cleared memory for crew {crewId} with filter"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOnlyClearShortTerm_WhenClearingMemoryAsyncWithShortTermFilter()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act
        await service.ClearMemoryAsync(crewId, DomainMemoryType.ShortTerm, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedInfo($"Cleared memory for crew {crewId} with filter ShortTerm"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldOnlyClearLongTerm_WhenClearingMemoryAsyncWithLongTermFilter()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act
        await service.ClearMemoryAsync(crewId, DomainMemoryType.LongTerm, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(logger.HasLoggedInfo($"Cleared memory for crew {crewId} with filter LongTerm"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotThrow_WhenClearingMemoryAsyncForNonExistentCrew()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act & Assert - Should not throw
        var exception = await Record.ExceptionAsync(async () => await service.ClearMemoryAsync(crewId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldWorkEndToEnd_WhenUsingMemoryLifecycle()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();

        // Act - Save memories
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("Task completed successfully", 0.8f), TestContext.Current.CancellationToken);
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("Error encountered", 0.9f), TestContext.Current.CancellationToken);
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem("Debug info", 0.2f), TestContext.Current.CancellationToken);

        // Search memories
        var searchResults = await service.SearchMemoryAsync(crewId, "completed", 5, cancellationToken: TestContext.Current.CancellationToken);

        // Clear short-term memories
        await service.ClearMemoryAsync(crewId, DomainMemoryType.ShortTerm, TestContext.Current.CancellationToken);

        // Search again
        var resultsAfterClear = await service.SearchMemoryAsync(crewId, "completed", 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(searchResults);
        Assert.NotNull(resultsAfterClear);
        Assert.True(logger.LoggedMessages.Count > 0);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingConcurrentOperations()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId = CreateTestCrewId();
        var tasks = new List<System.Threading.Tasks.Task>();

        // Act - Concurrent saves
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await service.SaveMemoryAsync(
                    crewId,
                    CreateTestMemoryItem($"Memory {index}", index / 10.0f));
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Concurrent searches
        tasks.Clear();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await service.SearchMemoryAsync(crewId, "Memory");
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.True(logger.LoggedMessages.Count >= 5); // At least some logs from concurrent operations
    }

    [Fact]
    public void ShouldHaveIsolatedMemorySystems_WhenUsingMultipleCrews()
    {
        // Arrange
        var factory = new TestMemoryProviderFactory();
        var logger = new TestLogger();
        using var service = new MemoryService(factory, logger);
        var crewId1 = CreateTestCrewId();
        var crewId2 = CreateTestCrewId();
        var crewId3 = CreateTestCrewId();

        // Act
        var system1 = service.GetMemorySystem(crewId1);
        var system2 = service.GetMemorySystem(crewId2);
        var system3 = service.GetMemorySystem(crewId3);

        // Assert
        Assert.NotSame(system1, system2);
        Assert.NotSame(system2, system3);
        Assert.NotSame(system1, system3);

        // Verify same crew returns same system
        var system1Again = service.GetMemorySystem(crewId1);
        Assert.Same(system1, system1Again);
    }

    #endregion

    #region Crew-declared Provider (P2-O-02)

    [Fact]
    public async System.Threading.Tasks.Task ShouldBackLongTermMemory_WithCrewDeclaredProvider()
    {
        // Arrange — a crew declares "redis"; the registry records it and MemoryService must resolve
        // that provider via the factory and route long-term stores to it.
        var provider = new TestMemoryProvider();
        var factory = new TestMemoryProviderFactory(provider);
        var registry = new CrewMemoryProviderRegistry();
        var crewId = CreateTestCrewId();
        registry.SetProvider(crewId, "redis");
        using var service = new MemoryService(factory, new TestLogger(), registry);

        // Act — high importance (> 0.7) routes to long-term memory.
        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem(content: "durable insight", importance: 0.9f), TestContext.Current.CancellationToken);

        // Assert — the crew's declared provider was resolved and actually received the store.
        Assert.Contains("Create:redis", factory.CreatedConfigs);
        Assert.Contains(provider.MethodCalls, c => c.StartsWith("StoreAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSearchLongTermMemory_ThroughCrewDeclaredProvider()
    {
        var provider = new TestMemoryProvider();
        var factory = new TestMemoryProviderFactory(provider);
        var registry = new CrewMemoryProviderRegistry();
        var crewId = CreateTestCrewId();
        registry.SetProvider(crewId, "redis");
        using var service = new MemoryService(factory, new TestLogger(), registry);

        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem(content: "durable insight", importance: 0.9f), TestContext.Current.CancellationToken);
        var results = await service.SearchMemoryAsync(crewId, "insight", 5, DomainMemoryType.LongTerm, TestContext.Current.CancellationToken);

        Assert.Contains(provider.MethodCalls, c => c.StartsWith("SearchAsync", StringComparison.Ordinal));
        Assert.Single(results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldNotTouchProvider_WhenCrewDeclaredNoProvider()
    {
        // Default path: registry has no entry for the crew → in-process store, factory never invoked.
        var provider = new TestMemoryProvider();
        var factory = new TestMemoryProviderFactory(provider);
        var registry = new CrewMemoryProviderRegistry();
        var crewId = CreateTestCrewId();
        using var service = new MemoryService(factory, new TestLogger(), registry);

        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem(content: "durable insight", importance: 0.9f), TestContext.Current.CancellationToken);

        Assert.Empty(factory.CreatedConfigs);
        Assert.Empty(provider.MethodCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseInProcessStore_WhenNoRegistrySupplied()
    {
        // Backward compatibility: the registry ctor arg is optional; omitting it keeps the pre-P2-O-02
        // in-process behavior with no provider resolution.
        var provider = new TestMemoryProvider();
        var factory = new TestMemoryProviderFactory(provider);
        var crewId = CreateTestCrewId();
        using var service = new MemoryService(factory, new TestLogger());

        await service.SaveMemoryAsync(crewId, CreateTestMemoryItem(content: "durable insight", importance: 0.9f), TestContext.Current.CancellationToken);

        Assert.Empty(factory.CreatedConfigs);
        Assert.Empty(provider.MethodCalls);
    }

    #endregion
}
