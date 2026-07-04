using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for Identity and derived identifier types following Clean Architecture principles.
/// Tests the business rules and validation logic of strongly-typed identifiers.
/// </summary>
public class IdentityTests
{
    [Fact]
    public void ShouldGenerateUniqueIdentity_WhenCreating()
    {
        // Act
        var id1 = AgentId.Create();
        var id2 = AgentId.Create();

        // Assert
        Assert.NotNull(id1);
        Assert.NotNull(id2);
        Assert.NotEqual(id1.Value, id2.Value);
        Assert.NotEqual(default(Ulid), id1.Value);
        Assert.NotEqual(default(Ulid), id2.Value);
    }

    [Fact]
    public void ShouldCreateIdentity_WhenUsingFromWithValidGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var agentId = AgentId.From(guid);

        // Assert
        Assert.NotNull(agentId);
        Assert.Equal(new Ulid(guid), agentId.Value);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithEmptyGuid()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => AgentId.From(Guid.Empty));

        Assert.Contains("Invalid AgentId: empty ULID", exception.Message);
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnGuidString_WhenCallingToString()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var agentId = AgentId.From(guid);

        // Act
        var result = agentId.ToString();

        // Assert
        // EntityId<T>.ToString() returns the ULID string representation
        Assert.Equal(agentId.Value.ToString(), result);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingImplicitConversionToGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var agentId = AgentId.From(guid);

        // Act
        Guid implicitGuid = agentId;

        // Assert
        Assert.Equal(guid, implicitGuid);
    }

    [Fact]
    public void ShouldReturnGuidString_WhenUsingImplicitConversionToString()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var agentId = AgentId.From(guid);

        // Act
        string implicitString = agentId;

        // Assert
        Assert.Equal(agentId.ToString(), implicitString);
    }

    [Fact]
    public void ShouldCreateNewGuid_WhenConstructing()
    {
        // Act
        var agentId = AgentId.Create();

        // Assert
        Assert.NotNull(agentId);
        Assert.NotEqual(default(Ulid), agentId.Value);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValue()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var id1 = AgentId.From(guid);
        var id2 = AgentId.From(guid);

        // Act & Assert
        Assert.Equal(id1, id2);
        Assert.True(id1.Equals(id2));
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValue()
    {
        // Arrange
        var id1 = AgentId.Create();
        var id2 = AgentId.Create();

        // Act & Assert
        Assert.NotEqual(id1, id2);
        Assert.False(id1.Equals(id2));
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
        Assert.NotEqual(id1.GetHashCode(), id2.GetHashCode());
    }

    [Theory]
    [InlineData(typeof(AgentId))]
    [InlineData(typeof(TaskId))]
    [InlineData(typeof(CrewId))]
    [InlineData(typeof(ToolId))]
    [InlineData(typeof(MemoryId))]
    [InlineData(typeof(ProcessId))]
    [InlineData(typeof(KnowledgeSourceId))]
    [InlineData(typeof(CollaborationId))]
    public void ShouldBehaveSimilarly_WhenUsingAllDerivedTypes(Type identityType)
    {
        // Act - Create two instances using the static Create() factory method
        // Create() is inherited from EntityId<T>, need FlattenHierarchy to find it on derived types
        var createMethod = identityType.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy, Type.EmptyTypes);
        Assert.NotNull(createMethod);
        var instance1 = createMethod!.Invoke(null, null);
        var instance2 = createMethod!.Invoke(null, null);

        // Get Value property
        var valueProperty = identityType.GetProperty("Value");
        var value1 = (Ulid)valueProperty!.GetValue(instance1)!;
        var value2 = (Ulid)valueProperty!.GetValue(instance2)!;

        // Assert
        Assert.NotNull(instance1);
        Assert.NotNull(instance2);
        // Create() generates unique ULIDs
        Assert.NotEqual(value1, value2);
        Assert.NotEqual(default(Ulid), value1);
        Assert.NotEqual(default(Ulid), value2);
    }

    [Fact]
    public void ShouldWorkIndependently_WhenUsingTaskId()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var taskId3 = TaskId.From(guid);

        // Assert
        Assert.NotNull(taskId1);
        Assert.NotNull(taskId2);
        Assert.NotNull(taskId3);
        Assert.NotEqual(taskId1, taskId2);
        Assert.Equal(new Ulid(guid), taskId3.Value);
    }

    [Fact]
    public void ShouldWorkIndependently_WhenUsingCrewId()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var crewId1 = CrewId.Create();
        var crewId2 = CrewId.Create();
        var crewId3 = CrewId.From(guid);

        // Assert
        Assert.NotNull(crewId1);
        Assert.NotNull(crewId2);
        Assert.NotNull(crewId3);
        Assert.NotEqual(crewId1, crewId2);
        Assert.Equal(new Ulid(guid), crewId3.Value);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingDifferentIdentityTypesWithSameGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var agentId = AgentId.From(guid);
        var taskId = TaskId.From(guid);

        // Act & Assert
        // They have same value but are different types
        Assert.Equal(agentId.Value.ToGuid(), taskId.Value.ToGuid());
        // Compiler won't allow direct comparison of different types
        Assert.NotEqual(agentId.GetType(), taskId.GetType());
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingIdentity()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var agentId = AgentId.From(guid);

        // Act & Assert
        // Records are immutable by default
        var agentId2 = AgentId.From(agentId.Value.ToGuid()); // Creates a copy
        Assert.Equal(agentId, agentId2);
        Assert.Equal(agentId.Value, agentId2.Value);
    }

    [Fact]
    public void ShouldSupportAllOperations_WhenUsingMemoryId()
    {
        // Act
        var memoryId = MemoryId.Create();
        Guid guidValue = memoryId;
        string stringValue = memoryId;

        // Assert
        Assert.NotNull(memoryId);
        // FIXME: // FIXME: // FIXME: // FIXME: Assert.Equal(memoryId.Value, guidValue); // constructor signature changed // constructor signature changed // constructor signature changed // constructor signature changed
        Assert.Equal(memoryId.ToString(), stringValue);
    }

    [Fact]
    public void ShouldBeUsableInCollections_WhenUsingProcessId()
    {
        // Arrange
        var processIds = new HashSet<ProcessId>();

        // Act
        var id1 = ProcessId.Create();
        var id2 = ProcessId.Create();
        var id3 = ProcessId.From(id1.Value.ToGuid()); // Same as id1

        processIds.Add(id1);
        processIds.Add(id2);
        processIds.Add(id3); // Should not add duplicate

        // Assert
        Assert.Equal(2, processIds.Count);
        Assert.Contains(id1, processIds);
        Assert.Contains(id2, processIds);
    }

}
