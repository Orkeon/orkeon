using Orkeon.Domain.Common;
using Orkeon.Domain.Memory.Events;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Memory;

public class AgentMemoryStoreTests
{
    private static readonly string[] TaskTypeCrewTag = ["task_type:TestCrewTask"];
    #region Test Doubles

    private class TestEmbeddingService : IEmbeddingService
    {
        private readonly Dictionary<string, float[]> _embeddings = [];
        private readonly float[] _defaultEmbedding = Enumerable.Range(0, 128).Select(i => (float)i / 128f).ToArray();

        public void SetEmbedding(string text, float[] embedding)
        {
            _embeddings[text] = embedding;
        }

        public System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            if (_embeddings.TryGetValue(text, out var embedding))
                return System.Threading.Tasks.Task.FromResult(embedding);

            // Return default embedding for unknown texts
            return System.Threading.Tasks.Task.FromResult(_defaultEmbedding);
        }
    }

    private class TestCrewTask : Orkeon.Domain.Task.ICrewTask
    {
        public TaskId TaskId { get; }
        public TaskDescription Description { get; }
        public ExpectedOutput ExpectedOutput { get; set; }
        public AgentId? AssignedAgent { get; set; }
        public Orkeon.Domain.Task.ValueObjects.TaskStatus Status { get; set; }
        public TaskOutput? Output { get; set; }
        public IReadOnlyList<TaskId> Dependencies { get; }
        public DateTime CreatedAt { get; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool AsyncExecution { get; set; }
        public JsonSchema? OutputJson { get; set; }
        public Type? OutputPydantic { get; set; }
        public string? OutputFile { get; set; }
        public bool HumanInput { get; set; }

        private readonly List<TaskId> _dependencies = [];

        public TestCrewTask(string description = "Test task")
        {
            TaskId = TaskId.From(Guid.NewGuid());
            Description = TaskDescription.From(description);
            ExpectedOutput = ExpectedOutput.From("Expected output");
            Status = Orkeon.Domain.Task.ValueObjects.TaskStatus.Pending;
            CreatedAt = DateTime.UtcNow;
            Dependencies = _dependencies.AsReadOnly();
        }

        public void AssignTo(AgentId agentId)
        {
            AssignedAgent = agentId;
        }

        public void Start(AgentId agentId)
        {
            AssignedAgent = agentId;
            Status = Orkeon.Domain.Task.ValueObjects.TaskStatus.InProgress;
            StartedAt = DateTime.UtcNow;
        }

        public void Complete(AgentId agentId, TaskOutput output)
        {
            Status = Orkeon.Domain.Task.ValueObjects.TaskStatus.Completed;
            CompletedAt = DateTime.UtcNow;
            Output = output;
        }

        public void Fail(string errorMessage, Exception? exception = null)
        {
            Status = Orkeon.Domain.Task.ValueObjects.TaskStatus.Failed;
            CompletedAt = DateTime.UtcNow;
            Output = TaskOutput.Create(errorMessage, success: false);
        }

        public bool CanExecute(Func<TaskId, bool> isTaskCompleted)
        {
            return Dependencies.All(isTaskCompleted);
        }

        public Orkeon.Domain.SharedKernel.ValidationResult ValidateOutput(TaskOutput output)
        {
            return Orkeon.Domain.SharedKernel.ValidationResult.Success();
        }

        public string GetContextSummary()
        {
            return $"Task: {Description.Value}";
        }

        public TimeSpan GetExecutionTime()
        {
            if (StartedAt == null) return TimeSpan.Zero;
            var endTime = CompletedAt ?? DateTime.UtcNow;
            return endTime - StartedAt.Value;
        }

        public void AddDependency(TaskId dependency)
        {
            _dependencies.Add(dependency);
        }
    }

    private static float[] CreateEmbedding(float baseValue)
    {
        return Enumerable.Range(0, 128).Select(i => baseValue + (i * 0.001f)).ToArray();
    }

    #endregion

    #region Create Factory Method Tests

    [Fact]
    public void ShouldCreateMemoryStore_WhenCreatingWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        var store = AgentMemoryStore.Create(agentId);

        // Assert
        Assert.NotNull(store.Id);
        Assert.Equal(agentId, store.OwnerAgentId);
        Assert.Equal(20, store.ShortTermCapacity);
        Assert.Equal(1000, store.LongTermCapacity);
        Assert.Empty(store.ShortTermMemory);
        Assert.Empty(store.LongTermMemory);
        Assert.Empty(store.EntityMemory);
        Assert.Empty(store.EpisodicMemory);
    }

    [Fact]
    public void ShouldSetCapacities_WhenCreatingWithCustomCapacities()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var shortTermCapacity = 50;
        var longTermCapacity = 2000;

        // Act
        var store = AgentMemoryStore.Create(agentId, shortTermCapacity, longTermCapacity);

        // Assert
        Assert.Equal(shortTermCapacity, store.ShortTermCapacity);
        Assert.Equal(longTermCapacity, store.LongTermCapacity);
    }

    [Fact]
    public void ShouldThrow_WhenCreatingWithNullAgentId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            AgentMemoryStore.Create(null!));
        Assert.Equal("ownerAgentId", exception.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenCreatingWithZeroShortTermCapacity()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            AgentMemoryStore.Create(agentId, 0, 1000));
        Assert.Contains("Short-term capacity must be positive", exception.Message);
    }

    [Fact]
    public void ShouldThrow_WhenCreatingWithNegativeLongTermCapacity()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            AgentMemoryStore.Create(agentId, 20, -1));
        Assert.Contains("Long-term capacity must be positive", exception.Message);
    }

    [Fact]
    public void ShouldRaiseMemoryStoreCreatedEvent_WhenCreating()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());

        // Act
        var store = AgentMemoryStore.Create(agentId);

        // Assert
        Assert.Single(store.DomainEvents);
        var createdEvent = Assert.IsType<MemoryStoreCreatedEvent>(store.DomainEvents[0]);
        Assert.Equal(store.Id, createdEvent.MemoryStoreId);
        Assert.Equal(agentId, createdEvent.OwnerAgentId);
    }

    #endregion

    #region AddShortTermMemory Tests

    [Fact]
    public void ShouldAddToShortTerm_WhenUsingAddShortTermMemoryWithValidMemory()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var memory = MemoryItem.Create("Test memory content");

        // Act
        store.AddShortTermMemory(memory);

        // Assert
        Assert.Single(store.ShortTermMemory);
        Assert.Contains(memory, store.ShortTermMemory);
    }

    [Fact]
    public void ShouldThrow_WhenUsingAddShortTermMemoryWithNullMemory()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            store.AddShortTermMemory(null!));
        Assert.Equal("memory", exception.ParamName);
    }

    [Fact]
    public void ShouldRemoveOldest_WhenUsingAddShortTermMemoryUsingExceedingCapacity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()), shortTermCapacity: 3);
        var memory1 = MemoryItem.Create("Memory 1");
        var memory2 = MemoryItem.Create("Memory 2");
        var memory3 = MemoryItem.Create("Memory 3");
        var memory4 = MemoryItem.Create("Memory 4");

        // Simulate time passing
        store.AddShortTermMemory(memory1);
        ClockAdvance.Tick();
        store.AddShortTermMemory(memory2);
        ClockAdvance.Tick();
        store.AddShortTermMemory(memory3);
        ClockAdvance.Tick();

        // Act
        store.AddShortTermMemory(memory4);

        // Assert
        Assert.Equal(3, store.ShortTermMemory.Count);
        Assert.DoesNotContain(memory1, store.ShortTermMemory);
        Assert.Contains(memory2, store.ShortTermMemory);
        Assert.Contains(memory3, store.ShortTermMemory);
        Assert.Contains(memory4, store.ShortTermMemory);
    }

    [Fact]
    public void ShouldPromoteWhenEvicted_WhenUsingAddShortTermMemoryWithHighImportance()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()), shortTermCapacity: 2);
        var importantMemory = MemoryItem.Create("Important memory", importance: 0.8f);
        var memory2 = MemoryItem.Create("Memory 2");
        var memory3 = MemoryItem.Create("Memory 3");

        store.AddShortTermMemory(importantMemory);
        ClockAdvance.Tick();
        store.AddShortTermMemory(memory2);
        ClockAdvance.Tick();

        // Act
        store.AddShortTermMemory(memory3);

        // Assert
        Assert.DoesNotContain(importantMemory, store.ShortTermMemory);
        Assert.Single(store.LongTermMemory);
        Assert.Contains(importantMemory, store.LongTermMemory);
    }

    [Fact]
    public void ShouldRaiseMemoryAddedEvent_WhenUsingAddShortTermMemory()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        var memory = MemoryItem.Create("Test memory");
        store.ClearDomainEvents();

        // Act
        store.AddShortTermMemory(memory);

        // Assert
        Assert.Single(store.DomainEvents);
        var addedEvent = Assert.IsType<MemoryAddedEvent>(store.DomainEvents[0]);
        Assert.Equal(store.Id, addedEvent.MemoryStoreId);
        Assert.Equal(memory.Id, addedEvent.MemoryItemId);
        Assert.Equal(memory.Content, addedEvent.Content);
        Assert.Equal(agentId, addedEvent.AgentId);
    }

    #endregion

    #region PromoteToLongTermMemory Tests

    [Fact]
    public void ShouldAddToLongTerm_WhenUsingPromoteToLongTermMemoryWithValidMemory()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var memory = MemoryItem.Create("Memory to promote");

        // Act
        store.PromoteToLongTermMemory(memory);

        // Assert
        Assert.Single(store.LongTermMemory);
        Assert.Contains(memory, store.LongTermMemory);
    }

    [Fact]
    public void ShouldThrow_WhenUsingPromoteToLongTermMemoryWithNullMemory()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            store.PromoteToLongTermMemory(null!));
        Assert.Equal("memory", exception.ParamName);
    }

    [Fact]
    public void ShouldRemoveLeastImportant_WhenUsingPromoteToLongTermMemoryUsingExceedingCapacity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()), longTermCapacity: 3);
        var memory1 = MemoryItem.Create("Memory 1", importance: 0.3f);
        var memory2 = MemoryItem.Create("Memory 2", importance: 0.7f);
        var memory3 = MemoryItem.Create("Memory 3", importance: 0.5f);
        var memory4 = MemoryItem.Create("Memory 4", importance: 0.9f);

        store.PromoteToLongTermMemory(memory1);
        store.PromoteToLongTermMemory(memory2);
        store.PromoteToLongTermMemory(memory3);

        // Act
        store.PromoteToLongTermMemory(memory4);

        // Assert
        Assert.Equal(3, store.LongTermMemory.Count);
        Assert.DoesNotContain(memory1, store.LongTermMemory); // Lowest importance
        Assert.Contains(memory2, store.LongTermMemory);
        Assert.Contains(memory3, store.LongTermMemory);
        Assert.Contains(memory4, store.LongTermMemory);
    }

    [Fact]
    public void ShouldRaiseMemoryPromotedEvent_WhenUsingPromoteToLongTermMemory()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        var memory = MemoryItem.Create("Memory to promote");
        store.ClearDomainEvents();

        // Act
        store.PromoteToLongTermMemory(memory);

        // Assert
        Assert.Single(store.DomainEvents);
        var promotedEvent = Assert.IsType<MemoryPromotedEvent>(store.DomainEvents[0]);
        Assert.Equal(store.Id, promotedEvent.MemoryStoreId);
        Assert.Equal(memory.Id, promotedEvent.MemoryItemId);
        Assert.Equal(memory.Content, promotedEvent.Content);
        Assert.Equal(agentId, promotedEvent.AgentId);
    }

    #endregion

    #region RetrieveRelevantMemoriesAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnRelevantMemories_WhenRetrievingRelevantMemoriesAsyncWithValidQuery()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var embeddingService = new TestEmbeddingService();

        var memory1 = MemoryItem.Create("Python programming", CreateEmbedding(0.1f));
        var memory2 = MemoryItem.Create("Java programming", CreateEmbedding(0.2f));
        var memory3 = MemoryItem.Create("Machine learning", CreateEmbedding(0.8f));

        store.AddShortTermMemory(memory1);
        store.AddShortTermMemory(memory2);
        store.PromoteToLongTermMemory(memory3);

        embeddingService.SetEmbedding("programming", CreateEmbedding(0.15f));

        // Act
        var results = await store.RetrieveRelevantMemoriesAsync("programming", embeddingService, limit: 2);

        // Assert
        var resultList = results.ToList();
        Assert.Equal(2, resultList.Count);
        Assert.Contains(memory1, resultList);
        Assert.Contains(memory2, resultList);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenRetrievingRelevantMemoriesAsyncWithEmptyQuery()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var embeddingService = new TestEmbeddingService();

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await store.RetrieveRelevantMemoriesAsync("", embeddingService));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenRetrievingRelevantMemoriesAsyncWithNullEmbeddingService()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.RetrieveRelevantMemoriesAsync(ParamQuery, null!));
        Assert.Equal("embeddingService", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenRetrievingRelevantMemoriesAsyncWithZeroLimit()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var embeddingService = new TestEmbeddingService();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await store.RetrieveRelevantMemoriesAsync(ParamQuery, embeddingService, 0));
        Assert.Contains("Limit must be positive", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldIncrementAccessCount_WhenRetrievingRelevantMemoriesAsync()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var embeddingService = new TestEmbeddingService();
        var memory = MemoryItem.Create("Test memory", CreateEmbedding(0.5f));
        store.AddShortTermMemory(memory);

        var initialAccessCount = memory.AccessCount;

        // Act
        await store.RetrieveRelevantMemoriesAsync("test", embeddingService, 1);

        // Assert
        Assert.Equal(initialAccessCount + 1, memory.AccessCount);
    }

    #endregion

    #region BuildContext Tests

    [Fact]
    public void ShouldReturnContext_WhenUsingBuildContextWithValidTask()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var task = new TestCrewTask("Process customer data");

        // Add memories
        for (int i = 0; i < 10; i++)
        {
            store.AddShortTermMemory(MemoryItem.Create($"Recent memory {i}"));
        }

        var taskRelatedMemory = MemoryItem.Create("Previous data processing result",
            tags: TaskTypeCrewTag);
        store.PromoteToLongTermMemory(taskRelatedMemory);

        var entity = new EntityMemory("customer", "entity", "Customer entity");
        store.UpdateEntityMemory("customer", entity);

        // Act
        var context = store.BuildContext(task);

        // Assert
        Assert.Equal(5, context.RecentMemories.Count); // Last 5 short-term
        Assert.Single(context.TaskRelatedMemories);
        Assert.Contains(taskRelatedMemory, context.TaskRelatedMemories);
        Assert.Single(context.RelevantEntities);
        Assert.Contains(entity, context.RelevantEntities);
    }

    [Fact]
    public void ShouldThrow_WhenUsingBuildContextWithNullTask()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            store.BuildContext(null!));
        Assert.Equal("task", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnEmptyCollections_WhenUsingBuildContextWithNoRelevantMemories()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var task = new TestCrewTask("Unrelated task");

        // Act
        var context = store.BuildContext(task);

        // Assert
        Assert.Empty(context.RecentMemories);
        Assert.Empty(context.TaskRelatedMemories);
        Assert.Empty(context.RelevantEntities);
    }

    #endregion

    #region UpdateEntityMemory Tests

    [Fact]
    public void ShouldAddOrUpdate_WhenUsingUpdateEntityMemoryWithValidEntity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var entity = new EntityMemory("John Doe", "person", "Software engineer");

        // Act
        store.UpdateEntityMemory("john_doe", entity);

        // Assert
        Assert.Single(store.EntityMemory);
        Assert.True(store.EntityMemory.ContainsKey("john_doe"));
        Assert.Equal(entity, store.EntityMemory["john_doe"]);
    }

    [Fact]
    public void ShouldThrow_WhenUsingUpdateEntityMemoryWithEmptyEntityName()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var entity = new EntityMemory("John Doe", "person");

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            store.UpdateEntityMemory("", entity));
    }

    [Fact]
    public void ShouldThrow_WhenUsingUpdateEntityMemoryWithNullEntity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            store.UpdateEntityMemory("entity_name", null!));
        Assert.Equal("entityMemory", exception.ParamName);
    }

    [Fact]
    public void ShouldRaiseEntityMemoryUpdatedEvent_WhenUsingUpdateEntityMemory()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        var entity = new EntityMemory("Company", "organization");
        store.ClearDomainEvents();

        // Act
        store.UpdateEntityMemory("company", entity);

        // Assert
        Assert.Single(store.DomainEvents);
        var updatedEvent = Assert.IsType<EntityMemoryUpdatedEvent>(store.DomainEvents[0]);
        Assert.Equal(agentId, updatedEvent.AgentId);
        Assert.Equal("company", updatedEvent.EntityKey);
        Assert.Equal(entity, updatedEvent.UpdatedValue);
    }

    #endregion

    #region AddEpisodicMemory Tests

    [Fact]
    public void ShouldAdd_WhenUsingAddEpisodicMemoryWithValidEpisode()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        var episode = EpisodicMemory.Create("Task completion", agentId);

        // Act
        store.AddEpisodicMemory(episode);

        // Assert
        Assert.Single(store.EpisodicMemory);
        Assert.Contains(episode, store.EpisodicMemory);
    }

    [Fact]
    public void ShouldThrow_WhenUsingAddEpisodicMemoryWithNullEpisode()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            store.AddEpisodicMemory(null!));
        Assert.Equal("episode", exception.ParamName);
    }

    [Fact]
    public void ShouldKeepMostRecent_WhenUsingAddEpisodicMemoryUsingExceedingLimit()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        var episodes = new List<EpisodicMemory>();

        // Add 101 episodes (exceeding the 100 limit)
        for (int i = 0; i < 101; i++)
        {
            var episode = EpisodicMemory.Create($"Episode {i}", agentId);
            episodes.Add(episode);
            store.AddEpisodicMemory(episode);
        }

        // Assert
        Assert.Equal(100, store.EpisodicMemory.Count);
        Assert.DoesNotContain(episodes[0], store.EpisodicMemory); // First (oldest) removed
        Assert.Contains(episodes[100], store.EpisodicMemory); // Last (newest) kept
    }

    [Fact]
    public void ShouldRaiseEpisodicMemoryAddedEvent_WhenUsingAddEpisodicMemory()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        var episode = EpisodicMemory.Create("Test episode", agentId);
        store.ClearDomainEvents();

        // Act
        store.AddEpisodicMemory(episode);

        // Assert
        Assert.Single(store.DomainEvents);
        var addedEvent = Assert.IsType<EpisodicMemoryAddedEvent>(store.DomainEvents[0]);
        Assert.Equal(agentId, addedEvent.AgentId);
        Assert.Equal(episode.Id, addedEvent.EpisodeId);
        Assert.Equal(episode.Title, addedEvent.EpisodeTitle);
    }

    #endregion

    #region ClearShortTermMemory Tests

    [Fact]
    public void ShouldRemoveAllShortTermMemories_WhenUsingClearShortTermMemory()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        store.AddShortTermMemory(MemoryItem.Create("Memory 1"));
        store.AddShortTermMemory(MemoryItem.Create("Memory 2"));
        store.AddShortTermMemory(MemoryItem.Create("Memory 3"));

        // Act
        store.ClearShortTermMemory();

        // Assert
        Assert.Empty(store.ShortTermMemory);
    }

    [Fact]
    public void ShouldNotAffectLongTermMemory_WhenUsingClearShortTermMemory()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var longTermMemory = MemoryItem.Create("Long term");
        store.PromoteToLongTermMemory(longTermMemory);
        store.AddShortTermMemory(MemoryItem.Create("Short term"));

        // Act
        store.ClearShortTermMemory();

        // Assert
        Assert.Empty(store.ShortTermMemory);
        Assert.Single(store.LongTermMemory);
        Assert.Contains(longTermMemory, store.LongTermMemory);
    }

    [Fact]
    public void ShouldRaiseMemoryClearedEvent_WhenUsingClearShortTermMemory()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        store.AddShortTermMemory(MemoryItem.Create("Memory"));
        store.ClearDomainEvents();

        // Act
        store.ClearShortTermMemory();

        // Assert
        Assert.Single(store.DomainEvents);
        var clearedEvent = Assert.IsType<MemoryClearedEvent>(store.DomainEvents[0]);
        Assert.Equal(store.Id, clearedEvent.MemoryStoreId);
        Assert.Equal(MemoryType.ShortTerm, clearedEvent.MemoryType);
        Assert.Equal(agentId, clearedEvent.AgentId);
    }

    #endregion

    #region UpdateCapacities Tests

    [Fact]
    public void ShouldUpdate_WhenUpdatingCapacitiesWithValidShortTermCapacity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var newCapacity = 50;

        // Act
        store.UpdateCapacities(shortTermCapacity: newCapacity);

        // Assert
        Assert.Equal(newCapacity, store.ShortTermCapacity);
        Assert.Equal(1000, store.LongTermCapacity); // Unchanged
    }

    [Fact]
    public void ShouldUpdate_WhenUpdatingCapacitiesWithValidLongTermCapacity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var newCapacity = 5000;

        // Act
        store.UpdateCapacities(longTermCapacity: newCapacity);

        // Assert
        Assert.Equal(20, store.ShortTermCapacity); // Unchanged
        Assert.Equal(newCapacity, store.LongTermCapacity);
    }

    [Fact]
    public void ShouldUpdateBoth_WhenUpdatingCapacitiesWithBothCapacities()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));
        var newShortTerm = 30;
        var newLongTerm = 2000;

        // Act
        store.UpdateCapacities(newShortTerm, newLongTerm);

        // Assert
        Assert.Equal(newShortTerm, store.ShortTermCapacity);
        Assert.Equal(newLongTerm, store.LongTermCapacity);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingCapacitiesWithZeroShortTermCapacity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            store.UpdateCapacities(shortTermCapacity: 0));
        Assert.Contains("Short-term capacity must be positive", exception.Message);
    }

    [Fact]
    public void ShouldThrow_WhenUpdatingCapacitiesWithNegativeLongTermCapacity()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            store.UpdateCapacities(longTermCapacity: -100));
        Assert.Contains("Long-term capacity must be positive", exception.Message);
    }

    #endregion

    #region AggregateRoot Behavior Tests

    [Fact]
    public void ShouldReturnMemoryStoreIdAsString_WhenUsingIdProperty()
    {
        // Arrange
        var store = AgentMemoryStore.Create(AgentId.From(Guid.NewGuid()));

        // Act
        var id = store.Id;

        // Assert
        Assert.Equal(store.Id.ToString(), id.ToString());
    }

    [Fact]
    public void ShouldAccumulateEvents_WhenUsingDomainEvents()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        store.ClearDomainEvents();

        // Act
        store.AddShortTermMemory(MemoryItem.Create("Memory 1"));
        store.PromoteToLongTermMemory(MemoryItem.Create("Memory 2"));
        store.UpdateEntityMemory("entity", new EntityMemory("Entity", "type"));

        // Assert
        Assert.Equal(3, store.DomainEvents.Count);
        Assert.IsType<MemoryAddedEvent>(store.DomainEvents[0]);
        Assert.IsType<MemoryPromotedEvent>(store.DomainEvents[1]);
        Assert.IsType<EntityMemoryUpdatedEvent>(store.DomainEvents[2]);
    }

    #endregion

    #region Complex Scenarios Tests

    [Fact]
    public void ShouldMemoryLifecycle_WhenUsingComplexScenario()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId, shortTermCapacity: 5, longTermCapacity: 10);

        // Act & Assert - Build up short-term memory
        var memories = new List<MemoryItem>();
        for (int i = 0; i < 8; i++)
        {
            var memory = MemoryItem.Create($"Memory {i}", importance: 0.3f + (i * 0.1f));
            memories.Add(memory);
            store.AddShortTermMemory(memory);
            ClockAdvance.Tick(); // Ensure different timestamps (R5.6)
        }

        // Should have 5 in short-term (capacity limit)
        Assert.Equal(5, store.ShortTermMemory.Count);

        // Oldest 3 with importance > 0.7 should be promoted
        var longTermMemories = store.LongTermMemory.ToList();
        Assert.True(longTermMemories.All(m => m.Importance > 0.7f));

        // Add entity memories
        var customer = new EntityMemory("ABC Corp", "organization", "Major customer");
        customer.SetAttribute("industry", "Technology");
        customer.SetAttribute("size", "Enterprise");
        store.UpdateEntityMemory("abc_corp", customer);

        var contact = new EntityMemory("John Smith", "person", "CEO of ABC Corp");
        contact.AddRelationship(new EntityRelationship("ABC Corp", "works_for", "CEO position"));
        store.UpdateEntityMemory("john_smith", contact);

        // Add episodic memory
        var episode = EpisodicMemory.Create("Customer meeting", agentId);
        episode.AddEvent(EpisodeEvent.Create("decision", "Decided to offer enterprise pricing"));
        episode.AddEvent(EpisodeEvent.Create("action", "Sent proposal to John Smith"));
        episode.Complete(EpisodeOutcome.Success, ["Always lead with value proposition"]);
        store.AddEpisodicMemory(episode);

        // Build context for a related task
        var task = new TestCrewTask("Follow up with ABC Corp about the proposal");
        var context = store.BuildContext(task);

        // Verify context contains relevant information
        Assert.NotEmpty(context.RecentMemories);
        Assert.NotEmpty(context.RelevantEntities);
        Assert.Contains(context.RelevantEntities, e => e.Name == "ABC Corp");
        Assert.True(context.HasContent);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSemanticMemoryRetrieval_WhenUsingComplexScenario()
    {
        // Arrange
        var agentId = AgentId.From(Guid.NewGuid());
        var store = AgentMemoryStore.Create(agentId);
        var embeddingService = new TestEmbeddingService();

        // Create memories with different semantic similarities
        var programmingMemory1 = MemoryItem.Create("Python is great for data science", CreateEmbedding(0.8f));
        var programmingMemory2 = MemoryItem.Create("JavaScript is used for web development", CreateEmbedding(0.75f));
        var cookingMemory = MemoryItem.Create("Italian pasta requires al dente cooking", CreateEmbedding(0.2f));
        var travelMemory = MemoryItem.Create("Paris is beautiful in spring", CreateEmbedding(0.1f));

        store.AddShortTermMemory(programmingMemory1);
        store.AddShortTermMemory(cookingMemory);
        store.PromoteToLongTermMemory(programmingMemory2);
        store.PromoteToLongTermMemory(travelMemory);

        // Set up query embedding close to programming memories
        embeddingService.SetEmbedding("programming languages", CreateEmbedding(0.77f));

        // Act
        var results = await store.RetrieveRelevantMemoriesAsync("programming languages", embeddingService, limit: 2);

        // Assert
        var resultList = results.ToList();
        Assert.Equal(2, resultList.Count);
        Assert.Contains(programmingMemory1, resultList);
        Assert.Contains(programmingMemory2, resultList);
        Assert.DoesNotContain(cookingMemory, resultList);
        Assert.DoesNotContain(travelMemory, resultList);

        // Verify access counts were incremented
        Assert.Equal(1, programmingMemory1.AccessCount);
        Assert.Equal(1, programmingMemory2.AccessCount);
        Assert.Equal(0, cookingMemory.AccessCount);
        Assert.Equal(0, travelMemory.AccessCount);
    }

    #endregion
}
