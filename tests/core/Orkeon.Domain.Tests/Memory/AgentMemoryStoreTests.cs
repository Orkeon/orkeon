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

    [Trait("BR", "CH-08")]
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
    }

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
    [Fact]
    public void ShouldThrow_WhenCreatingWithNullAgentId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            AgentMemoryStore.Create(null!));
        Assert.Equal("ownerAgentId", exception.ParamName);
    }

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

    [Trait("BR", "CH-08")]
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

        // Assert
        Assert.Equal(2, store.DomainEvents.Count);
        Assert.IsType<MemoryAddedEvent>(store.DomainEvents[0]);
        Assert.IsType<MemoryPromotedEvent>(store.DomainEvents[1]);
    }

    #endregion

    #region Complex Scenarios Tests

    [Trait("BR", "CH-08")]
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
    }

    #endregion
}
