using Orkeon.Application.Execution;

namespace Orkeon.Application.Tests.Common;

public class ExecutionMetadataTests
{
    private static readonly string[] s_testProductionTags = ["test", "production"];

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var metadata = ExecutionMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Keys);
        Assert.Empty(metadata.ToDictionary());
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", 42)
            .Build();

        // Act
        var stringValue = metadata.Get<string>("key1");
        var intValue = metadata.GetRequired<int>("key2");

        // Assert
        Assert.Equal("value1", stringValue);
        Assert.Equal(42, intValue);
    }

    [Fact]
    public void ShouldReturnNull_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var metadata = ExecutionMetadata.Empty;

        // Act
        var result = metadata.Get<string>("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldAttemptConversion_WhenGettingWithTypeConversion()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateBuilder()
            .Add("intValue", 123)
            .Build();

        // Act
        var stringValue = metadata.Get<string>("intValue");

        // Assert
        Assert.Equal("123", stringValue);
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingKey()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateBuilder()
            .Add("required", "value")
            .Build();

        // Act
        var result = metadata.GetRequired<string>("required");

        // Assert
        Assert.Equal("value", result);
    }

    [Fact]
    public void ShouldThrowKeyNotFoundException_WhenGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var metadata = ExecutionMetadata.Empty;

        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(() =>
            metadata.GetRequired<string>("nonexistent"));
        Assert.Contains("Required metadata key 'nonexistent' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateBuilder()
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
        var metadata = ExecutionMetadata.CreateBuilder()
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
    public void ShouldReturnAllMetadata_WhenUsingToDictionary()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var metadata = ExecutionMetadata.CreateBuilder()
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
        var builder = ExecutionMetadata.CreateBuilder();

        // Assert
        Assert.NotNull(builder);
        var metadata = builder.Build();
        Assert.Empty(metadata.Keys);
    }

    [Fact]
    public void ShouldCopyExistingMetadata_WhenCreatingBuilderFrom()
    {
        // Arrange
        var original = ExecutionMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Add("key2", "value2")
            .Build();

        // Act
        var builder = ExecutionMetadata.CreateBuilderFrom(original);
        var metadata = builder
            .Add("key3", "value3")
            .Build();

        // Assert
        Assert.Equal(3, metadata.Keys.Count());
        Assert.Equal("value1", metadata.Get<string>("key1"));
        Assert.Equal("value2", metadata.Get<string>("key2"));
        Assert.Equal("value3", metadata.Get<string>("key3"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddIterationCount()
    {
        // Arrange & Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddIterationCount(5)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("iteration_count"));
        Assert.Equal(5, metadata.GetRequired<int>("iteration_count"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddRetryCount()
    {
        // Arrange & Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddRetryCount(3)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("retry_count"));
        Assert.Equal(3, metadata.GetRequired<int>("retry_count"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddExecutionMode()
    {
        // Arrange & Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddExecutionMode("parallel")
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("execution_mode"));
        Assert.Equal("parallel", metadata.GetRequired<string>("execution_mode"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddParentExecutionId()
    {
        // Arrange & Act
        var parentId = "parent-123";
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddParentExecutionId(parentId)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("parent_execution_id"));
        Assert.Equal(parentId, metadata.GetRequired<string>("parent_execution_id"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddUserId()
    {
        // Arrange & Act
        var userId = "user-456";
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddUserId(userId)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("user_id"));
        Assert.Equal(userId, metadata.GetRequired<string>("user_id"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddSessionId()
    {
        // Arrange & Act
        var sessionId = "session-789";
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddSessionId(sessionId)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("session_id"));
        Assert.Equal(sessionId, metadata.GetRequired<string>("session_id"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddPriority()
    {
        // Arrange & Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddPriority(10)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("priority"));
        Assert.Equal(10, metadata.GetRequired<int>("priority"));
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddTags()
    {
        // Arrange
        var tags = new[] { "tag1", "tag2", "tag3" };

        // Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddTags(tags)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("tags"));
        var retrievedTags = metadata.GetRequired<string[]>("tags");
        Assert.Equal(tags, retrievedTags);
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddResourceConstraints()
    {
        // Arrange
        var constraints = new Dictionary<string, double>
        {
            { "cpu", 2.5 },
            { "memory", 1024.0 },
            { "gpu", 1.0 }
        };

        // Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddResourceConstraints(constraints)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("resource_constraints"));
        var retrievedConstraints = metadata.GetRequired<Dictionary<string, double>>("resource_constraints");
        Assert.Equal(constraints, retrievedConstraints);
    }

    [Fact]
    public void ShouldAddCorrectMetadata_WhenUsingBuilderAddCheckpoint()
    {
        // Arrange
        var checkpointData = "checkpoint-state-123";

        // Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddCheckpoint(checkpointData)
            .Build();

        // Assert
        Assert.True(metadata.ContainsKey("checkpoint"));
        Assert.Equal(checkpointData, metadata.GetRequired<string>("checkpoint"));
    }

    [Fact]
    public void ShouldMaintainFluency_WhenUsingBuilderChainedOperations()
    {
        // Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .AddIterationCount(3)
            .AddRetryCount(2)
            .AddExecutionMode("sequential")
            .AddUserId("user-123")
            .AddSessionId("session-456")
            .AddPriority(5)
            .AddTags(s_testProductionTags)
            .Add("custom", "value")
            .Build();

        // Assert
        Assert.Equal(8, metadata.Keys.Count());
        Assert.Equal(3, metadata.GetRequired<int>("iteration_count"));
        Assert.Equal(2, metadata.GetRequired<int>("retry_count"));
        Assert.Equal("sequential", metadata.GetRequired<string>("execution_mode"));
        Assert.Equal("user-123", metadata.GetRequired<string>("user_id"));
        Assert.Equal("session-456", metadata.GetRequired<string>("session_id"));
        Assert.Equal(5, metadata.GetRequired<int>("priority"));
        Assert.Equal(2, metadata.GetRequired<string[]>("tags").Length);
        Assert.Equal("value", metadata.GetRequired<string>("custom"));
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
        var metadata = ExecutionMetadata.FromDictionary(dictionary);

        // Assert
        Assert.Equal(3, metadata.Keys.Count());
        Assert.Equal("value1", metadata.Get<string>("key1"));
        Assert.Equal(42, metadata.GetRequired<int>("key2"));
        Assert.NotNull(metadata.Get<TestClass>("key3"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNullDictionary()
    {
        // Act
        var metadata = ExecutionMetadata.FromDictionary(null);

        // Assert
        Assert.Same(ExecutionMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var metadata = ExecutionMetadata.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Same(ExecutionMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingExecutionMetadataValueFromWithNullValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ExecutionMetadataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingExecutionMetadataValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = ExecutionMetadataValue.From("test");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test", result);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingExecutionMetadataValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = ExecutionMetadataValue.From("test");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(() =>
            value.GetValue<TestClass>());
        Assert.Contains("Cannot convert execution metadata value", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenUsingExecutionMetadataValueUsingProperties()
    {
        // Arrange
        var testObject = new TestClass { Name = "Test", Value = 123 };
        var value = ExecutionMetadataValue.From(testObject);

        // Act & Assert
        Assert.Same(testObject, value.RawValue);
        Assert.Equal(typeof(TestClass), value.ValueType);
    }

    [Fact]
    public void ShouldUpdateValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Arrange & Act
        var metadata = ExecutionMetadata.CreateBuilder()
            .Add("key", "original")
            .Add("key", "updated")
            .Build();

        // Assert
        Assert.Equal("updated", metadata.GetRequired<string>("key"));
    }

    [Fact]
    public void ShouldMaintain_WhenUsingMetadataImmutability()
    {
        // Arrange
        var original = ExecutionMetadata.CreateBuilder()
            .Add("key1", "value1")
            .Build();

        // Act
        var builder = ExecutionMetadata.CreateBuilderFrom(original);
        var modified = builder
            .Add("key1", "modified")
            .Add("key2", "value2")
            .Build();

        // Assert
        Assert.Equal("value1", original.Get<string>("key1"));
        Assert.False(original.ContainsKey("key2"));
        Assert.Equal("modified", modified.Get<string>("key1"));
        Assert.Equal("value2", modified.Get<string>("key2"));
    }

    // Helper class for testing
    private class TestClass
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
