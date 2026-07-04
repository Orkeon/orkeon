using Orkeon.Domain.Crew.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class CrewMetadataTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var metadata = CrewMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(0, metadata.Count);
        Assert.Empty(metadata.Keys);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Add("key3", true)
            .Build();

        // Act & Assert
        Assert.Equal("value1", metadata.Get<string>("key1"));
        Assert.Equal(42, metadata.Get<int>("key2"));
        Assert.True(metadata.Get<bool>("key3"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var metadata = CrewMetadata.Empty;

        // Act & Assert
        Assert.Null(metadata.Get<string>("missing"));
        Assert.Equal(0, metadata.Get<int>("missing"));
        Assert.False(metadata.Get<bool>("missing"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingKey()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("required", "value")
            .Build();

        // Act
        var result = metadata.GetRequired<string>("required");

        // Assert
        Assert.Equal("value", result);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var metadata = CrewMetadata.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => metadata.GetRequired<string>("missing"));
        Assert.Contains("Required metadata 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContains()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("existing", "value")
            .Build();

        // Act & Assert
        Assert.True(metadata.Contains("existing"));
        Assert.False(metadata.Contains("missing"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", "value2")
            .Add("key3", "value3")
            .Build();

        // Act
        var keys = metadata.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    [Fact]
    public void ShouldReturnCorrectNumber_WhenCounting()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", "value2")
            .Build();

        // Act & Assert
        Assert.Equal(2, metadata.Count);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNullDictionary()
    {
        // Act
        var metadata = CrewMetadata.FromDictionary(null!);

        // Assert
        Assert.Equal(0, metadata.Count);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var metadata = CrewMetadata.FromDictionary([]);

        // Assert
        Assert.Equal(0, metadata.Count);
    }

    [Fact]
    public void ShouldCreateMetadata_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 42 },
            { "key3", true }
        };

        // Act
        var metadata = CrewMetadata.FromDictionary(dict);

        // Assert
        Assert.Equal(3, metadata.Count);
        Assert.Equal("value1", metadata.Get<string>("key1"));
        Assert.Equal(42, metadata.Get<int>("key2"));
        Assert.True(metadata.Get<bool>("key3"));
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingToDictionary()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("string", "text")
            .Add("number", 42)
            .Add("boolean", true)
            .Build();

        // Act
        var dict = metadata.ToDictionary();

        // Assert
        Assert.Equal(3, dict.Count);
        Assert.Equal("text", dict["string"]);
        Assert.Equal(42, dict["number"]);
        Assert.True((bool)dict["boolean"]);
    }

    [Fact]
    public void ShouldAddExecutionIdKey_WhenUsingBuilderAddExecutionId()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutionId("exec-123")
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal("exec-123", metadata.Get<string>("executionId"));
    }

    [Fact]
    public void ShouldAddExecutorAgentKey_WhenUsingBuilderAddExecutorAgent()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutorAgent("agent-456")
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal("agent-456", metadata.Get<string>("executorAgent"));
    }

    [Fact]
    public void ShouldAddMemoryUsedKey_WhenUsingBuilderAddMemoryUsed()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddMemoryUsed("Redis")
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal("Redis", metadata.Get<string>("memoryUsed"));
    }

    [Fact]
    public void ShouldAddLlmProviderKey_WhenUsingBuilderAddLlmProvider()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddLlmProvider("OpenAI")
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal("OpenAI", metadata.Get<string>("llmProvider"));
    }

    [Fact]
    public void ShouldAddTotalTokensKey_WhenUsingBuilderAddTotalTokens()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddTotalTokens(1500)
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal(1500, metadata.Get<int>("totalTokens"));
    }

    [Fact]
    public void ShouldAddTotalCostKey_WhenUsingBuilderAddTotalCost()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddTotalCost(2.35)
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal(2.35, metadata.Get<double>("totalCost"));
    }

    [Fact]
    public void ShouldAddTagPrefixedKey_WhenUsingBuilderAddTag()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddTag("production")
            .AddTag("urgent")
            .Build();

        // Assert
        Assert.Equal(2, metadata.Count);
        Assert.True(metadata.Get<bool>("tag_production"));
        Assert.True(metadata.Get<bool>("tag_urgent"));
    }

    [Fact]
    public void ShouldAddTimestampKey_WhenUsingBuilderAddTimestamp()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddTimestamp("startedAt", timestamp)
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal(timestamp, metadata.Get<DateTime>("startedAt"));
    }

    [Fact]
    public void ShouldAddDurationKey_WhenUsingBuilderAddDuration()
    {
        // Arrange
        var duration = TimeoutStandard;

        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddDuration("executionTime", duration)
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal(duration, metadata.Get<TimeSpan>("executionTime"));
    }

    [Fact]
    public void ShouldAddCustomKey_WhenUsingBuilderAdd()
    {
        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(1, metadata.Count);
        Assert.Equal("value", metadata.Get<string>("custom"));
    }

    [Fact]
    public void ShouldAddMultipleValues_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var duration = TimeoutQuick;

        // Act
        var metadata = CrewMetadata.CreateBuilder()
            .AddExecutionId("exec-123")
            .AddExecutorAgent("agent-456")
            .AddMemoryUsed("InMemory")
            .AddLlmProvider("Ollama")
            .AddTotalTokens(1000)
            .AddTotalCost(0.5)
            .AddTag("test")
            .AddTimestamp("createdAt", timestamp)
            .AddDuration("duration", duration)
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(10, metadata.Count);
        Assert.Equal("exec-123", metadata.Get<string>("executionId"));
        Assert.Equal("agent-456", metadata.Get<string>("executorAgent"));
        Assert.Equal("InMemory", metadata.Get<string>("memoryUsed"));
        Assert.Equal("Ollama", metadata.Get<string>("llmProvider"));
        Assert.Equal(1000, metadata.Get<int>("totalTokens"));
        Assert.Equal(0.5, metadata.Get<double>("totalCost"));
        Assert.True(metadata.Get<bool>("tag_test"));
        Assert.Equal(timestamp, metadata.Get<DateTime>("createdAt"));
        Assert.Equal(duration, metadata.Get<TimeSpan>("duration"));
        Assert.Equal("value", metadata.Get<string>("custom"));
    }

    [Fact]
    public void ShouldAcceptVariousTypes_WhenUsingCrewMetadataValueFrom()
    {
        // Act & Assert - Should not throw
        var stringValue = CrewMetadataValue.From("text");
        var intValue = CrewMetadataValue.From(123);
        var boolValue = CrewMetadataValue.From(true);
        var dateValue = CrewMetadataValue.From(DateTime.UtcNow);
        var objectValue = CrewMetadataValue.From(new { Test = "value" });

        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(dateValue);
        Assert.NotNull(objectValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingCrewMetadataValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CrewMetadataValue.From(null!));
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingCrewMetadataValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = CrewMetadataValue.From("test");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingCrewMetadataValueGettingValueWithConvertibleType()
    {
        // Arrange
        var value = CrewMetadataValue.From(123);

        // Act
        var asString = value.GetValue<string>();
        var asDouble = value.GetValue<double>();

        // Assert
        Assert.Equal("123", asString);
        Assert.Equal(123.0, asDouble);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingCrewMetadataValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = CrewMetadataValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert crew metadata value", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingCrewMetadataValueUsingRawValue()
    {
        // Arrange
        var original = new { Name = "Test", Value = 123 };
        var value = CrewMetadataValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Equal(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingCrewMetadataValueUsingValueType()
    {
        // Arrange
        var stringValue = CrewMetadataValue.From("test");
        var intValue = CrewMetadataValue.From(123);
        var boolValue = CrewMetadataValue.From(true);

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(bool), boolValue.ValueType);
    }

    [Fact]
    public void ShouldCreateImmutableValues_WhenUsingCrewMetadataUsingConstructor()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("key", "value")
            .Build();

        // Attempt to get the underlying dictionary (should not be possible)
        var dict = metadata.ToDictionary();
        dict["key"] = "modified"; // Modify the returned dictionary

        // Act & Assert - Original should be unchanged
        Assert.Equal("value", metadata.Get<string>("key"));
    }

    #region Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoMetadataWithSameValues()
    {
        // Arrange
        var metadata1 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Build();

        var metadata2 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Build();

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1.Equals(metadata2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoMetadataWithDifferentValues()
    {
        // Arrange
        var metadata1 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        var metadata2 = CrewMetadata.CreateBuilder()
            .Add("key1", "value2")
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
        Assert.False(metadata1.Equals(metadata2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoMetadataWithDifferentKeys()
    {
        // Arrange
        var metadata1 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        var metadata2 = CrewMetadata.CreateBuilder()
            .Add("key2", "value1")
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoMetadataWithDifferentCounts()
    {
        // Arrange
        var metadata1 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        var metadata2 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", "value2")
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingWithNull()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        // Act & Assert
        Assert.False(metadata.Equals(null));
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingWithSameReference()
    {
        // Arrange
        var metadata = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        // Act & Assert
        Assert.True(metadata.Equals(metadata));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoMetadataAreEqual()
    {
        // Arrange
        var metadata1 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Build();

        var metadata2 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Build();

        // Act & Assert
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldHaveDifferentHashCode_WhenTwoMetadataAreDifferent()
    {
        // Arrange
        var metadata1 = CrewMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        var metadata2 = CrewMetadata.CreateBuilder()
            .Add("key1", "value2")
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldBeEqualForEmpty_WhenComparingTwoEmptyMetadata()
    {
        // Arrange
        var metadata1 = CrewMetadata.Empty;
        var metadata2 = CrewMetadata.Empty;

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    #endregion

    #region CrewMetadataValue Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoCrewMetadataValuesWithSameContent()
    {
        // Arrange
        var value1 = CrewMetadataValue.From("test");
        var value2 = CrewMetadataValue.From("test");

        // Act & Assert
        Assert.Equal(value1, value2);
        Assert.True(value1.Equals(value2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoCrewMetadataValuesWithDifferentContent()
    {
        // Arrange
        var value1 = CrewMetadataValue.From("test1");
        var value2 = CrewMetadataValue.From("test2");

        // Act & Assert
        Assert.NotEqual(value1, value2);
        Assert.False(value1.Equals(value2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingCrewMetadataValueWithNull()
    {
        // Arrange
        var value = CrewMetadataValue.From("test");

        // Act & Assert
        Assert.False(value.Equals(null));
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingCrewMetadataValueWithSameReference()
    {
        // Arrange
        var value = CrewMetadataValue.From("test");

        // Act & Assert
        Assert.True(value.Equals(value));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoCrewMetadataValuesAreEqual()
    {
        // Arrange
        var value1 = CrewMetadataValue.From(42);
        var value2 = CrewMetadataValue.From(42);

        // Act & Assert
        Assert.Equal(value1.GetHashCode(), value2.GetHashCode());
    }

    #endregion
}
