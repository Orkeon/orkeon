using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Memory;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Application.Tests.TestHelpers;
using Orkeon.Application.Interfaces.Ports;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;
using AppIMemorySystem = Orkeon.Application.Interfaces.Services.ICrewMemorySystem;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Services;

public sealed class MemoryCoordinatorTests : IDisposable
{
    private static readonly string[] s_agentTaskTags = ["agent:123", "task:456"];
    private static readonly string[] s_agentExperienceTags = ["agent:123", "experience"];
    private static readonly string[] s_testTags = ["test"];

    private readonly Fixtures.TestLogger<MemoryCoordinator> _logger;
    private readonly TestMemoryService _memoryService;
    private readonly MemoryCoordinator _coordinator;
    private readonly DomainAgent _testAgent;
    private readonly DomainTask _testTask;
    private readonly SimpleExecutionContext _context;
    private readonly SimpleMemoryScope _memoryScope;

    public MemoryCoordinatorTests()
    {
        _logger = new Fixtures.TestLogger<MemoryCoordinator>();
        _memoryService = new TestMemoryService();
        _coordinator = new MemoryCoordinator(_logger, _memoryService);

        _testAgent = DomainAgent.Create(
            AgentRole.From("Test Agent"),
            AgentGoal.From(TestGoal),
            AgentBackstory.From("Test backstory"));

        _testTask = DomainTask.Create(
            TaskDescription.From("Test task description"),
            ExpectedOutput.From("Expected output"));

        _memoryScope = new SimpleMemoryScope("test-agent");
        _context = new SimpleExecutionContext(
            CrewId: CrewId.From(Guid.NewGuid()),
            Variables: [],
            Memory: _memoryScope,
            PreviousOutputs: [],
            CancellationToken: CancellationToken.None);
    }

    public void Dispose() => _memoryScope.Dispose();

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MemoryCoordinator(null!, _memoryService));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullMemoryService()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MemoryCoordinator(_logger, null!));
        Assert.Equal("memoryService", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnMemories_WhenRetrievingRelevantMemoriesAsyncWithValidInputs()
    {
        // Arrange
        var expectedMemories = new List<MemoryItem>
        {
            MemoryItem.Create(
                content: "Previous task result",
                embedding: null,
                importance: 0.8f,
                source: "task_execution",
                tags: s_agentTaskTags,
                createdBy: _testAgent.Id,
                customProperties: []),
            MemoryItem.Create(
                content: "Related experience",
                embedding: null,
                importance: 0.7f,
                source: "agent_experience",
                tags: s_agentExperienceTags,
                createdBy: _testAgent.Id,
                customProperties: [])
        };

        _memoryService.SetupSearchResult(expectedMemories);

        // Act
        var memories = await _coordinator.RetrieveRelevantMemoriesAsync(
            _testAgent, _testTask, _context, maxResults: 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, memories.Count());
        Assert.Contains(memories, m => m.Content == "Previous task result");
        Assert.Contains(memories, m => m.Content == "Related experience");

        // Verify search was called with correct parameters
        Assert.Equal(_context.CrewId, _memoryService.LastSearchCrewId);
        Assert.Contains(_testTask.Description.Value, _memoryService.LastSearchQuery);
        Assert.Contains(_testAgent.Role.Value, _memoryService.LastSearchQuery);
        Assert.Equal(10, _memoryService.LastMaxResults);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmpty_WhenRetrievingRelevantMemoriesAsyncWithNoMemories()
    {
        // Arrange
        _memoryService.SetupSearchResult([]);

        // Act
        var memories = await _coordinator.RetrieveRelevantMemoriesAsync(
            _testAgent, _testTask, _context, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(memories);
        Assert.True(_logger.HasLoggedMessage(LogLevel.Debug, "Retrieved 0 memories"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreMemory_WhenStoringTaskResultAsyncWithValidInputs()
    {
        // Arrange
        var taskResult = "Task completed successfully with result X";

        // Act
        await _coordinator.StoreTaskResultAsync(
            _testAgent, _testTask, taskResult, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Equal(_context.CrewId, _memoryService.LastSavedCrewId);

        var savedMemory = _memoryService.LastSavedMemory;
        Assert.NotNull(savedMemory);
        Assert.Equal(taskResult, savedMemory.Content);
        Assert.Equal(0.8f, savedMemory.Importance);
        Assert.Equal("task_execution", savedMemory.Metadata.Source);
        Assert.Contains($"agent:{_testAgent.Id}", savedMemory.Metadata.Tags ?? []);
        Assert.Contains($"task:{_testTask.Id.Value}", savedMemory.Metadata.Tags ?? []);

        // Verify custom properties
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("agent_id") ?? false);
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("agent_role") ?? false);
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("task_id") ?? false);
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("task_description") ?? false);
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("timestamp") ?? false);

        Assert.True(_logger.HasLoggedMessage(LogLevel.Debug, "Stored task result in memory"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreExperience_WhenStoringAgentExperienceAsyncWithValidInputs()
    {
        // Arrange
        var experience = "Learned that using tool X is more efficient for task Y";
        var importance = 0.9;

        // Act
        await _coordinator.StoreAgentExperienceAsync(
            _testAgent, experience, importance, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);

        var savedMemory = _memoryService.LastSavedMemory;
        Assert.NotNull(savedMemory);
        Assert.Equal(experience, savedMemory.Content);
        Assert.Equal((float)importance, savedMemory.Importance);
        Assert.Equal("agent_experience", savedMemory.Metadata.Source);
        Assert.Contains($"agent:{_testAgent.Id}", savedMemory.Metadata.Tags ?? []);
        Assert.Contains("experience", savedMemory.Metadata.Tags ?? []);

        // Verify custom properties
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("agent_id"));
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("agent_role"));
        Assert.Equal("experience", savedMemory.Metadata.CustomProperties?["type"]);

        Assert.True(_logger.HasLoggedMessage(LogLevel.Debug, "Stored experience for agent"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUpdateMemory_WhenUpdatingWorkingMemoryAsyncWithValidInputs()
    {
        // Arrange
        var key = "current_context";
        var value = "Processing customer order #12345";

        // Act
        await _coordinator.UpdateWorkingMemoryAsync(
            _testAgent, key, value, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);

        var savedMemory = _memoryService.LastSavedMemory;
        Assert.NotNull(savedMemory);
        Assert.Equal($"{key}: {value}", savedMemory.Content);
        Assert.Equal(0.5f, savedMemory.Importance); // Working memory has medium importance
        Assert.Equal("working_memory", savedMemory.Metadata.Source);
        Assert.Contains($"agent:{_testAgent.Id}", savedMemory.Metadata.Tags ?? []);
        Assert.Contains("working_memory", savedMemory.Metadata.Tags ?? []);
        Assert.Contains($"key:{key}", savedMemory.Metadata.Tags ?? []);

        // Verify custom properties
        Assert.True(savedMemory.Metadata.CustomProperties?.ContainsKey("timestamp"));

        Assert.True(_logger.HasLoggedMessage(LogLevel.Debug, "Updated working memory"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowOperationCanceledException_WhenRetrievingRelevantMemoriesAsyncWithCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _memoryService.SimulateDelay(TimeSpan.FromSeconds(5));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _coordinator.RetrieveRelevantMemoriesAsync(
                _testAgent, _testTask, _context, 10, cts.Token));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStillStore_WhenStoringTaskResultAsyncWithEmptyResult()
    {
        // Arrange
        var emptyResult = "[No output generated]"; // MemoryItem constructor rejects empty strings

        // Act
        await _coordinator.StoreTaskResultAsync(
            _testAgent, _testTask, emptyResult, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Equal(emptyResult, _memoryService.LastSavedMemory?.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenRetrievingRelevantMemoriesAsyncWithNullAgent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.RetrieveRelevantMemoriesAsync(
                null!, _testTask, _context, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenRetrievingRelevantMemoriesAsyncWithNullTask()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.RetrieveRelevantMemoriesAsync(
                _testAgent, null!, _context, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenRetrievingRelevantMemoriesAsyncWithNullContext()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.RetrieveRelevantMemoriesAsync(
                _testAgent, _testTask, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenStoringTaskResultAsyncWithNullAgent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.StoreTaskResultAsync(
                null!, _testTask, "result", _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenStoringTaskResultAsyncWithNullTask()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.StoreTaskResultAsync(
                _testAgent, null!, "result", _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenStoringTaskResultAsyncWithNullContext()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.StoreTaskResultAsync(
                _testAgent, _testTask, "result", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreNullContent_WhenStoringTaskResultAsyncWithNullResult()
    {
        // Act
        await _coordinator.StoreTaskResultAsync(
            _testAgent, _testTask, null!, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        // MemoryItem constructor converts null to "[No output generated]"
        Assert.Equal("[No output generated]", _memoryService.LastSavedMemory?.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenStoringAgentExperienceAsyncWithNullAgent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.StoreAgentExperienceAsync(
                null!, "experience", 0.5, _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreNullContent_WhenStoringAgentExperienceAsyncWithNullExperience()
    {
        // Act
        await _coordinator.StoreAgentExperienceAsync(
            _testAgent, null!, 0.5, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        // MemoryItem constructor converts null to "[No experience recorded]"
        Assert.Equal("[No experience recorded]", _memoryService.LastSavedMemory?.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreWithZeroImportance_WhenStoringAgentExperienceAsyncWithZeroImportance()
    {
        // Act
        await _coordinator.StoreAgentExperienceAsync(
            _testAgent, "Low importance experience", 0.0, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Equal(0.0f, _memoryService.LastSavedMemory?.Importance);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreWithMaxImportance_WhenStoringAgentExperienceAsyncWithMaxImportance()
    {
        // Act
        await _coordinator.StoreAgentExperienceAsync(
            _testAgent, "Critical experience", 1.0, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Equal(1.0f, _memoryService.LastSavedMemory?.Importance);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenUpdatingWorkingMemoryAsyncWithNullAgent()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _coordinator.UpdateWorkingMemoryAsync(
                null!, "key", "value", _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreWithNullKey_WhenUpdatingWorkingMemoryAsyncWithNullKey()
    {
        // Act
        await _coordinator.UpdateWorkingMemoryAsync(
            _testAgent, null!, "value", _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Contains("value", _memoryService.LastSavedMemory?.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreWithNullValue_WhenUpdatingWorkingMemoryAsyncWithNullValue()
    {
        // Act
        await _coordinator.UpdateWorkingMemoryAsync(
            _testAgent, "key", null!, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Contains("key", _memoryService.LastSavedMemory?.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreWithEmptyKey_WhenUpdatingWorkingMemoryAsyncWithEmptyKey()
    {
        // Act
        await _coordinator.UpdateWorkingMemoryAsync(
            _testAgent, string.Empty, "value", _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Contains("value", _memoryService.LastSavedMemory?.Content);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmpty_WhenRetrievingRelevantMemoriesAsyncWithMaxResultsZero()
    {
        // Arrange
        _memoryService.SetupSearchResult(
        [
            MemoryItem.Create(
                content: "Memory 1",
                embedding: null,
                importance: 0.8f,
                source: "test",
                tags: s_testTags,
                createdBy: _testAgent.Id,
                customProperties: [])
        ]);

        // Act
        var memories = await _coordinator.RetrieveRelevantMemoriesAsync(
            _testAgent, _testTask, _context, maxResults: 0, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(memories);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleGracefully_WhenRetrievingRelevantMemoriesAsyncWithNegativeMaxResults()
    {
        // Arrange
        _memoryService.SetupSearchResult(
        [
            MemoryItem.Create(
                content: "Memory 1",
                embedding: null,
                importance: 0.8f,
                source: "test",
                tags: s_testTags,
                createdBy: _testAgent.Id,
                customProperties: [])
        ]);

        // Act
        var memories = await _coordinator.RetrieveRelevantMemoriesAsync(
            _testAgent, _testTask, _context, maxResults: -1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(memories);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentOutOfRangeException_WhenStoringAgentExperienceAsyncWithNegativeImportance()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _coordinator.StoreAgentExperienceAsync(
                _testAgent, "Negative importance experience", -0.5, _context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreFullContent_WhenStoringAgentExperienceAsyncWithVeryLongExperience()
    {
        // Arrange
        var longExperience = new string('x', 10000);

        // Act
        await _coordinator.StoreAgentExperienceAsync(
            _testAgent, longExperience, 0.5, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Equal(longExperience, _memoryService.LastSavedMemory?.Content);
        Assert.Equal(10000, _memoryService.LastSavedMemory?.Content?.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStoreCorrectly_WhenUpdatingWorkingMemoryAsyncWithSpecialCharacters()
    {
        // Arrange
        var key = "special\nkey\t";
        var value = "value\"with\"quotes\"and'apostrophes'";

        // Act
        await _coordinator.UpdateWorkingMemoryAsync(
            _testAgent, key, value, _context, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_memoryService.MemorySaved);
        Assert.Contains(key, _memoryService.LastSavedMemory?.Content);
        Assert.Contains(value, _memoryService.LastSavedMemory?.Content);
    }
}

// Test double for IMemoryService
internal class TestMemoryService : IMemoryService
{
    public void ReleaseMemorySystem(CrewId crewId)
    {
        // Nothing held per crew in this double.
    }

    private List<MemoryItem> _searchResult = [];
    private TimeSpan _delay = TimeSpan.Zero;

    public bool MemorySaved { get; private set; }
    public CrewId? LastSavedCrewId { get; private set; }
    public MemoryItem? LastSavedMemory { get; private set; }

    public CrewId? LastSearchCrewId { get; private set; }
    public string? LastSearchQuery { get; private set; }
    public int LastMaxResults { get; private set; }

    public void SetupSearchResult(List<MemoryItem> memories)
    {
        _searchResult = memories;
    }

    public void SimulateDelay(TimeSpan delay)
    {
        _delay = delay;
    }

    public async System.Threading.Tasks.Task SaveMemoryAsync(
        CrewId crewId, MemoryItem memory, CancellationToken cancellationToken = default)
    {
        if (_delay > TimeSpan.Zero)
        {
            await System.Threading.Tasks.Task.Delay(_delay, cancellationToken);
        }

        MemorySaved = true;
        LastSavedCrewId = crewId;
        LastSavedMemory = memory;
    }

    public async System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId, string query, int maxResults = 10,
        string? filter = null, CancellationToken cancellationToken = default)
    {
        if (_delay > TimeSpan.Zero)
        {
            await System.Threading.Tasks.Task.Delay(_delay, cancellationToken);
        }

        LastSearchCrewId = crewId;
        LastSearchQuery = query;
        LastMaxResults = maxResults;

        return _searchResult.Take(maxResults).ToList();
    }

    public static System.Threading.Tasks.Task DeleteMemoryAsync(
        CrewId crewId, string memoryId, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public static System.Threading.Tasks.Task<MemoryItem?> GetMemoryAsync(
        CrewId crewId, string memoryId, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult<MemoryItem?>(null);
    }

    public static System.Threading.Tasks.Task ClearMemoriesAsync(
        CrewId crewId, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public AppIMemorySystem GetMemorySystem(CrewId crewId)
    {
        return new TestMemorySystem();
    }

    public async System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId, string query, int maxResults = 10,
        DomainMemoryType? memoryType = null, CancellationToken cancellationToken = default)
    {
        if (_delay > TimeSpan.Zero)
        {
            await System.Threading.Tasks.Task.Delay(_delay, cancellationToken);
        }

        LastSearchCrewId = crewId;
        LastSearchQuery = query;
        LastMaxResults = maxResults;

        return _searchResult.Take(maxResults).ToList();
    }

    public System.Threading.Tasks.Task ClearMemoryAsync(
        CrewId crewId, DomainMemoryType? memoryType = null,
        CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

// Test double for Application IMemorySystem
internal class TestMemorySystem : AppIMemorySystem
{
    public IShortTermMemory ShortTerm => new TestShortTermMemory();
    public ILongTermMemory LongTerm => new TestLongTermMemory();
    public IEntityMemory Entities => new TestEntityMemory();
    public IContextualMemory Contextual => new TestContextualMemory();
}

internal class TestShortTermMemory : IShortTermMemory
{
    public System.Threading.Tasks.Task AddAsync(MemoryItem item) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRecentAsync(int count = 10) =>
        System.Threading.Tasks.Task.FromResult<IReadOnlyList<MemoryItem>>([]);
    public System.Threading.Tasks.Task ClearAsync() => System.Threading.Tasks.Task.CompletedTask;
    public void Clear() { }
}

internal class TestLongTermMemory : ILongTermMemory
{
    public System.Threading.Tasks.Task AddAsync(MemoryItem item) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10) =>
        System.Threading.Tasks.Task.FromResult<IReadOnlyList<MemoryItem>>([]);
    public System.Threading.Tasks.Task ClearAsync() => System.Threading.Tasks.Task.CompletedTask;
}

internal class TestEntityMemory : IEntityMemory
{
    public System.Threading.Tasks.Task AddEntityAsync(string entityName, EntityType type, Dictionary<string, string> attributes) =>
        System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task<MemoryEntity?> GetEntityAsync(string entityName) =>
        System.Threading.Tasks.Task.FromResult<MemoryEntity?>(null);
    public System.Threading.Tasks.Task<IReadOnlyList<MemoryEntity>> GetEntitiesByTypeAsync(EntityType type) =>
        System.Threading.Tasks.Task.FromResult<IReadOnlyList<MemoryEntity>>([]);
    public System.Threading.Tasks.Task UpdateEntityAsync(string entityName, Dictionary<string, string> attributes) =>
        System.Threading.Tasks.Task.CompletedTask;
}

internal class TestContextualMemory : IContextualMemory
{
    public System.Threading.Tasks.Task<string> GetContextAsync(string query, int maxTokens = 1000) =>
        System.Threading.Tasks.Task.FromResult(string.Empty);
    public System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRelevantMemoriesAsync(string context, int count = 20) =>
        System.Threading.Tasks.Task.FromResult<IReadOnlyList<MemoryItem>>([]);
}

internal class SimpleMemoryScope : IMemoryScope
{
    public SimpleMemoryScope(string agentId)
    {
        AgentId = agentId;
        ScopeId = Guid.NewGuid().ToString();
    }

    public string AgentId { get; }
    public string ScopeId { get; }

    public async System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation)
    {
        return await operation();
    }

    public async System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation)
    {
        await operation();
    }

    public void Dispose()
    {
        // No-op for test
    }
}
