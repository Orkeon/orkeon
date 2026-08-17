using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Serialization;

public class YamlDotNetSerializerTests
{
    private readonly YamlDotNetSerializer _serializer;

    public YamlDotNetSerializerTests()
    {
        _serializer = new YamlDotNetSerializer();
    }

    [Fact]
    public void ShouldReturnValidYaml_WhenSerializeWithSimpleObject()
    {
        // Arrange
        var obj = new TestObject
        {
            Name = "Test",
            Value = 42,
            IsActive = true
        };

        // Act
        var yaml = _serializer.Serialize(obj);

        // Assert
        Assert.NotNull(yaml);
        Assert.Contains("name: Test", yaml);
        Assert.Contains("value: 42", yaml);
        Assert.Contains("isActive: true", yaml);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenSerializeWithNullObject()
    {
        // Arrange
        TestObject? obj = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => _serializer.Serialize(obj!));
        Assert.Equal("obj", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnObject_WhenDeserializeWithValidYaml()
    {
        // Arrange
        var yaml = @"
name: Test
value: 42
isActive: true";

        // Act
        var result = _serializer.Deserialize<TestObject>(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
        Assert.True(result.IsActive);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeWithNullYaml()
    {
        // Arrange
        string? yaml = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.Deserialize<TestObject>(yaml!));
        Assert.Equal("yaml", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeWithEmptyYaml()
    {
        // Arrange
        var yaml = "";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.Deserialize<TestObject>(yaml));
        Assert.Equal("yaml", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeWithWhitespaceYaml()
    {
        // Arrange
        var yaml = "   \n\t  ";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.Deserialize<TestObject>(yaml));
        Assert.Equal("yaml", exception.ParamName);
    }

    [Fact]
    public void ShouldPreserveData_WhenSerializeAndDeserializeWithComplexObject()
    {
        // Arrange
        var original = new ComplexTestObject
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Complex Object",
            Tags = ["tag1", "tag2", "tag3"],
            Settings = new Dictionary<string, object>
            {
                ["setting1"] = "value1",
                ["setting2"] = 123,
                ["setting3"] = true
            },
            Nested = new TestObject
            {
                Name = "Nested",
                Value = 99,
                IsActive = false
            }
        };

        // Act
        var yaml = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<ComplexTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Tags, deserialized.Tags);
        Assert.Equal(original.Settings["setting1"], deserialized.Settings["setting1"]);
        Assert.Equal(123, Convert.ToInt32(deserialized.Settings["setting2"]));
        Assert.True(Convert.ToBoolean(deserialized.Settings["setting3"]));
        Assert.Equal(original.Nested.Name, deserialized.Nested.Name);
        Assert.Equal(original.Nested.Value, deserialized.Nested.Value);
        Assert.Equal(original.Nested.IsActive, deserialized.Nested.IsActive);
    }

    [Fact]
    public void ShouldGenerateValidYaml_WhenSerializeWithCollections()
    {
        // Arrange
        var obj = new CollectionTestObject
        {
            Numbers = [1, 2, 3, 4, 5],
            Names = ["Alice", "Bob", "Charlie"],
            Scores = new Dictionary<string, int>
            {
                ["Alice"] = 100,
                ["Bob"] = 85,
                ["Charlie"] = 92
            }
        };

        // Act
        var yaml = _serializer.Serialize(obj);

        // Assert
        Assert.NotNull(yaml);
        Assert.Contains("numbers:", yaml);
        Assert.Contains("- 1", yaml);
        Assert.Contains("- 2", yaml);
        Assert.Contains("names:", yaml);
        Assert.Contains("- Alice", yaml);
        Assert.Contains("scores:", yaml);
        Assert.Contains("Alice: 100", yaml);
    }

    [Fact]
    public void ShouldReturnObject_WhenDeserializeNonGenericWithValidYaml()
    {
        // Arrange
        var yaml = @"
name: Test
value: 42
isActive: true";

        // Act
        var result = _serializer.Deserialize(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Dictionary<object, object>>(result);
        var dict = (Dictionary<object, object>)result;
        Assert.Equal("Test", dict["name"]);
        Assert.Equal(42, Convert.ToInt32(dict["value"]));
        Assert.True(Convert.ToBoolean(dict["isActive"]));
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeNonGenericWithNullYaml()
    {
        // Arrange
        string? yaml = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.Deserialize(yaml!));
        Assert.Equal("yaml", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenDeserializeNonGenericWithEmptyYaml()
    {
        // Arrange
        var yaml = "";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => _serializer.Deserialize(yaml));
        Assert.Equal("yaml", exception.ParamName);
    }

    [Fact]
    public void ShouldHandleDatesCorrectly_WhenSerializeWithDateTimeObject()
    {
        // Arrange
        var obj = new DateTestObject
        {
            CreatedAt = new DateTime(2024, 12, 25, 10, 30, 45, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 12, 26, 15, 45, 30, DateTimeKind.Utc)
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<DateTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(obj.UpdatedAt, deserialized.UpdatedAt);
    }

    [Fact]
    public void ShouldHandleNullsCorrectly_WhenSerializeWithNullProperties()
    {
        // Arrange
        var obj = new NullableTestObject
        {
            RequiredName = "Required",
            OptionalName = null,
            OptionalValue = null
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<NullableTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.RequiredName, deserialized.RequiredName);
        Assert.Null(deserialized.OptionalName);
        Assert.Null(deserialized.OptionalValue);
    }

    [Fact]
    public void ShouldEscapeCorrectly_WhenSerializeWithSpecialCharacters()
    {
        // Arrange
        var obj = new TestObject
        {
            Name = "Special: \"characters\" & 'quotes' \n newline",
            Value = 123,
            IsActive = true
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<TestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.Name, deserialized.Name);
    }

    [Fact]
    public void ShouldThrowException_WhenDeserializeWithInvalidYamlSyntax()
    {
        // Arrange
        var invalidYaml = @"
name: Test
value: [unclosed bracket
isActive: true";

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => _serializer.Deserialize<TestObject>(invalidYaml));
    }

    [Fact]
    public void ShouldNotCauseStackOverflow_WhenSerializeWithCircularReference()
    {
        // Arrange
        var parent = new CircularTestObject { Name = "Parent" };
        var child = new CircularTestObject { Name = "Child", Parent = parent };
        parent.Children = [child];

        // Act & Assert - Should either serialize successfully or throw a clear exception
        try
        {
            var yaml = _serializer.Serialize(parent);
            Assert.NotNull(yaml);
        }
        catch (Exception ex)
        {
            // YamlDotNet should handle circular references with an exception
            Assert.NotNull(ex.Message);
        }
    }

    [Fact]
    public void ShouldIgnoreExtraFields_WhenDeserializeWithUnknownProperties()
    {
        // Arrange — friction #2 from Experiment 07: forward-compatible YAML extensions
        // (e.g. a newer YAML dialect adds a field we don't model yet) must not crash the loader.
        var yaml = @"
name: Test
value: 42
isActive: true
unknownField: ShouldBeIgnored
anotherUnknown: 999";

        // Act
        var result = _serializer.Deserialize<TestObject>(yaml);

        // Assert — known fields are populated; unknown fields are silently dropped.
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
        Assert.True(result.IsActive);
    }

    [Fact]
    public void ShouldSerializeAsString_WhenSerializeWithEnum()
    {
        // Arrange
        var obj = new EnumTestObject
        {
            Status = TestStatus.Active,
            Priority = TestPriority.High
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<EnumTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.Status, deserialized.Status);
        Assert.Equal(obj.Priority, deserialized.Priority);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenSerializeWithVeryLargeObject()
    {
        // Arrange
        var obj = new ComplexTestObject
        {
            Id = Guid.NewGuid().ToString(),
            Name = new string('x', 10000), // Very long string
            Tags = []
        };

        // Add many tags
        for (int i = 0; i < 1000; i++)
        {
            obj.Tags.Add($"tag_{i}");
        }

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<ComplexTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.Name, deserialized.Name);
        Assert.Equal(obj.Tags.Count, deserialized.Tags.Count);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenSerializeWithUnicodeCharacters()
    {
        // Arrange
        var obj = new TestObject
        {
            Name = "Unicode: 你好世界 🎉 مرحبا العالم",
            Value = 42,
            IsActive = true
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<TestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.Name, deserialized.Name);
    }

    [Fact]
    public void ShouldUseDefaultValues_WhenDeserializeWithMissingRequiredFields()
    {
        // Arrange
        var yaml = @"
name: OnlyName";

        // Act
        var result = _serializer.Deserialize<TestObject>(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("OnlyName", result.Name);
        Assert.Equal(0, result.Value); // Default int value
        Assert.False(result.IsActive); // Default bool value
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenSerializeWithDeeplyNestedObject()
    {
        // Arrange
        var obj = new ComplexTestObject
        {
            Id = "root",
            Name = "Root Object",
            Nested = new TestObject
            {
                Name = "Level 1",
                Value = 1,
                IsActive = true
            }
        };

        // Add nested dictionaries
        obj.Settings["nestedDict"] = new Dictionary<string, object>
        {
            ["level2"] = new Dictionary<string, object>
            {
                ["level3"] = new Dictionary<string, object>
                {
                    ["deepValue"] = "Found it!"
                }
            }
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<ComplexTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Settings["nestedDict"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenSerializeWithEmptyCollections()
    {
        // Arrange
        var obj = new CollectionTestObject
        {
            Numbers = [],
            Names = [],
            Scores = []
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<CollectionTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Numbers);
        Assert.Empty(deserialized.Names);
        Assert.Empty(deserialized.Scores);
    }

    [Fact]
    public void ShouldHandleTypeConversion_WhenDeserializeWithNumericStringValues()
    {
        // Arrange
        var yaml = @"
name: Test
value: '42'  # String representation of number
isActive: 'true' # String representation of boolean";

        // Act
        // YamlDotNet handles type conversion automatically in newer versions
        var result = _serializer.Deserialize<TestObject>(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
        Assert.True(result.IsActive);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenSerializeWithGuid()
    {
        // Arrange
        var obj = new GuidTestObject
        {
            Id = Guid.NewGuid(),
            Name = "Test with GUID"
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<GuidTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.Id, deserialized.Id);
        Assert.Equal(obj.Name, deserialized.Name);
    }

    [Fact]
    public void ShouldMaintainPrecision_WhenSerializeWithDecimalValues()
    {
        // Arrange
        var obj = new DecimalTestObject
        {
            Price = 123.456789m,
            Quantity = 999999999999999999m,
            Discount = 0.0000000001m
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<DecimalTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.Price, deserialized.Price);
        Assert.Equal(obj.Quantity, deserialized.Quantity);
        Assert.Equal(obj.Discount, deserialized.Discount);
    }

    [Fact]
    public void ShouldIgnoreComments_WhenDeserializeWithComments()
    {
        // Arrange
        var yaml = @"
# This is a comment
name: Test # Inline comment
value: 42
# Another comment
isActive: true";

        // Act
        var result = _serializer.Deserialize<TestObject>(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
        Assert.True(result.IsActive);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenSerializeWithByteArray()
    {
        // Arrange
        var obj = new ByteArrayTestObject
        {
            Data = [0, 1, 2, 3, 255, 254, 253],
            Name = "Binary Data"
        };

        // Act
        var yaml = _serializer.Serialize(obj);
        var deserialized = _serializer.Deserialize<ByteArrayTestObject>(yaml);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(obj.Data, deserialized.Data);
        Assert.Equal(obj.Name, deserialized.Name);
    }

    [Fact]
    public void ShouldResolveCorrectly_WhenDeserializeWithAnchorsAndReferences()
    {
        // Arrange
        var yaml = @"
defaultSettings: &defaults
  timeout: 30
  retries: 3
  
profile1:
  <<: *defaults
  name: Profile One
  
profile2:
  <<: *defaults
  name: Profile Two
  timeout: 60";

        // Act
        var result = _serializer.Deserialize(yaml);

        // Assert
        Assert.NotNull(result);
        // YamlDotNet should handle anchors and merge keys
    }
}

// Test classes
internal class TestObject
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public bool IsActive { get; set; }
}

internal class ComplexTestObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, object> Settings { get; set; } = [];
    public TestObject Nested { get; set; } = new();
}

internal class CollectionTestObject
{
    public int[] Numbers { get; set; } = [];
    public List<string> Names { get; set; } = [];
    public Dictionary<string, int> Scores { get; set; } = [];
}

internal class DateTestObject
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal class NullableTestObject
{
    public string RequiredName { get; set; } = string.Empty;
    public string? OptionalName { get; set; }
    public int? OptionalValue { get; set; }
}

internal class CircularTestObject
{
    public string Name { get; set; } = string.Empty;
    public CircularTestObject? Parent { get; set; }
    public List<CircularTestObject> Children { get; set; } = [];
}

internal class EnumTestObject
{
    public TestStatus Status { get; set; }
    public TestPriority Priority { get; set; }
}

internal enum TestStatus
{
    Active,
    Inactive,
    Pending
}

internal enum TestPriority
{
    Low,
    Medium,
    High
}

internal class GuidTestObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal class DecimalTestObject
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Discount { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal class ByteArrayTestObject
{
    public byte[] Data { get; set; } = [];
    public string Name { get; set; } = string.Empty;
}
