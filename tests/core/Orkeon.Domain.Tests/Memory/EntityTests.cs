using Orkeon.Domain.Memory;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.Memory;

public class EntityTests
{
    #region Entity Record Tests

    [Fact]
    public void ShouldCreateEntity_WhenUsingEntityCreateWithValidParameters()
    {
        // Arrange
        var name = "John Doe";
        var type = EntityType.Person;
        var attributes = new Dictionary<string, string>
        {
            ["email"] = "john@example.com",
            ["role"] = RoleDeveloper
        };

        // Act
        var entity = MemoryEntity.Create(name, type, attributes);

        // Assert
        Assert.Equal(name, entity.Name);
        Assert.Equal(type, entity.Type);
        Assert.Equal(attributes, entity.Attributes);
        Assert.True(entity.LastUpdated <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldCreateEntity_WhenUsingEntityCreateWithEmptyAttributes()
    {
        // Arrange
        var name = "Empty Entity";
        var type = EntityType.Other;
        var attributes = new Dictionary<string, string>();

        // Act
        var entity = MemoryEntity.Create(name, type, attributes);

        // Assert
        Assert.Equal(name, entity.Name);
        Assert.Equal(type, entity.Type);
        Assert.Empty(entity.Attributes);
        Assert.True(entity.LastUpdated <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrowOnNullName_WhenUsingEntityCreate()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MemoryEntity.Create(null!, EntityType.Person));
    }

    [Fact]
    public void ShouldThrowOnWhitespaceName_WhenUsingEntityCreate()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => MemoryEntity.Create("  ", EntityType.Person));
    }

    [Fact]
    public void ShouldBeEqual_WhenUsingEntityRecordEqualityWithSameValues()
    {
        // Arrange
        var name = "Test Entity";
        var type = EntityType.Organization;
        var attributes = new Dictionary<string, string> { ["key"] = "value" };
        var lastUpdated = DateTime.UtcNow;

        var entity1 = MemoryEntity.Create(name, type, attributes) with { LastUpdated = lastUpdated };
        var entity2 = MemoryEntity.Create(name, type, attributes) with { LastUpdated = lastUpdated };

        // Act & Assert
        Assert.Equal(entity1, entity2);
        Assert.True(entity1 == entity2);
        Assert.False(entity1 != entity2);
        Assert.Equal(entity1.GetHashCode(), entity2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingEntityRecordEqualityWithDifferentName()
    {
        // Arrange
        var attributes = new Dictionary<string, string> { ["key"] = "value" };
        var lastUpdated = DateTime.UtcNow;

        var entity1 = MemoryEntity.Create("Entity1", EntityType.Place, attributes) with { LastUpdated = lastUpdated };
        var entity2 = MemoryEntity.Create("Entity2", EntityType.Place, attributes) with { LastUpdated = lastUpdated };

        // Act & Assert
        Assert.NotEqual(entity1, entity2);
        Assert.False(entity1 == entity2);
        Assert.True(entity1 != entity2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingEntityRecordEqualityWithDifferentType()
    {
        // Arrange
        var name = "Same Name";
        var attributes = new Dictionary<string, string> { ["key"] = "value" };
        var lastUpdated = DateTime.UtcNow;

        var entity1 = MemoryEntity.Create(name, EntityType.Person, attributes) with { LastUpdated = lastUpdated };
        var entity2 = MemoryEntity.Create(name, EntityType.Organization, attributes) with { LastUpdated = lastUpdated };

        // Act & Assert
        Assert.NotEqual(entity1, entity2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingEntityRecordEqualityWithDifferentAttributes()
    {
        // Arrange
        var name = "Test Entity";
        var type = EntityType.Tool;
        var lastUpdated = DateTime.UtcNow;

        var attributes1 = new Dictionary<string, string> { ["key1"] = "value1" };
        var attributes2 = new Dictionary<string, string> { ["key2"] = "value2" };

        var entity1 = MemoryEntity.Create(name, type, attributes1) with { LastUpdated = lastUpdated };
        var entity2 = MemoryEntity.Create(name, type, attributes2) with { LastUpdated = lastUpdated };

        // Act & Assert
        Assert.NotEqual(entity1, entity2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingEntityRecordEqualityWithDifferentLastUpdated()
    {
        // Arrange
        var name = "Test Entity";
        var type = EntityType.Concept;
        var attributes = new Dictionary<string, string> { ["key"] = "value" };

        var entity1 = MemoryEntity.Create(name, type, attributes) with { LastUpdated = DateTime.UtcNow };
        var entity2 = MemoryEntity.Create(name, type, attributes) with { LastUpdated = DateTime.UtcNow.AddMinutes(1) };

        // Act & Assert
        Assert.NotEqual(entity1, entity2);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingEntityWith()
    {
        // Arrange
        var original = MemoryEntity.Create(
            "Original",
            EntityType.Person,
            new Dictionary<string, string> { ["age"] = "30" });

        var newAttributes = new Dictionary<string, string> { ["age"] = "31" };

        // Act
        var modified = original with { Attributes = newAttributes };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(original.Name, modified.Name);
        Assert.Equal(original.Type, modified.Type);
        Assert.NotEqual(original.Attributes, modified.Attributes);
        Assert.Equal("31", modified.Attributes["age"]);
        Assert.Equal(original.LastUpdated, modified.LastUpdated);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenUsingEntityToString()
    {
        // Arrange
        var entity = MemoryEntity.Create(
            "Test Entity",
            EntityType.Organization,
            new Dictionary<string, string>());

        // Act
        var result = entity.ToString();

        // Assert
        Assert.Contains("Entity", result);
        Assert.Contains("Name = Test Entity", result);
        Assert.Contains("Type = Organization", result);
    }

    [Fact]
    public void ShouldReturnAllComponents_WhenUsingEntityDeconstruct()
    {
        // Arrange
        var expectedName = "Deconstructed";
        var expectedType = EntityType.Place;
        var expectedAttributes = new Dictionary<string, string> { ["location"] = "NYC" };
        var expectedLastUpdated = DateTime.UtcNow;

        var entity = MemoryEntity.Create(expectedName, expectedType, expectedAttributes) with { LastUpdated = expectedLastUpdated };

        // Act
        var (name, type, attributes, lastUpdated) = entity;

        // Assert
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedType, type);
        Assert.Equal(expectedAttributes, attributes);
        Assert.Equal(expectedLastUpdated, lastUpdated);
    }

    [Fact]
    public void ShouldBeReadOnly_WhenUsingEntityAttributes()
    {
        // Arrange
        var mutableDict = new Dictionary<string, string> { ["key"] = "value" };
        var entity = MemoryEntity.Create("Test", EntityType.Other, mutableDict);

        // Act
        mutableDict["key"] = "modified";
        mutableDict["newKey"] = "newValue";

        // Assert
        Assert.Equal("value", entity.Attributes["key"]);
        Assert.False(entity.Attributes.ContainsKey("newKey"));
    }

    #endregion

    #region EntityType Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingEntityType()
    {
        // Assert
        Assert.Equal(0, (int)EntityType.Person);
        Assert.Equal(1, (int)EntityType.Place);
        Assert.Equal(2, (int)EntityType.Organization);
        Assert.Equal(3, (int)EntityType.Concept);
        Assert.Equal(4, (int)EntityType.Tool);
        Assert.Equal(5, (int)EntityType.Other);
    }

    [Fact]
    public void ShouldBeDefined_WhenUsingEntityTypeWithAllValues()
    {
        // Arrange
        var allValues = Enum.GetValues<EntityType>();

        // Act & Assert
        Assert.Equal(6, allValues.Length);
        Assert.Contains(EntityType.Person, allValues);
        Assert.Contains(EntityType.Place, allValues);
        Assert.Contains(EntityType.Organization, allValues);
        Assert.Contains(EntityType.Concept, allValues);
        Assert.Contains(EntityType.Tool, allValues);
        Assert.Contains(EntityType.Other, allValues);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingEntityTypeToString()
    {
        // Assert
        Assert.Equal("Person", EntityType.Person.ToString());
        Assert.Equal("Place", EntityType.Place.ToString());
        Assert.Equal("Organization", EntityType.Organization.ToString());
        Assert.Equal("Concept", EntityType.Concept.ToString());
        Assert.Equal("Tool", EntityType.Tool.ToString());
        Assert.Equal("Other", EntityType.Other.ToString());
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingEntityTypeParsing()
    {
        // Act & Assert
        Assert.Equal(EntityType.Person, Enum.Parse<EntityType>("Person"));
        Assert.Equal(EntityType.Place, Enum.Parse<EntityType>("Place"));
        Assert.Equal(EntityType.Organization, Enum.Parse<EntityType>("Organization"));
        Assert.Equal(EntityType.Concept, Enum.Parse<EntityType>("Concept"));
        Assert.Equal(EntityType.Tool, Enum.Parse<EntityType>("Tool"));
        Assert.Equal(EntityType.Other, Enum.Parse<EntityType>("Other"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingEntityTypeUsingTryParseWithValidValue()
    {
        // Act
        var result = Enum.TryParse<EntityType>("Organization", out var entityType);

        // Assert
        Assert.True(result);
        Assert.Equal(EntityType.Organization, entityType);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingEntityTypeUsingTryParseWithInvalidValue()
    {
        // Act
        var result = Enum.TryParse<EntityType>("InvalidType", out var entityType);

        // Assert
        Assert.False(result);
        Assert.Equal(default(EntityType), entityType);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldEntityWithManyAttributes_WhenUsingComplexScenario()
    {
        // Arrange
        var attributes = new Dictionary<string, string>
        {
            ["firstName"] = "John",
            ["lastName"] = "Doe",
            ["email"] = "john.doe@example.com",
            ["phone"] = "+1-555-0123",
            ["department"] = "Engineering",
            ["title"] = RoleSeniorDeveloper,
            ["location"] = "New York",
            ["employeeId"] = "EMP12345",
            ["manager"] = "Jane Smith",
            ["startDate"] = "2020-01-15"
        };

        var entity = MemoryEntity.Create("John Doe", EntityType.Person, attributes);

        // Act & Assert
        Assert.Equal(10, entity.Attributes.Count);
        Assert.Equal("john.doe@example.com", entity.Attributes["email"]);
        Assert.Equal("Engineering", entity.Attributes["department"]);

        // Verify immutability
        var modifiedAttributes = new Dictionary<string, string>(attributes)
        {
            ["email"] = "modified@example.com"
        };
        Assert.Equal("john.doe@example.com", entity.Attributes["email"]);
    }

    [Fact]
    public void ShouldEntityTypeForDifferentDomains_WhenUsingComplexScenario()
    {
        // Arrange & Act
        var person = MemoryEntity.Create("Alice Johnson", EntityType.Person,
            new Dictionary<string, string> { ["role"] = "CEO" });

        var place = MemoryEntity.Create("Central Park", EntityType.Place,
            new Dictionary<string, string> { ["city"] = "New York", ["type"] = "park" });

        var organization = MemoryEntity.Create("Acme Corp", EntityType.Organization,
            new Dictionary<string, string> { ["industry"] = "Technology", ["size"] = "Enterprise" });

        var concept = MemoryEntity.Create("Machine Learning", EntityType.Concept,
            new Dictionary<string, string> { ["category"] = "AI", ["complexity"] = "High" });

        var tool = MemoryEntity.Create("Visual Studio Code", EntityType.Tool,
            new Dictionary<string, string> { ["type"] = "IDE", ["language"] = "Multiple" });

        var other = MemoryEntity.Create("Project Alpha", EntityType.Other,
            new Dictionary<string, string> { ["status"] = Active, ["priority"] = "High" });

        // Assert
        Assert.Equal(EntityType.Person, person.Type);
        Assert.Equal(EntityType.Place, place.Type);
        Assert.Equal(EntityType.Organization, organization.Type);
        Assert.Equal(EntityType.Concept, concept.Type);
        Assert.Equal(EntityType.Tool, tool.Type);
        Assert.Equal(EntityType.Other, other.Type);

        // Verify each has appropriate attributes
        Assert.Equal("CEO", person.Attributes["role"]);
        Assert.Equal("New York", place.Attributes["city"]);
        Assert.Equal("Technology", organization.Attributes["industry"]);
        Assert.Equal("AI", concept.Attributes["category"]);
        Assert.Equal("IDE", tool.Attributes["type"]);
        Assert.Equal(Active, other.Attributes["status"]);
    }

    [Fact]
    public void ShouldEntityEvolution_WhenUsingComplexScenario()
    {
        // Arrange - Create initial entity
        var initialTime = DateTime.UtcNow;
        var entity = MemoryEntity.Create(
            "Startup Inc",
            EntityType.Organization,
            new Dictionary<string, string>
            {
                ["size"] = "Startup",
                ["employees"] = "10",
                ["funding"] = "Seed"
            }) with
        { LastUpdated = initialTime };

        // Act - Simulate evolution over time
        var afterGrowth = entity with
        {
            Attributes = new Dictionary<string, string>
            {
                ["size"] = "Small Business",
                ["employees"] = "50",
                ["funding"] = "Series A",
                ["revenue"] = "$1M"
            },
            LastUpdated = initialTime.AddYears(1)
        };

        var afterExpansion = afterGrowth with
        {
            Attributes = new Dictionary<string, string>
            {
                ["size"] = "Medium Enterprise",
                ["employees"] = "200",
                ["funding"] = "Series B",
                ["revenue"] = "$10M",
                ["offices"] = "3"
            },
            LastUpdated = initialTime.AddYears(3)
        };

        // Assert
        Assert.Equal("Startup Inc", entity.Name);
        Assert.Equal("Startup Inc", afterGrowth.Name);
        Assert.Equal("Startup Inc", afterExpansion.Name);

        Assert.Equal("10", entity.Attributes["employees"]);
        Assert.Equal("50", afterGrowth.Attributes["employees"]);
        Assert.Equal("200", afterExpansion.Attributes["employees"]);

        Assert.False(entity.Attributes.ContainsKey("revenue"));
        Assert.True(afterGrowth.Attributes.ContainsKey("revenue"));
        Assert.True(afterExpansion.Attributes.ContainsKey("offices"));

        Assert.True(afterGrowth.LastUpdated > entity.LastUpdated);
        Assert.True(afterExpansion.LastUpdated > afterGrowth.LastUpdated);
    }

    #endregion
}
