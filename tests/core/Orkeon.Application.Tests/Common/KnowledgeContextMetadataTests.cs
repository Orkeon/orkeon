using Orkeon.Application.Rag;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Application.Tests.Common;

public class KnowledgeContextMetadataTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var metadata = KnowledgeContextMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Keys);
        Assert.Equal(0, metadata.Count);
        Assert.Empty(metadata.ToDictionary());
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Build();

        // Act
        var stringValue = metadata.Get<string>("key1");
        var intValue = metadata.Get<int>("key2");

        // Assert
        Assert.Equal("value1", stringValue);
        Assert.Equal(42, intValue);
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var metadata = KnowledgeContextMetadata.Empty;

        // Act
        var stringResult = metadata.Get<string>("nonexistent");
        var intResult = metadata.Get<int>("nonexistent");
        var objectResult = metadata.Get<object>("nonexistent");

        // Assert
        Assert.Null(stringResult);
        Assert.Equal(0, intResult);
        Assert.Null(objectResult);
    }

    [Fact]
    public void ShouldAttemptConversion_WhenGettingWithTypeConversion()
    {
        // Arrange
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .Add("intValue", 123)
            .Build();

        // Act
        var stringValue = metadata.Get<string>("intValue");

        // Assert
        Assert.Equal("123", stringValue);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .Add("existing", "value")
            .Build();

        // Act & Assert
        Assert.True(metadata.ContainsKey("existing"));
        Assert.False(metadata.ContainsKey("nonexistent"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var metadata = KnowledgeContextMetadata.CreateBuilder()
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
        var empty = KnowledgeContextMetadata.Empty;
        var withItems = KnowledgeContextMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", "value2")
            .Add("key3", "value3")
            .Build();

        // Act & Assert
        Assert.Equal(0, empty.Count);
        Assert.Equal(3, withItems.Count);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithAddedValue_WhenSetting()
    {
        // Arrange
        var original = KnowledgeContextMetadata.Empty;

        // Act
        var updated = original.Set("key", "value");

        // Assert
        Assert.NotSame(original, updated);
        Assert.Equal(0, original.Count);
        Assert.Equal(1, updated.Count);
        Assert.True(updated.ContainsKey("key"));
        Assert.Equal("value", updated.Get<string>("key"));
    }

    [Fact]
    public void ShouldUpdateValue_WhenSettingWithExistingKey()
    {
        // Arrange
        var original = KnowledgeContextMetadata.CreateBuilder()
            .Add("key", "original")
            .Build();

        // Act
        var updated = original.Set("key", "updated");

        // Assert
        Assert.Equal("original", original.Get<string>("key"));
        Assert.Equal("updated", updated.Get<string>("key"));
        Assert.Equal(1, updated.Count);
    }

    [Fact]
    public void ShouldMaintainImmutability_WhenSettingWithMultipleCalls()
    {
        // Arrange
        var original = KnowledgeContextMetadata.Empty;

        // Act
        var first = original.Set("key1", "value1");
        var second = first.Set("key2", "value2");
        var third = second.Set("key1", "updatedValue1");

        // Assert
        Assert.Equal(0, original.Count);
        Assert.Equal(1, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(2, third.Count);

        Assert.Equal("value1", first.Get<string>("key1"));
        Assert.Equal("updatedValue1", third.Get<string>("key1"));
        Assert.Equal("value2", third.Get<string>("key2"));
    }

    [Fact]
    public void ShouldReturnAllMetadata_WhenUsingToDictionary()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Add("key3", testObject)
            .Build();

        // Act
        var dictionary = metadata.ToDictionary();

        // Assert
        Assert.Equal(3, dictionary.Count);
        Assert.Equal("value1", dictionary["key1"]);
        Assert.Equal(42, dictionary["key2"]);
        Assert.IsType<TestClass>(dictionary["key3"]);
    }

    [Fact]
    public void ShouldCreateEmptyBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = KnowledgeContextMetadata.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
        var metadata = builder.Build();
        Assert.Empty(metadata.Keys);
    }

    [Fact]
    public void ShouldCopyExistingMetadata_WhenCreatingBuilderFrom()
    {
        // Arrange
        var original = KnowledgeContextMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", "value2")
            .Build();

        // Act
        var builder = KnowledgeContextMetadata.CreateBuilderFrom(original);
        var metadata = builder
            .Add("key3", "value3")
            .Build();

        // Assert
        Assert.Equal(3, metadata.Count);
        Assert.Equal("value1", metadata.Get<string>("key1"));
        Assert.Equal("value2", metadata.Get<string>("key2"));
        Assert.Equal("value3", metadata.Get<string>("key3"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddQuery()
    {
        // Arrange & Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddQuery("test query")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey(ParamQuery));
        Assert.Equal("test query", metadata.Get<string>(ParamQuery));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddSource()
    {
        // Arrange & Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddSource("database")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("source"));
        Assert.Equal("database", metadata.Get<string>("source"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddTimestamp()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddTimestamp(timestamp)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("timestamp"));
        Assert.Equal(timestamp, metadata.Get<DateTime>("timestamp"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddItemCount()
    {
        // Arrange & Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddItemCount(25)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("item_count"));
        Assert.Equal(25, metadata.Get<int>("item_count"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddAverageScore()
    {
        // Arrange & Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddAverageScore(0.85f)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("average_score"));
        Assert.Equal(0.85f, metadata.Get<float>("average_score"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddContextType()
    {
        // Arrange & Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddContextType("semantic")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("context_type"));
        Assert.Equal("semantic", metadata.Get<string>("context_type"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddAgentId()
    {
        // Arrange & Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddAgentId("agent-123")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("agent_id"));
        Assert.Equal("agent-123", metadata.Get<string>("agent_id"));
    }

    [Fact]
    public void ShouldMaintainFluency_WhenUsingBuilderChainedOperations()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddQuery("test query")
            .AddSource("vector_db")
            .AddTimestamp(timestamp)
            .AddItemCount(10)
            .AddAverageScore(0.75f)
            .AddContextType("hybrid")
            .AddAgentId("agent-456")
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(8, metadata.Count);
        Assert.Equal("test query", metadata.Get<string>(ParamQuery));
        Assert.Equal("vector_db", metadata.Get<string>("source"));
        Assert.Equal(timestamp, metadata.Get<DateTime>("timestamp"));
        Assert.Equal(10, metadata.Get<int>("item_count"));
        Assert.Equal(0.75f, metadata.Get<float>("average_score"));
        Assert.Equal("hybrid", metadata.Get<string>("context_type"));
        Assert.Equal("agent-456", metadata.Get<string>("agent_id"));
        Assert.Equal("value", metadata.Get<string>("custom"));
    }

    [Fact]
    public void ShouldUpdateValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Arrange & Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .Add("key", "original")
            .Add("key", "updated")
            .Build();

        // Assert
        Assert.Equal("updated", metadata.Get<string>("key"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingKnowledgeMetadataValueFromWithNullValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            KnowledgeMetadataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingKnowledgeMetadataValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = KnowledgeMetadataValue.From("test");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingKnowledgeMetadataValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = KnowledgeMetadataValue.From("test");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() =>
            value.GetValue<TestClass>());
        Assert.Contains("Cannot convert knowledge metadata value", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenUsingKnowledgeMetadataValueUsingProperties()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var value = KnowledgeMetadataValue.From(testObject);

        // Act & Assert
        Assert.Same(testObject, value.RawValue);
        Assert.Equal(typeof(TestClass), value.ValueType);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingComplexScenarioWithVariousTypes()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var tags = new[] { "tag1", "tag2", "tag3" };
        var scores = new Dictionary<string, float> { { "relevance", 0.9f }, { "confidence", 0.8f } };

        // Act
        var metadata = KnowledgeContextMetadata.CreateBuilder()
            .AddQuery("complex query")
            .AddTimestamp(timestamp)
            .Add("tags", tags)
            .Add("scores", scores)
            .Add("processed", true)
            .Build();

        // Assert
        Assert.Equal(5, metadata.Count);

        var retrievedTags = metadata.Get<string[]>("tags");
        Assert.Equal(tags, retrievedTags);

        var retrievedScores = metadata.Get<Dictionary<string, float>>("scores");
        Assert.Equal(scores, retrievedScores);

        Assert.True(metadata.Get<bool>("processed"));
    }

    // Helper class for testing
    private class TestClass
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
