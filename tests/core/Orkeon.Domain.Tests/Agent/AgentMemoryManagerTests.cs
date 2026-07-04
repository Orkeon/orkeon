using Orkeon.Domain.Agent;
using Orkeon.Domain.Constants.Agent;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Domain.Tests.Agent;

/// <summary>
/// Tests for AgentMemoryManager internal domain helper.
/// Validates memory addition, capacity trimming, and read-only access.
/// </summary>
public class AgentMemoryManagerTests
{
    [Fact]
    public void ShouldAddMemory_WhenMemoryProvided()
    {
        // Arrange
        var memories = new List<AgentMemory>();
        var manager = new AgentMemoryManager(memories);
        var memory = AgentMemory.CreateShortTerm(TestContent, "context");

        // Act
        var result = manager.AddMemory(memory);

        // Assert
        Assert.Single(manager.Memories);
        Assert.Equal(memory, result);
        Assert.Equal(TestContent, manager.Memories[0].Content);
    }

    [Fact]
    public void ShouldThrow_WhenAddingNullMemory()
    {
        // Arrange
        var memories = new List<AgentMemory>();
        var manager = new AgentMemoryManager(memories);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.AddMemory(null!));
    }

    [Fact]
    public void ShouldThrow_WhenConstructedWithNullList()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AgentMemoryManager(null!));
    }

    [Fact]
    public void ShouldTrimOldMemories_WhenCapacityExceeded()
    {
        // Arrange
        var memories = new List<AgentMemory>();
        var manager = new AgentMemoryManager(memories);

        // Fill to max capacity (AgentDefaults.MaxMemories = 100)
        for (int i = 0; i < AgentDefaults.MaxMemories; i++)
        {
            manager.AddMemory(AgentMemory.CreateShortTerm($"Memory {i}"));
        }

        Assert.Equal(AgentDefaults.MaxMemories, manager.Memories.Count);

        // Act - add one more to exceed capacity
        var newMemory = AgentMemory.CreateShortTerm("Overflow memory");
        manager.AddMemory(newMemory);

        // Assert - count should be at max, oldest memories removed
        Assert.Equal(AgentDefaults.MaxMemories, manager.Memories.Count);
        // The newest memory should be present
        Assert.Equal("Overflow memory", manager.Memories[^1].Content);
        // The very first memory ("Memory 0") should be trimmed
        Assert.DoesNotContain(manager.Memories, m => m.Content == "Memory 0");
        // "Memory 1" should still be present (it is the new oldest)
        Assert.Contains(manager.Memories, m => m.Content == "Memory 1");
    }

    [Fact]
    public void ShouldReturnReadOnlyList_WhenAccessingMemories()
    {
        // Arrange
        var memories = new List<AgentMemory>();
        var manager = new AgentMemoryManager(memories);
        manager.AddMemory(AgentMemory.CreateShortTerm("content"));

        // Act
        var result = manager.Memories;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<AgentMemory>>(result);
        Assert.Single(result);
    }

    [Fact]
    public void ShouldReturnAddedMemory_WhenMemoryAddedSuccessfully()
    {
        // Arrange
        var memories = new List<AgentMemory>();
        var manager = new AgentMemoryManager(memories);
        var memory = AgentMemory.CreateLongTerm("Important knowledge");

        // Act
        var returned = manager.AddMemory(memory);

        // Assert
        Assert.Same(memory, returned);
    }

    [Fact]
    public void ShouldPreserveNewestMemories_WhenTrimmingMultipleExcess()
    {
        // Arrange
        var memories = new List<AgentMemory>();
        var manager = new AgentMemoryManager(memories);

        // Fill to max + 5 (will trim 5 oldest on each add past capacity)
        for (int i = 0; i < AgentDefaults.MaxMemories + 5; i++)
        {
            manager.AddMemory(AgentMemory.CreateShortTerm($"Memory {i}"));
        }

        // Assert
        Assert.Equal(AgentDefaults.MaxMemories, manager.Memories.Count);
        // Oldest surviving should be "Memory 5"
        Assert.Equal("Memory 5", manager.Memories[0].Content);
        // Newest should be the last added
        Assert.Equal($"Memory {AgentDefaults.MaxMemories + 4}", manager.Memories[^1].Content);
    }

    [Fact]
    public void ShouldSupportMultipleMemoryTypes_WhenAddingDifferentTypes()
    {
        // Arrange
        var memories = new List<AgentMemory>();
        var manager = new AgentMemoryManager(memories);

        // Act
        manager.AddMemory(AgentMemory.CreateShortTerm("short term"));
        manager.AddMemory(AgentMemory.CreateLongTerm("long term"));
        manager.AddMemory(AgentMemory.CreateEpisodic("episodic"));

        // Assert
        Assert.Equal(3, manager.Memories.Count);
        Assert.Equal(MemoryType.ShortTerm, manager.Memories[0].Type);
        Assert.Equal(MemoryType.LongTerm, manager.Memories[1].Type);
        Assert.Equal(MemoryType.Episodic, manager.Memories[2].Type);
    }
}
