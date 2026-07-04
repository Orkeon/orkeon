using Orkeon.Domain.Memory;

namespace Orkeon.Domain.Tests.Memory;

public class MemoryTypeTests
{
    #region Enum Value Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingMemoryType()
    {
        // Assert
        Assert.Equal(0, (int)MemoryType.ShortTerm);
        Assert.Equal(1, (int)MemoryType.LongTerm);
        Assert.Equal(2, (int)MemoryType.Episodic);
        Assert.Equal(3, (int)MemoryType.Entity);
        Assert.Equal(4, (int)MemoryType.Procedural);
    }

    [Fact]
    public void ShouldBeDefined_WhenUsingMemoryTypeWithAllValues()
    {
        // Arrange
        var allValues = Enum.GetValues<MemoryType>();

        // Act & Assert
        Assert.Equal(5, allValues.Length);
        Assert.Contains(MemoryType.ShortTerm, allValues);
        Assert.Contains(MemoryType.LongTerm, allValues);
        Assert.Contains(MemoryType.Episodic, allValues);
        Assert.Contains(MemoryType.Entity, allValues);
        Assert.Contains(MemoryType.Procedural, allValues);
    }

    [Fact]
    public void ShouldMatchExpectedStrings_WhenUsingMemoryTypeUsingNames()
    {
        // Assert
        Assert.Equal("ShortTerm", nameof(MemoryType.ShortTerm));
        Assert.Equal("LongTerm", nameof(MemoryType.LongTerm));
        Assert.Equal("Episodic", nameof(MemoryType.Episodic));
        Assert.Equal("Entity", nameof(MemoryType.Entity));
        Assert.Equal("Procedural", nameof(MemoryType.Procedural));
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ShouldReturnCorrectStringRepresentation_WhenCallingToString()
    {
        // Assert
        Assert.Equal("ShortTerm", MemoryType.ShortTerm.ToString());
        Assert.Equal("LongTerm", MemoryType.LongTerm.ToString());
        Assert.Equal("Episodic", MemoryType.Episodic.ToString());
        Assert.Equal("Entity", MemoryType.Entity.ToString());
        Assert.Equal("Procedural", MemoryType.Procedural.ToString());
    }

    [Fact]
    public void ShouldBeConsistent_WhenCallingToStringForAllValues()
    {
        // Arrange
        var allValues = Enum.GetValues<MemoryType>();

        // Act & Assert
        foreach (var value in allValues)
        {
            var stringValue = value.ToString();
            Assert.NotNull(stringValue);
            Assert.NotEmpty(stringValue);
            Assert.DoesNotContain(" ", stringValue); // No spaces
            Assert.DoesNotContain("_", stringValue); // No underscores
        }
    }

    #endregion

    #region Parse Tests

    [Fact]
    public void ShouldReturnCorrectEnum_WhenParsingWithValidString()
    {
        // Act & Assert
        Assert.Equal(MemoryType.ShortTerm, Enum.Parse<MemoryType>("ShortTerm"));
        Assert.Equal(MemoryType.LongTerm, Enum.Parse<MemoryType>("LongTerm"));
        Assert.Equal(MemoryType.Episodic, Enum.Parse<MemoryType>("Episodic"));
        Assert.Equal(MemoryType.Entity, Enum.Parse<MemoryType>("Entity"));
        Assert.Equal(MemoryType.Procedural, Enum.Parse<MemoryType>("Procedural"));
    }

    [Fact]
    public void ShouldThrow_WhenParsingWithInvalidString()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            Enum.Parse<MemoryType>("InvalidMemoryType"));
    }

    [Fact]
    public void ShouldWork_WhenParsingWithCaseInsensitive()
    {
        // Act & Assert
        Assert.Equal(MemoryType.ShortTerm, Enum.Parse<MemoryType>("shortterm", true));
        Assert.Equal(MemoryType.LongTerm, Enum.Parse<MemoryType>("LONGTERM", true));
        Assert.Equal(MemoryType.Episodic, Enum.Parse<MemoryType>("ePiSoDiC", true));
    }

    [Fact]
    public void ShouldReturnCorrectEnum_WhenParsingWithNumericString()
    {
        // Act & Assert
        Assert.Equal(MemoryType.ShortTerm, Enum.Parse<MemoryType>("0"));
        Assert.Equal(MemoryType.LongTerm, Enum.Parse<MemoryType>("1"));
        Assert.Equal(MemoryType.Episodic, Enum.Parse<MemoryType>("2"));
        Assert.Equal(MemoryType.Entity, Enum.Parse<MemoryType>("3"));
        Assert.Equal(MemoryType.Procedural, Enum.Parse<MemoryType>("4"));
    }

    #endregion

    #region TryParse Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingTryParseWithValidString()
    {
        // Act
        var result = Enum.TryParse<MemoryType>("LongTerm", out var memoryType);

        // Assert
        Assert.True(result);
        Assert.Equal(MemoryType.LongTerm, memoryType);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingTryParseWithInvalidString()
    {
        // Act
        var result = Enum.TryParse<MemoryType>("InvalidType", out var memoryType);

        // Assert
        Assert.False(result);
        Assert.Equal(default(MemoryType), memoryType);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingTryParseWithNull()
    {
        // Act
        var result = Enum.TryParse<MemoryType>(null, out var memoryType);

        // Assert
        Assert.False(result);
        Assert.Equal(default(MemoryType), memoryType);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingTryParseWithEmptyString()
    {
        // Act
        var result = Enum.TryParse<MemoryType>("", out var memoryType);

        // Assert
        Assert.False(result);
        Assert.Equal(default(MemoryType), memoryType);
    }

    [Fact]
    public void ShouldWork_WhenUsingTryParseCaseInsensitive()
    {
        // Act & Assert
        Assert.True(Enum.TryParse<MemoryType>("episodic", true, out var type1));
        Assert.Equal(MemoryType.Episodic, type1);

        Assert.True(Enum.TryParse<MemoryType>("ENTITY", true, out var type2));
        Assert.Equal(MemoryType.Entity, type2);
    }

    #endregion

    #region IsDefined Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsDefinedWithValidValues()
    {
        // Assert
        Assert.True(Enum.IsDefined(MemoryType.ShortTerm));
        Assert.True(Enum.IsDefined(MemoryType.LongTerm));
        Assert.True(Enum.IsDefined(MemoryType.Episodic));
        Assert.True(Enum.IsDefined(MemoryType.Entity));
        Assert.True(Enum.IsDefined(MemoryType.Procedural));
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsDefinedWithInvalidValue()
    {
        // Assert
        Assert.False(Enum.IsDefined((MemoryType)99));
        Assert.False(Enum.IsDefined((MemoryType)(-1)));
    }

    [Fact]
    public void ShouldWork_WhenUsingIsDefinedWithStringValues()
    {
        // Assert
        Assert.True(Enum.IsDefined(typeof(MemoryType), "ShortTerm"));
        Assert.True(Enum.IsDefined(typeof(MemoryType), "LongTerm"));
        Assert.False(Enum.IsDefined(typeof(MemoryType), "InvalidType"));
    }

    #endregion

    #region GetNames and GetValues Tests

    [Fact]
    public void ShouldReturnAllEnumNames_WhenGettingNames()
    {
        // Act
        var names = Enum.GetNames<MemoryType>();

        // Assert
        Assert.Equal(5, names.Length);
        Assert.Contains("ShortTerm", names);
        Assert.Contains("LongTerm", names);
        Assert.Contains("Episodic", names);
        Assert.Contains("Entity", names);
        Assert.Contains("Procedural", names);
    }

    [Fact]
    public void ShouldReturnAllEnumValues_WhenGettingValues()
    {
        // Act
        var values = Enum.GetValues<MemoryType>();

        // Assert
        Assert.Equal(5, values.Length);
        Assert.Equal(MemoryType.ShortTerm, values[0]);
        Assert.Equal(MemoryType.LongTerm, values[1]);
        Assert.Equal(MemoryType.Episodic, values[2]);
        Assert.Equal(MemoryType.Entity, values[3]);
        Assert.Equal(MemoryType.Procedural, values[4]);
    }

    #endregion

    #region Casting Tests

    [Fact]
    public void ShouldReturnCorrectValue_WhenUsingCastToInt()
    {
        // Assert
        Assert.Equal(0, (int)MemoryType.ShortTerm);
        Assert.Equal(1, (int)MemoryType.LongTerm);
        Assert.Equal(2, (int)MemoryType.Episodic);
        Assert.Equal(3, (int)MemoryType.Entity);
        Assert.Equal(4, (int)MemoryType.Procedural);
    }

    [Fact]
    public void ShouldReturnCorrectEnum_WhenUsingCastFromInt()
    {
        // Assert
        Assert.Equal(MemoryType.ShortTerm, (MemoryType)0);
        Assert.Equal(MemoryType.LongTerm, (MemoryType)1);
        Assert.Equal(MemoryType.Episodic, (MemoryType)2);
        Assert.Equal(MemoryType.Entity, (MemoryType)3);
        Assert.Equal(MemoryType.Procedural, (MemoryType)4);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldSwitchStatement_WhenUsingComplexScenario()
    {
        // Arrange
        var allTypes = Enum.GetValues<MemoryType>();
        var results = new Dictionary<MemoryType, string>();

        // Act
        foreach (var type in allTypes)
        {
            var description = type switch
            {
                MemoryType.ShortTerm => "Recent events and immediate context",
                MemoryType.LongTerm => "Important persistent information",
                MemoryType.Episodic => "Complete event sequences",
                MemoryType.Entity => "Information about specific entities",
                MemoryType.Procedural => "Learned skills and procedures",
                _ => "Unknown memory type"
            };
            results[type] = description;
        }

        // Assert
        Assert.Equal(5, results.Count);
        Assert.DoesNotContain("Unknown memory type", results.Values);
        Assert.Equal("Recent events and immediate context", results[MemoryType.ShortTerm]);
        Assert.Equal("Learned skills and procedures", results[MemoryType.Procedural]);
    }

    [Fact]
    public void ShouldFlagsLikeBehavior_WhenUsingComplexScenario()
    {
        // Although MemoryType is not a Flags enum, test that it doesn't behave like one
        // Arrange
        var combined = (MemoryType)((int)MemoryType.ShortTerm | (int)MemoryType.LongTerm);

        // Act & Assert
        Assert.Equal((MemoryType)1, combined); // Should be LongTerm, not a combination
        Assert.False(Enum.IsDefined(combined) && combined != MemoryType.LongTerm);
    }

    [Fact]
    public void ShouldCollectionGrouping_WhenUsingComplexScenario()
    {
        // Arrange
        var memoryItems = new[]
        {
            (Type: MemoryType.ShortTerm, Content: "Recent task"),
            (Type: MemoryType.LongTerm, Content: "Important fact"),
            (Type: MemoryType.ShortTerm, Content: "Current context"),
            (Type: MemoryType.Entity, Content: "Person: John"),
            (Type: MemoryType.Episodic, Content: "Task completion episode"),
            (Type: MemoryType.LongTerm, Content: "Core knowledge"),
            (Type: MemoryType.Procedural, Content: "How to process data")
        };

        // Act
        var grouped = memoryItems.GroupBy(item => item.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        // Assert
        Assert.Equal(5, grouped.Count);
        Assert.Equal(2, grouped[MemoryType.ShortTerm]);
        Assert.Equal(2, grouped[MemoryType.LongTerm]);
        Assert.Equal(1, grouped[MemoryType.Entity]);
        Assert.Equal(1, grouped[MemoryType.Episodic]);
        Assert.Equal(1, grouped[MemoryType.Procedural]);
    }

    [Fact]
    public void ShouldConfigurationUsage_WhenUsingComplexScenario()
    {
        // Simulate configuration scenarios where MemoryType is used
        // Arrange
        var config = new Dictionary<string, object>
        {
            ["defaultMemoryType"] = "LongTerm",
            ["fallbackMemoryType"] = MemoryType.ShortTerm.ToString(),
            ["allowedTypes"] = new[] { "ShortTerm", "LongTerm", "Entity" }
        };

        // Act
        var defaultType = Enum.Parse<MemoryType>((string)config["defaultMemoryType"]);
        var fallbackType = Enum.Parse<MemoryType>((string)config["fallbackMemoryType"]);
        var allowedTypes = ((string[])config["allowedTypes"])
            .Select(s => Enum.Parse<MemoryType>(s))
            .ToList();

        // Assert
        Assert.Equal(MemoryType.LongTerm, defaultType);
        Assert.Equal(MemoryType.ShortTerm, fallbackType);
        Assert.Equal(3, allowedTypes.Count);
        Assert.DoesNotContain(MemoryType.Procedural, allowedTypes);
        Assert.DoesNotContain(MemoryType.Episodic, allowedTypes);
    }

    [Fact]
    public void ShouldMemoryTypeLifecycle_WhenUsingComplexScenario()
    {
        // Simulate memory type transitions
        // Arrange
        var memoryLifecycle = new List<(DateTime Time, MemoryType Type, string Reason)>();
        var startTime = DateTime.UtcNow;

        // Act - Simulate memory progression
        memoryLifecycle.Add((startTime, MemoryType.ShortTerm, "Initial creation"));
        memoryLifecycle.Add((startTime.AddMinutes(5), MemoryType.ShortTerm, "Still recent"));
        memoryLifecycle.Add((startTime.AddMinutes(30), MemoryType.LongTerm, "Promoted due to importance"));
        memoryLifecycle.Add((startTime.AddHours(1), MemoryType.Entity, "Identified as entity information"));

        // Assert
        Assert.Equal(4, memoryLifecycle.Count);

        // Verify progression
        var types = memoryLifecycle.Select(m => m.Type).ToList();
        Assert.Equal(MemoryType.ShortTerm, types[0]);
        Assert.Equal(MemoryType.ShortTerm, types[1]);
        Assert.Equal(MemoryType.LongTerm, types[2]);
        Assert.Equal(MemoryType.Entity, types[3]);

        // Verify time ordering
        for (int i = 1; i < memoryLifecycle.Count; i++)
        {
            Assert.True(memoryLifecycle[i].Time > memoryLifecycle[i - 1].Time);
        }
    }

    #endregion
}
