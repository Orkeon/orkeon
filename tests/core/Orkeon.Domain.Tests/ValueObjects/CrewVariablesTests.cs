using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class CrewVariablesTests
{
    [Fact]
    public void ShouldStoreString_WhenSettingWithStringValue()
    {
        // Arrange
        var variables = CrewVariables.Empty;
        var key = "name";
        var value = "John Doe";

        // Act
        var updated = variables.Set(key, value);

        // Assert
        Assert.Equal(value, updated.GetString(key));
    }

    [Fact]
    public void ShouldStoreInt_WhenSettingWithIntValue()
    {
        // Arrange
        var variables = CrewVariables.Empty;
        var key = "age";
        var value = 30;

        // Act
        var updated = variables.Set(key, value);

        // Assert
        Assert.Equal(value, updated.Get<int>(key));
    }

    [Fact]
    public void ShouldStoreBool_WhenSettingWithBoolValue()
    {
        // Arrange
        var variables = CrewVariables.Empty;
        var key = "isActive";
        var value = true;

        // Act
        var updated = variables.Set(key, value);

        // Assert
        Assert.Equal(value, updated.Get<bool>(key));
    }

    [Fact]
    public void ShouldStoreDouble_WhenSettingWithDoubleValue()
    {
        // Arrange
        var variables = CrewVariables.Empty;
        var key = "rate";
        var value = 3.14;

        // Act
        var updated = variables.Set(key, value);

        // Assert
        Assert.Equal(value, updated.Get<double>(key));
    }

    [Fact]
    public void ShouldOverwriteValue_WhenSettingWithExistingKey()
    {
        // Arrange
        var variables = CrewVariables.Empty;
        var key = "value";

        // Act
        var v1 = variables.Set(key, "initial");
        var v2 = v1.Set(key, "updated");

        // Assert
        Assert.Equal("updated", v2.GetString(key));
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var variables = CrewVariables.Empty;

        // Act & Assert
        Assert.Null(variables.Get<int>("nonexistent"));
        Assert.Null(variables.Get<bool>("nonexistent"));
        Assert.Null(variables.Get<double>("nonexistent"));
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingStringWithNonExistentKey()
    {
        // Arrange
        var variables = CrewVariables.Empty;

        // Act
        var result = variables.GetString("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingWithWrongType()
    {
        // Arrange
        var variables = CrewVariables.Empty
            .Set("stringKey", "value")
            .Set("intKey", 42);

        // Act & Assert
        Assert.Null(variables.Get<int>("stringKey")); // String stored, int requested
        Assert.Null(variables.Get<bool>("intKey")); // Int stored, bool requested
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingToDictionaryWithMixedTypes()
    {
        // Arrange
        var variables = CrewVariables.Empty
            .Set("name", "Test")
            .Set("count", 10)
            .Set("enabled", true)
            .Set("rate", 2.5);

        // Act
        var dict = variables.ToDictionary();

        // Assert
        Assert.Equal(4, dict.Count);
        Assert.Equal("Test", dict["name"]);
        Assert.Equal(10, dict["count"]);
        Assert.True((bool)dict["enabled"]);
        Assert.Equal(2.5, dict["rate"]);
    }

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenUsingToDictionaryWithEmptyVariables()
    {
        // Arrange
        var variables = CrewVariables.Empty;

        // Act
        var dict = variables.ToDictionary();

        // Assert
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldNotHaveDuplicates_WhenUsingToDictionaryWithDuplicateKeysAcrossTypes()
    {
        // Arrange - Setting same key with different types
        // With immutable API, each Set returns a new instance
        var variables = CrewVariables.Empty
            .Set("key", "string value")
            .Set("key", 42);

        // Act
        var dict = variables.ToDictionary();

        // Assert — both string and int stores have "key", int wins last in ToDictionary
        Assert.Equal(42, dict["key"]);
    }

    [Fact]
    public void ShouldCreateVariables_WhenUsingFromDictionaryWithStringValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.Equal("value1", variables.GetString("key1"));
        Assert.Equal("value2", variables.GetString("key2"));
    }

    [Fact]
    public void ShouldCreateVariables_WhenUsingFromDictionaryWithIntValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "age", 25 },
            { "count", 100 }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.Equal(25, variables.Get<int>("age"));
        Assert.Equal(100, variables.Get<int>("count"));
    }

    [Fact]
    public void ShouldCreateVariables_WhenUsingFromDictionaryWithBoolValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "active", true },
            { "visible", false }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.True(variables.Get<bool>("active"));
        Assert.False(variables.Get<bool>("visible"));
    }

    [Fact]
    public void ShouldCreateVariables_WhenUsingFromDictionaryWithDoubleValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "pi", 3.14159 },
            { "rate", 0.05 }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.Equal(3.14159, variables.Get<double>("pi"));
        Assert.Equal(0.05, variables.Get<double>("rate"));
    }

    [Fact]
    public void ShouldConvertToDouble_WhenUsingFromDictionaryWithFloatValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "floatValue", 1.5f }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.Equal(1.5, variables.Get<double>("floatValue"));
    }

    [Fact]
    public void ShouldCreateVariables_WhenUsingFromDictionaryWithMixedTypes()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "name", "Test" },
            { "age", 30 },
            { "active", true },
            { "score", 95.5 }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.Equal("Test", variables.GetString("name"));
        Assert.Equal(30, variables.Get<int>("age"));
        Assert.True(variables.Get<bool>("active"));
        Assert.Equal(95.5, variables.Get<double>("score"));
    }

    [Fact]
    public void ShouldConvertToString_WhenUsingFromDictionaryWithUnknownType()
    {
        // Arrange
        var customObject = new { Name = "Test", Value = 123 };
        var dict = new Dictionary<string, object>
        {
            { "custom", customObject }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        var result = variables.GetString("custom");
        Assert.NotNull(result);
        Assert.Contains("Name", result);
        Assert.Contains("Test", result);
    }

    [Fact]
    public void ShouldStoreEmptyString_WhenUsingFromDictionaryWithNullValue()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "nullKey", null! }
        };

        // Act
        var variables = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.Equal(string.Empty, variables.GetString("nullKey"));
    }

    [Fact]
    public void ShouldPreserveValues_WhenUsingRoundTripToDictionaryFromDictionary()
    {
        // Arrange
        var original = CrewVariables.Empty
            .Set("string", "value")
            .Set("int", 42)
            .Set("bool", true)
            .Set("double", 3.14);

        // Act
        var dict = original.ToDictionary();
        var restored = CrewVariables.FromDictionary(dict);

        // Assert
        Assert.Equal("value", restored.GetString("string"));
        Assert.Equal(42, restored.Get<int>("int"));
        Assert.True(restored.Get<bool>("bool"));
        Assert.Equal(3.14, restored.Get<double>("double"));
    }

    [Fact]
    public void ShouldThrow_WhenSettingWithEmptyStringKey()
    {
        // Arrange
        var variables = CrewVariables.Empty;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => variables.Set("", "value"));
    }

    [Fact]
    public void ShouldKeepLastValue_WhenSettingWithMultipleTimesWithSameKey()
    {
        // Arrange
        var variables = CrewVariables.Empty;
        var key = "counter";

        // Act
        var v1 = variables.Set(key, 1);
        var v2 = v1.Set(key, 2);
        var v3 = v2.Set(key, 3);

        // Assert
        Assert.Equal(3, v3.Get<int>(key));
    }

    [Fact]
    public void ShouldNotMutateOriginal_WhenSettingReturnsNewInstance()
    {
        // Arrange
        var original = CrewVariables.Empty;

        // Act
        var updated = original.Set("key", "value");

        // Assert
        Assert.Null(original.GetString("key"));
        Assert.Equal("value", updated.GetString("key"));
    }

    #region Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoVariablesWithSameStringValues()
    {
        // Arrange
        var vars1 = CrewVariables.Empty.Set("name", "Alice");
        var vars2 = CrewVariables.Empty.Set("name", "Alice");

        // Act & Assert
        Assert.Equal(vars1, vars2);
        Assert.True(vars1.Equals(vars2));
        Assert.True(vars1 == vars2);
        Assert.False(vars1 != vars2);
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoVariablesWithSameMixedValues()
    {
        // Arrange
        var vars1 = CrewVariables.Empty
            .Set("name", "Test")
            .Set("count", 10)
            .Set("active", true)
            .Set("rate", 2.5);

        var vars2 = CrewVariables.Empty
            .Set("name", "Test")
            .Set("count", 10)
            .Set("active", true)
            .Set("rate", 2.5);

        // Act & Assert
        Assert.Equal(vars1, vars2);
        Assert.True(vars1 == vars2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoVariablesWithDifferentStringValues()
    {
        // Arrange
        var vars1 = CrewVariables.Empty.Set("name", "Alice");
        var vars2 = CrewVariables.Empty.Set("name", "Bob");

        // Act & Assert
        Assert.NotEqual(vars1, vars2);
        Assert.True(vars1 != vars2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoVariablesWithDifferentIntValues()
    {
        // Arrange
        var vars1 = CrewVariables.Empty.Set("count", 10);
        var vars2 = CrewVariables.Empty.Set("count", 20);

        // Act & Assert
        Assert.NotEqual(vars1, vars2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoVariablesWithDifferentBoolValues()
    {
        // Arrange
        var vars1 = CrewVariables.Empty.Set("flag", true);
        var vars2 = CrewVariables.Empty.Set("flag", false);

        // Act & Assert
        Assert.NotEqual(vars1, vars2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoVariablesWithDifferentDoubleValues()
    {
        // Arrange
        var vars1 = CrewVariables.Empty.Set("rate", 1.0);
        var vars2 = CrewVariables.Empty.Set("rate", 2.0);

        // Act & Assert
        Assert.NotEqual(vars1, vars2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoVariablesWithDifferentCounts()
    {
        // Arrange
        var vars1 = CrewVariables.Empty.Set("name", "Test");
        var vars2 = CrewVariables.Empty.Set("name", "Test").Set("extra", "val");

        // Act & Assert
        Assert.NotEqual(vars1, vars2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingWithNull()
    {
        // Arrange
        var vars = CrewVariables.Empty.Set("key", "value");

        // Act & Assert
        Assert.False(vars.Equals(null));
        Assert.NotNull(vars);
    }

    [Fact]
    public void ShouldReturnTrue_WhenBothOperandsAreNull()
    {
        // Arrange
        CrewVariables? left = null;
        CrewVariables? right = null;

        // Act & Assert
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingWithSameReference()
    {
        // Arrange
        var vars = CrewVariables.Empty.Set("key", "value");

        // Act & Assert
        Assert.True(vars.Equals(vars));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingWithDifferentObjectType()
    {
        // Arrange
        var vars = CrewVariables.Empty.Set("key", "value");

        // Act & Assert
        Assert.False(vars.Equals("not a CrewVariables"));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoVariablesAreEqual()
    {
        // Arrange
        var vars1 = CrewVariables.Empty
            .Set("name", "Test")
            .Set("count", 42)
            .Set("active", true)
            .Set("score", 9.5);

        var vars2 = CrewVariables.Empty
            .Set("name", "Test")
            .Set("count", 42)
            .Set("active", true)
            .Set("score", 9.5);

        // Act & Assert
        Assert.Equal(vars1.GetHashCode(), vars2.GetHashCode());
    }

    [Fact]
    public void ShouldHaveDifferentHashCode_WhenTwoVariablesAreDifferent()
    {
        // Arrange
        var vars1 = CrewVariables.Empty.Set("name", "Alice");
        var vars2 = CrewVariables.Empty.Set("name", "Bob");

        // Act & Assert
        Assert.NotEqual(vars1.GetHashCode(), vars2.GetHashCode());
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoEmptyVariables()
    {
        // Arrange
        var vars1 = CrewVariables.Empty;
        var vars2 = CrewVariables.Empty;

        // Act & Assert
        Assert.Equal(vars1, vars2);
        Assert.Equal(vars1.GetHashCode(), vars2.GetHashCode());
    }

    #endregion
}
