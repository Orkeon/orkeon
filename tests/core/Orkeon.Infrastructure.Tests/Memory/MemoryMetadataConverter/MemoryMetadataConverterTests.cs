using Orkeon.Domain.Memory.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Memory;

namespace Orkeon.Infrastructure.Tests.Memory;

public class MemoryMetadataConverterTests
{
    private static readonly string[] s_tags12 = ["tag1", "tag2"];

    #region ConvertToSerializable Tests

    [Fact]
    public void ShouldReturnEmpty_WhenConvertToSerializableWithNullMetadata()
    {
        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(null);

        // Assert
        Assert.NotNull(result);
        var dict = result.ToDictionary();
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldConvertAllFields_WhenConvertToSerializableWithBasicMetadata()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var metadata = MemoryMetadata.Create(
            createdAt: createdAt,
            lastAccessedAt: null,
            source: "test-source",
            relevance: 0.85,
            accessCount: 5);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.Equal(4, dict.Count);
        Assert.Equal(createdAt, dict["createdAt"]);
        Assert.Equal("test-source", dict["source"]);
        Assert.Equal(0.85f, dict["relevance"]); // Note: converted to float
        Assert.Equal(5, dict["accessCount"]);
    }

    [Fact]
    public void ShouldIncludeIt_WhenConvertToSerializableWithLastAccessedAt()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var lastAccessedAt = createdAt.AddHours(1);
        var metadata = MemoryMetadata.Create(
            createdAt: createdAt,
            lastAccessedAt: lastAccessedAt,
            source: "test",
            relevance: 0.5);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.Contains("lastAccessedAt", dict.Keys);
        Assert.Equal(lastAccessedAt, dict["lastAccessedAt"]);
    }

    [Fact]
    public void ShouldJoinThem_WhenConvertToSerializableWithTags()
    {
        // Arrange
        var tags = new[] { "tag1", "tag2", "tag3" };
        var metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow,
            lastAccessedAt: null,
            source: "test",
            relevance: 0.5,
            tags: tags);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.Contains("tags", dict.Keys);
        Assert.Equal("tag1,tag2,tag3", dict["tags"]);
    }

    [Fact]
    public void ShouldNotIncludeThem_WhenConvertToSerializableWithEmptyTags()
    {
        // Arrange
        var metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow,
            lastAccessedAt: null,
            source: "test",
            relevance: 0.5,
            tags: []);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.DoesNotContain("tags", dict.Keys);
    }

    [Fact]
    public void ShouldConvertToString_WhenConvertToSerializableWithCreatedBy()
    {
        // Arrange
        var agentId = AgentId.Create();
        var metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow,
            lastAccessedAt: null,
            source: "test",
            relevance: 0.5,
            createdBy: agentId);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.Contains("createdBy", dict.Keys);
        Assert.Equal(agentId.ToString(), dict["createdBy"]);
    }

    [Fact]
    public void ShouldPrefixThem_WhenConvertToSerializableWithCustomProperties()
    {
        // Arrange
        var customProps = new Dictionary<string, string>
        {
            { "prop1", "value1" },
            { "prop2", "value2" },
            { "nested.prop", "nested value" }
        };
        var metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow,
            lastAccessedAt: null,
            source: "test",
            relevance: 0.5,
            customProperties: customProps);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.Contains("custom_prop1", dict.Keys);
        Assert.Contains("custom_prop2", dict.Keys);
        Assert.Contains("custom_nested.prop", dict.Keys);
        Assert.Equal("value1", dict["custom_prop1"]);
        Assert.Equal("value2", dict["custom_prop2"]);
        Assert.Equal("nested value", dict["custom_nested.prop"]);
    }

    [Fact]
    public void ShouldConvertEverything_WhenConvertToSerializableWithAllFields()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var lastAccessedAt = createdAt.AddMinutes(30);
        var agentId = AgentId.Create();
        var tags = new[] { "important", "processed" };
        var customProps = new Dictionary<string, string>
        {
            { "category", "research" },
            { "priority", "high" }
        };

        var metadata = MemoryMetadata.Create(
            createdAt: createdAt,
            lastAccessedAt: lastAccessedAt,
            source: "research-agent",
            relevance: 0.95,
            tags: tags,
            createdBy: agentId,
            accessCount: 10,
            customProperties: customProps);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.Equal(9, dict.Count); // 4 basic + lastAccessedAt + tags + createdBy + 2 custom
        Assert.Equal(createdAt, dict["createdAt"]);
        Assert.Equal(lastAccessedAt, dict["lastAccessedAt"]);
        Assert.Equal("research-agent", dict["source"]);
        Assert.Equal(0.95f, dict["relevance"]);
        Assert.Equal(10, dict["accessCount"]);
        Assert.Equal("important,processed", dict["tags"]);
        Assert.Equal(agentId.ToString(), dict["createdBy"]);
        Assert.Equal("research", dict["custom_category"]);
        Assert.Equal("high", dict["custom_priority"]);
    }

    #endregion

    #region ConvertToDomainFormat Tests

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenConvertToDomainFormatWithNullMetadata()
    {
        // Act
        var result = MemoryMetadataConverter.ConvertToDomainFormat(null);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenConvertToDomainFormatWithEmptyMetadata()
    {
        // Arrange
        var metadata = DeserializedMemoryMetadata.Empty;

        // Act
        var result = MemoryMetadataConverter.ConvertToDomainFormat(metadata);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldConvertToStrings_WhenConvertToDomainFormatWithSimpleData()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", 42 },
            { "key3", true },
            { "key4", 3.14 }
        };
        var metadata = DeserializedMemoryMetadata.FromDictionary(dict);

        // Act
        var result = MemoryMetadataConverter.ConvertToDomainFormat(metadata);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Count);
        Assert.Equal("value1", result["key1"]);
        Assert.Equal("42", result["key2"]);
        Assert.Equal("True", result["key3"]);
        Assert.Equal("3.14", result["key4"]);
    }

    [Fact]
    public void ShouldConvertToStrings_WhenConvertToDomainFormatWithComplexData()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var dict = new Dictionary<string, object>
        {
            { "createdAt", now },
            { "tags", "tag1,tag2,tag3" },
            { "relevance", 0.75f },
            { "custom_property", "custom value" }
        };
        var metadata = DeserializedMemoryMetadata.FromDictionary(dict);

        // Act
        var result = MemoryMetadataConverter.ConvertToDomainFormat(metadata);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(4, result.Count);
        Assert.Equal(now.ToString(), result["createdAt"]);
        Assert.Equal("tag1,tag2,tag3", result["tags"]);
        Assert.Equal("0.75", result["relevance"]);
        Assert.Equal("custom value", result["custom_property"]);
    }

    #endregion

    #region SerializableMemoryMetadata Tests

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenSerializableMemoryMetadataEmpty()
    {
        // Act
        var empty = SerializableMemoryMetadata.Empty;
        var dict = empty.ToDictionary();

        // Assert
        Assert.NotNull(dict);
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenSerializableMemoryMetadataBuilder()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var builder = SerializableMemoryMetadata.CreateBuilder();

        // Act
        var metadata = builder
            .AddCreatedAt(now)
            .AddSource("test-source")
            .AddRelevance(0.9f)
            .AddAccessCount(3)
            .AddLastAccessedAt(now.AddHours(1))
            .AddTags(s_tags12)
            .AddCreatedBy("agent-123")
            .AddCustomProperty("key1", "value1")
            .AddCustomProperty("key2", "value2")
            .Build();

        var dict = metadata.ToDictionary();

        // Assert
        Assert.Equal(9, dict.Count);
        Assert.Equal(now, dict["createdAt"]);
        Assert.Equal("test-source", dict["source"]);
        Assert.Equal(0.9f, dict["relevance"]);
        Assert.Equal(3, dict["accessCount"]);
        Assert.Equal(now.AddHours(1), dict["lastAccessedAt"]);
        Assert.Equal("tag1,tag2", dict["tags"]);
        Assert.Equal("agent-123", dict["createdBy"]);
        Assert.Equal("value1", dict["custom_key1"]);
        Assert.Equal("value2", dict["custom_key2"]);
    }

    #endregion

    #region DeserializedMemoryMetadata Tests

    [Fact]
    public void ShouldReturnEmpty_WhenDeserializedMemoryMetadataFromDictionaryWithNull()
    {
        // Act
        var metadata = DeserializedMemoryMetadata.FromDictionary(null);

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Keys);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenDeserializedMemoryMetadataFromDictionaryWithEmptyDict()
    {
        // Act
        var metadata = DeserializedMemoryMetadata.FromDictionary([]);

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Keys);
    }

    [Fact]
    public void ShouldReturnValue_WhenDeserializedMemoryMetadataGetWithExistingKey()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "string", "value" },
            { "int", 42 },
            { "bool", true },
            { "float", 3.14f }
        };
        var metadata = DeserializedMemoryMetadata.FromDictionary(dict);

        // Act & Assert
        Assert.Equal("value", metadata.Get<string>("string"));
        Assert.Equal(42, metadata.Get<int>("int"));
        Assert.True(metadata.Get<bool>("bool"));
        Assert.Equal(3.14f, metadata.Get<float>("float"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenDeserializedMemoryMetadataGetWithNonExistingKey()
    {
        // Arrange
        var dict = new Dictionary<string, object> { { "key", "value" } };
        var metadata = DeserializedMemoryMetadata.FromDictionary(dict);

        // Act & Assert
        Assert.Null(metadata.Get<string>("nonexistent"));
        Assert.Equal(0, metadata.Get<int>("nonexistent"));
        Assert.False(metadata.Get<bool>("nonexistent"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenDeserializedMemoryMetadataKeys()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "key1", "value1" },
            { "key2", "value2" },
            { "key3", "value3" }
        };
        var metadata = DeserializedMemoryMetadata.FromDictionary(dict);

        // Act
        var keys = metadata.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    #endregion

    #region MemoryMetadataValue Tests

    [Fact]
    public void ShouldThrow_WhenMemoryMetadataValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => MemoryMetadataValue.From(null!));
    }

    [Fact]
    public void ShouldReturn_WhenMemoryMetadataValueGetValueWithCorrectType()
    {
        // Arrange
        var value = MemoryMetadataValue.From("test string");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void ShouldConvert_WhenMemoryMetadataValueGetValueWithConvertibleType()
    {
        // Arrange
        var value = MemoryMetadataValue.From(42);

        // Act
        var asString = value.GetValue<string>();
        var asDouble = value.GetValue<double>();
        var asLong = value.GetValue<long>();

        // Assert
        Assert.Equal("42", asString);
        Assert.Equal(42.0, asDouble);
        Assert.Equal(42L, asLong);
    }

    [Fact]
    public void ShouldThrow_WhenMemoryMetadataValueGetValueWithIncompatibleType()
    {
        // Arrange
        var value = MemoryMetadataValue.From("not a number");

        // Act & Assert
        var ex = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert memory metadata value", ex.Message);
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenMemoryMetadataValueProperties()
    {
        // Arrange
        var dateTime = DateTime.UtcNow;
        var value = MemoryMetadataValue.From(dateTime);

        // Act & Assert
        Assert.Equal(dateTime, value.RawValue);
        Assert.Equal(typeof(DateTime), value.ValueType);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldHandleCorrectly_WhenConvertToSerializableWithSpecialCharactersInCustomProperties()
    {
        // Arrange
        var customProps = new Dictionary<string, string>
        {
            { "key with spaces", "value with spaces" },
            { "key.with.dots", "value.with.dots" },
            { "key_with_underscores", "value_with_underscores" },
            { "key-with-dashes", "value-with-dashes" }
        };
        var metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow,
            lastAccessedAt: null,
            source: "test",
            relevance: 0.5,
            customProperties: customProps);

        // Act
        var result = MemoryMetadataConverter.ConvertToSerializable(metadata);
        var dict = result.ToDictionary();

        // Assert
        Assert.Contains("custom_key with spaces", dict.Keys);
        Assert.Contains("custom_key.with.dots", dict.Keys);
        Assert.Contains("custom_key_with_underscores", dict.Keys);
        Assert.Contains("custom_key-with-dashes", dict.Keys);
    }

    [Fact]
    public void ShouldPreserveStringValues_WhenRoundTripWithComplexMetadata()
    {
        // Arrange
        var originalDict = new Dictionary<string, object>
        {
            { "string", "test value" },
            { "number", 123 },
            { "bool", true },
            { "date", DateTime.UtcNow.ToString("O") }
        };
        var deserialized = DeserializedMemoryMetadata.FromDictionary(originalDict);

        // Act
        var domainFormat = MemoryMetadataConverter.ConvertToDomainFormat(deserialized);

        // Assert
        Assert.NotNull(domainFormat);
        Assert.Equal("test value", domainFormat["string"]);
        Assert.Equal("123", domainFormat["number"]);
        Assert.Equal("True", domainFormat["bool"]);
        Assert.Contains("T", domainFormat["date"]); // ISO date format contains 'T'
    }

    #endregion
}
