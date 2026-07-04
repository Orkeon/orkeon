using Orkeon.Application.Memory;

namespace Orkeon.Application.Tests.Common;

public class MemorySearchMetadataTests
{
    private static readonly string[] s_financeTags = ["finance", "quarterly_report", "2024"];
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var metadata = MemorySearchMetadata.Empty;

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
        var metadata = MemorySearchMetadata.CreateBuilder()
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
        var metadata = MemorySearchMetadata.Empty;

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
        var metadata = MemorySearchMetadata.CreateBuilder()
            .Add("intValue", 123)
            .Build();

        // Act
        var stringValue = metadata.Get<string>("intValue");

        // Assert
        Assert.Equal("123", stringValue);
    }

    [Fact]
    public void ShouldReturnStringValue_WhenGettingStringWithExistingKey()
    {
        // Arrange
        var metadata = MemorySearchMetadata.CreateBuilder()
            .Add("stringKey", "stringValue")
            .Add("intKey", 42)
            .Add("boolKey", true)
            .Build();

        // Act
        var stringResult = metadata.GetString("stringKey");
        var intResult = metadata.GetString("intKey");
        var boolResult = metadata.GetString("boolKey");

        // Assert
        Assert.Equal("stringValue", stringResult);
        Assert.Equal("42", intResult);
        Assert.Equal("True", boolResult);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingStringWithNonExistentKey()
    {
        // Arrange
        var metadata = MemorySearchMetadata.Empty;

        // Act
        var result = metadata.GetString("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var metadata = MemorySearchMetadata.CreateBuilder()
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
        var metadata = MemorySearchMetadata.CreateBuilder()
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
        var empty = MemorySearchMetadata.Empty;
        var withItems = MemorySearchMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", "value2")
            .Add("key3", "value3")
            .Build();

        // Act & Assert
        Assert.Equal(0, empty.Count);
        Assert.Equal(3, withItems.Count);
    }

    [Fact]
    public void ShouldReturnAllMetadata_WhenUsingToDictionary()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var metadata = MemorySearchMetadata.CreateBuilder()
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
    public void ShouldConvertAllValuesToStrings_WhenUsingToStringDictionary()
    {
        // Arrange
        var metadata = MemorySearchMetadata.CreateBuilder()
            .Add("string", "value")
            .Add("int", 42)
            .Add("float", 3.14f)
            .Add("bool", true)
            .Add("date", new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))
            .Build();

        // Act
        var dictionary = metadata.ToStringDictionary();

        // Assert
        Assert.Equal(5, dictionary.Count);
        Assert.Equal("value", dictionary["string"]);
        Assert.Equal("42", dictionary["int"]);
        Assert.Equal("3.14", dictionary["float"]);
        Assert.Equal("True", dictionary["bool"]);
        Assert.Contains("2024", dictionary["date"]);
    }

    [Fact]
    public void ShouldCreateEmptyBuilder_WhenUsingCreateBuilder()
    {
        // Act
        var builder = MemorySearchMetadata.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
        var metadata = builder.Build();
        Assert.Empty(metadata.Keys);
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddSource()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddSource("database")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("source"));
        Assert.Equal("database", metadata.Get<string>("source"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddRelevance()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddRelevance(0.85f)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("relevance"));
        Assert.Equal(0.85f, metadata.Get<float>("relevance"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddAccessCount()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddAccessCount(42)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("accessCount"));
        Assert.Equal(42, metadata.Get<int>("accessCount"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddType()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddType("semantic")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("type"));
        Assert.Equal("semantic", metadata.Get<string>("type"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddAgentId()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddAgentId("agent-123")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("agent_id"));
        Assert.Equal("agent-123", metadata.Get<string>("agent_id"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddContext()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddContext("user-interaction")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("context"));
        Assert.Equal("user-interaction", metadata.Get<string>("context"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddCreatedAt()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;

        // Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddCreatedAt(createdAt)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("created_at"));
        Assert.Equal(createdAt, metadata.Get<DateTime>("created_at"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddScore()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddScore(0.92f)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("score"));
        Assert.Equal(0.92f, metadata.Get<float>("score"));
    }

    [Fact]
    public void ShouldMaintainFluency_WhenUsingBuilderChainedOperations()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;

        // Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddSource("vector_db")
            .AddRelevance(0.89f)
            .AddAccessCount(10)
            .AddType("episodic")
            .AddAgentId("agent-456")
            .AddContext("task-execution")
            .AddCreatedAt(createdAt)
            .AddScore(0.95f)
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(9, metadata.Count);
        Assert.Equal("vector_db", metadata.Get<string>("source"));
        Assert.Equal(0.89f, metadata.Get<float>("relevance"));
        Assert.Equal(10, metadata.Get<int>("accessCount"));
        Assert.Equal("episodic", metadata.Get<string>("type"));
        Assert.Equal("agent-456", metadata.Get<string>("agent_id"));
        Assert.Equal("task-execution", metadata.Get<string>("context"));
        Assert.Equal(createdAt, metadata.Get<DateTime>("created_at"));
        Assert.Equal(0.95f, metadata.Get<float>("score"));
        Assert.Equal("value", metadata.Get<string>("custom"));
    }

    [Fact]
    public void ShouldCreateMetadata_WhenUsingFromDictionaryWithValidDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 42 },
            { "key3", new TestClass { Name = "Test" } }
        };

        // Act
        var metadata = MemorySearchMetadata.FromDictionary(dictionary);

        // Assert
        Assert.Equal(3, metadata.Count);
        Assert.Equal("value1", metadata.Get<string>("key1"));
        Assert.Equal(42, metadata.Get<int>("key2"));
        Assert.NotNull(metadata.Get<TestClass>("key3"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNullDictionary()
    {
        // Act
        var metadata = MemorySearchMetadata.FromDictionary(null);

        // Assert
        Assert.Same(MemorySearchMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var metadata = MemorySearchMetadata.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Same(MemorySearchMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingMemorySearchMetadataValueFromWithNullValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            MemorySearchMetadataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingMemorySearchMetadataValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = MemorySearchMetadataValue.From("test");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingMemorySearchMetadataValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = MemorySearchMetadataValue.From("test");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() =>
            value.GetValue<TestClass>());
        Assert.Contains("Cannot convert memory search metadata value", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenUsingMemorySearchMetadataValueUsingProperties()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var value = MemorySearchMetadataValue.From(testObject);

        // Act & Assert
        Assert.Same(testObject, value.RawValue);
        Assert.Equal(typeof(TestClass), value.ValueType);
    }

    [Fact]
    public void ShouldUpdateValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Arrange & Act
        var metadata = MemorySearchMetadata.CreateBuilder()
            .Add("key", "original")
            .Add("key", "updated")
            .Build();

        // Assert
        Assert.Equal("updated", metadata.Get<string>("key"));
    }

    [Fact]
    public void ShouldMaintain_WhenUsingMetadataImmutability()
    {
        // Arrange
        var original = MemorySearchMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        // Act
        var builder = MemorySearchMetadata.CreateBuilder();
        builder.Add("key1", "modified");
        builder.Add("key2", "value2");
        var modified = builder.Build();

        // Assert
        Assert.Equal("value1", original.Get<string>("key1"));
        Assert.False(original.ContainsKey("key2"));
        Assert.Equal("modified", modified.Get<string>("key1"));
        Assert.Equal("value2", modified.Get<string>("key2"));
    }

    [Fact]
    public void ShouldMemorySearchResult_WhenUsingComplexScenario()
    {
        // Arrange - Simulate a complex memory search result
        var metadata = MemorySearchMetadata.CreateBuilder()
            .AddSource("long_term_memory")
            .AddType("episodic")
            .AddRelevance(0.89f)
            .AddScore(0.91f)
            .AddAccessCount(15)
            .AddAgentId("agent-research-01")
            .AddContext("market_analysis_task")
            .AddCreatedAt(DateTime.UtcNow.AddDays(-7))
            .Add("tags", s_financeTags)
            .Add("confidence", 0.95f)
            .Add("retrieval_time_ms", 125)
            .Build();

        // Act - Convert to different formats
        var dictionary = metadata.ToDictionary();
        var stringDict = metadata.ToStringDictionary();

        // Assert - Verify all metadata preserved correctly
        Assert.Equal(11, metadata.Count);

        // Verify specific values
        Assert.Equal("long_term_memory", metadata.Get<string>("source"));
        Assert.Equal(0.89f, metadata.Get<float>("relevance"));
        Assert.Equal(15, metadata.Get<int>("accessCount"));

        // Verify array conversion
        var tags = metadata.Get<string[]>("tags");
        Assert.NotNull(tags);
        Assert.Equal(3, tags.Length);
        Assert.Contains("finance", tags);

        // Verify string dictionary conversion
        Assert.Equal("0.95", stringDict["confidence"]);
        Assert.Equal("125", stringDict["retrieval_time_ms"]);

        // Verify GetString method
        Assert.Equal("episodic", metadata.GetString("type"));
        Assert.Equal("0.91", metadata.GetString("score"));
    }

    // Helper class for testing
    private class TestClass
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
