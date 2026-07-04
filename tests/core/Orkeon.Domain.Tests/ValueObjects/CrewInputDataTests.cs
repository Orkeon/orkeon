using Orkeon.Domain.Crew.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class CrewInputDataTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var data = CrewInputData.Empty;

        // Assert
        Assert.NotNull(data);
        Assert.Equal(0, data.Count);
        Assert.Empty(data.Keys);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingGetValueWithExistingKey()
    {
        // Arrange
        var data = CrewInputData.Empty
            .SetValue("name", "John")
            .SetValue("age", 30)
            .SetValue("active", true);

        // Act & Assert
        Assert.Equal("John", data.GetValue<string>("name"));
        Assert.Equal(30, data.GetValue<int>("age"));
        Assert.True(data.GetValue<bool>("active"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenUsingGetValueWithNonExistentKey()
    {
        // Arrange
        var data = CrewInputData.Empty;

        // Act & Assert
        Assert.Null(data.GetValue<string>("missing"));
        Assert.Equal(0, data.GetValue<int>("missing"));
        Assert.False(data.GetValue<bool>("missing"));
    }

    [Fact]
    public void ShouldReturnRawValue_WhenGettingObjectWithExistingKey()
    {
        // Arrange
        var complexObject = new { Name = "Test", Value = 123 };
        var data = CrewInputData.Empty.SetValue("complex", complexObject);

        // Act
        var result = data.GetObject("complex");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(complexObject, result);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingObjectWithNonExistentKey()
    {
        // Arrange
        var data = CrewInputData.Empty;

        // Act
        var result = data.GetObject("missing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingHasValue()
    {
        // Arrange
        var data = CrewInputData.Empty.SetValue("existing", "value");

        // Act & Assert
        Assert.True(data.HasValue("existing"));
        Assert.False(data.HasValue("missing"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithValue_WhenUsingSetValue()
    {
        // Arrange
        var original = CrewInputData.Empty;

        // Act
        var modified = original.SetValue("key", "value");

        // Assert
        Assert.Equal(0, original.Count); // Original unchanged
        Assert.Equal(1, modified.Count);
        Assert.Equal("value", modified.GetValue<string>("key"));
    }

    [Fact]
    public void ShouldUpdateValue_WhenUsingSetValueWithExistingKey()
    {
        // Arrange
        var data = CrewInputData.Empty.SetValue("key", "original");

        // Act
        var updated = data.SetValue("key", "updated");

        // Assert
        Assert.Equal("original", data.GetValue<string>("key")); // Original unchanged
        Assert.Equal("updated", updated.GetValue<string>("key"));
        Assert.Equal(1, updated.Count);
    }

    [Fact]
    public void ShouldAddAllValues_WhenSettingMultiple()
    {
        // Arrange
        var items = new List<KeyValuePair<string, object>>
        {
            new("key1", "value1"),
            new("key2", 42),
            new("key3", true)
        };

        // Act
        var data = CrewInputData.Empty.SetMultiple(items);

        // Assert
        Assert.Equal(3, data.Count);
        Assert.Equal("value1", data.GetValue<string>("key1"));
        Assert.Equal(42, data.GetValue<int>("key2"));
        Assert.True(data.GetValue<bool>("key3"));
    }

    [Fact]
    public void ShouldUpdateValues_WhenSettingMultipleWithExistingKeys()
    {
        // Arrange
        var original = CrewInputData.Empty
            .SetValue("key1", "original1")
            .SetValue("key2", "original2");

        var updates = new List<KeyValuePair<string, object>>
        {
            new("key2", "updated2"),
            new("key3", "new3")
        };

        // Act
        var updated = original.SetMultiple(updates);

        // Assert
        Assert.Equal(3, updated.Count);
        Assert.Equal("original1", updated.GetValue<string>("key1")); // Unchanged
        Assert.Equal("updated2", updated.GetValue<string>("key2")); // Updated
        Assert.Equal("new3", updated.GetValue<string>("key3")); // New
    }

    [Fact]
    public void ShouldRemoveValue_WhenRemovingWithExistingKey()
    {
        // Arrange
        var data = CrewInputData.Empty
            .SetValue("key1", "value1")
            .SetValue("key2", "value2");

        // Act
        var removed = data.Remove("key1");

        // Assert
        Assert.Equal(2, data.Count); // Original unchanged
        Assert.Equal(1, removed.Count);
        Assert.False(removed.HasValue("key1"));
        Assert.True(removed.HasValue("key2"));
    }

    [Fact]
    public void ShouldReturnUnchanged_WhenRemovingWithNonExistentKey()
    {
        // Arrange
        var data = CrewInputData.Empty.SetValue("key", "value");

        // Act
        var result = data.Remove("nonexistent");

        // Assert
        Assert.Equal(1, result.Count);
        Assert.True(result.HasValue("key"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var data = CrewInputData.Empty
            .SetValue("key1", "value1")
            .SetValue("key2", "value2")
            .SetValue("key3", "value3");

        // Act
        var keys = data.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingToDictionary()
    {
        // Arrange
        var data = CrewInputData.Empty
            .SetValue("string", "text")
            .SetValue("number", 42)
            .SetValue("boolean", true);

        // Act
        var dict = data.ToDictionary();

        // Assert
        Assert.Equal(3, dict.Count);
        Assert.Equal("text", dict["string"]);
        Assert.Equal(42, dict["number"]);
        Assert.True((bool)dict["boolean"]);
    }

    [Fact]
    public void ShouldCreateEmptyBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = CrewInputData.CreateBuilder();
        var data = builder.Build();

        // Assert
        Assert.NotNull(data);
        Assert.Equal(0, data.Count);
    }

    [Fact]
    public void ShouldAddPromptKey_WhenUsingBuilderAddPrompt()
    {
        // Act
        var data = CrewInputData.CreateBuilder()
            .AddPrompt(TestPrompt)
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        Assert.Equal(TestPrompt, data.GetValue<string>("prompt"));
    }

    [Fact]
    public void ShouldAddContextKey_WhenUsingBuilderAddContext()
    {
        // Arrange
        var context = new { Data = "test", Value = 123 };

        // Act
        var data = CrewInputData.CreateBuilder()
            .AddContext(context)
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        Assert.Equal(context, data.GetObject("context"));
    }

    [Fact]
    public void ShouldAddGoalKey_WhenUsingBuilderAddGoal()
    {
        // Act
        var data = CrewInputData.CreateBuilder()
            .AddGoal("Complete the task")
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        Assert.Equal("Complete the task", data.GetValue<string>("goal"));
    }

    [Fact]
    public void ShouldAddConstraintsKey_WhenUsingBuilderAddConstraints()
    {
        // Arrange
        var constraints = new List<string> { "Constraint 1", "Constraint 2" };

        // Act
        var data = CrewInputData.CreateBuilder()
            .AddConstraints(constraints)
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        var result = data.GetObject("constraints") as List<string>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ShouldAddMaxIterationsKey_WhenUsingBuilderAddMaxIterations()
    {
        // Act
        var data = CrewInputData.CreateBuilder()
            .AddMaxIterations(10)
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        Assert.Equal(10, data.GetValue<int>("max_iterations"));
    }

    [Fact]
    public void ShouldAddTimeLimitKey_WhenUsingBuilderAddTimeLimit()
    {
        // Arrange
        var timeLimit = TimeSpan.FromMinutes(30);

        // Act
        var data = CrewInputData.CreateBuilder()
            .AddTimeLimit(timeLimit)
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        Assert.Equal(timeLimit, data.GetObject("time_limit"));
    }

    [Fact]
    public void ShouldAddMemoryPrefixedKey_WhenUsingBuilderAddMemoryContext()
    {
        // Act
        var data = CrewInputData.CreateBuilder()
            .AddMemoryContext("cache", new { Hit = true, Value = "cached" })
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        Assert.True(data.HasValue("memory.cache"));
    }

    [Fact]
    public void ShouldAddCustomKey_WhenUsingBuilderAdd()
    {
        // Act
        var data = CrewInputData.CreateBuilder()
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(1, data.Count);
        Assert.Equal("value", data.GetValue<string>("custom"));
    }

    [Fact]
    public void ShouldAddMultipleValues_WhenUsingBuilderChainedCalls()
    {
        // Act
        var data = CrewInputData.CreateBuilder()
            .AddPrompt(TestPrompt)
            .AddGoal("Test goal")
            .AddMaxIterations(5)
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(4, data.Count);
        Assert.Equal(TestPrompt, data.GetValue<string>("prompt"));
        Assert.Equal("Test goal", data.GetValue<string>("goal"));
        Assert.Equal(5, data.GetValue<int>("max_iterations"));
        Assert.Equal("value", data.GetValue<string>("custom"));
    }

    [Fact]
    public void ShouldCopyExistingData_WhenCreatingBuilderFrom()
    {
        // Arrange
        var original = CrewInputData.Empty
            .SetValue("key1", "value1")
            .SetValue("key2", "value2");

        // Act
        var data = CrewInputData.CreateBuilderFrom(original)
            .Add("key3", "value3")
            .Build();

        // Assert
        Assert.Equal(3, data.Count);
        Assert.Equal("value1", data.GetValue<string>("key1"));
        Assert.Equal("value2", data.GetValue<string>("key2"));
        Assert.Equal("value3", data.GetValue<string>("key3"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNullDictionary()
    {
        // Act
        var data = CrewInputData.FromDictionary(null);

        // Assert
        Assert.Equal(0, data.Count);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var data = CrewInputData.FromDictionary([]);

        // Assert
        Assert.Equal(0, data.Count);
    }

    [Fact]
    public void ShouldCreateData_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 42 },
            { "key3", true }
        };

        // Act
        var data = CrewInputData.FromDictionary(dict);

        // Assert
        Assert.Equal(3, data.Count);
        Assert.Equal("value1", data.GetValue<string>("key1"));
        Assert.Equal(42, data.GetValue<int>("key2"));
        Assert.True(data.GetValue<bool>("key3"));
    }

    [Fact]
    public void ShouldAcceptVariousTypes_WhenUsingCrewInputValueFrom()
    {
        // Act & Assert - Should not throw
        var stringValue = CrewInputValue.From("text");
        var intValue = CrewInputValue.From(123);
        var boolValue = CrewInputValue.From(true);
        var objectValue = CrewInputValue.From(new { Test = "value" });

        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(objectValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingCrewInputValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CrewInputValue.From(null!));
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingCrewInputValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = CrewInputValue.From("test");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingCrewInputValueGettingValueWithConvertibleType()
    {
        // Arrange
        var value = CrewInputValue.From(123);

        // Act
        var asString = value.GetValue<string>();
        var asDouble = value.GetValue<double>();

        // Assert
        Assert.Equal("123", asString);
        Assert.Equal(123.0, asDouble);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingCrewInputValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = CrewInputValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert crew input value", exception.Message);
    }
}
