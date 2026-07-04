using System.Text.Json;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.Common;

public class ValueObjectRecordTests
{
    // Test implementation for ValueObjectRecord
    private record TestValueObject : ValueObjectRecord
    {
        public string Name { get; init; }
        public int Value { get; init; }
        public DateTime? OptionalDate { get; init; }

        public TestValueObject(string name, int value)
        {
            Name = name;
            Value = value;
        }

        // Parameterless constructor for JSON deserialization
        public TestValueObject() : this("", 0) { }
    }

    // Test implementation with validation
    private record ValidatedValueObject : ValueObjectRecord
    {
        public string RequiredName { get; init; }
        public int PositiveValue { get; init; }

        public ValidatedValueObject(string name, int value)
        {
            RequiredName = EnsureNotNullOrWhiteSpace(name, nameof(name));
            PositiveValue = EnsurePositive(value, nameof(value));
            Validate();
        }

        protected override void Validate()
        {
            if (RequiredName.Length > 100)
                throw new ArgumentException("Name cannot exceed 100 characters");
        }
    }

    // Test implementation with collections
    private record CollectionValueObject : ValueObjectRecord
    {
        public IReadOnlyList<string> Items { get; init; }
        public IReadOnlyDictionary<string, int> Mapping { get; init; }

        public CollectionValueObject(IEnumerable<string> items, IDictionary<string, int> mapping)
        {
            Items = CreateDefensiveCopy(items);
            Mapping = CreateDefensiveCopy(mapping);
        }
    }

    // Test class to expose protected methods for testing
    private record TestHelper : ValueObjectRecord
    {
        public static T TestEnsureNotNull<T>(T? value, string parameterName) where T : class
            => EnsureNotNull(value, parameterName);

        public static T TestEnsureInRange<T>(T value, T min, T max, string parameterName) where T : IComparable<T>
            => EnsureInRange(value, min, max, parameterName);

        public static T TestEnsurePositive<T>(T value, string parameterName) where T : IComparable<T>, IComparable
            => EnsurePositive(value, parameterName);

        public static T TestEnsureNonNegative<T>(T value, string parameterName) where T : IComparable<T>, IComparable
            => EnsureNonNegative(value, parameterName);

        public static string TestEnsureNotNullOrWhiteSpace(string? value, string parameterName)
            => EnsureNotNullOrWhiteSpace(value, parameterName);
    }

    [Fact]
    public void ShouldSerializeObjectCorrectly_WhenUsingToJson()
    {
        // Arrange
        var obj = new TestValueObject("Test", 42) { OptionalDate = new DateTime(2024, 1, 1) };

        // Act
        var json = obj.ToJson();
        var parsed = JsonSerializer.Deserialize<TestValueObject>(json);

        // Assert
        Assert.Contains("\"Name\": \"Test\"", json);
        Assert.Contains("\"Value\": 42", json);
        Assert.Contains("2024-01-01", json);
        Assert.NotNull(parsed);
        Assert.Equal("Test", parsed.Name);
        Assert.Equal(42, parsed.Value);
    }

    [Fact]
    public void ShouldIgnoreNullValues_WhenUsingToJsonWithNullProperties()
    {
        // Arrange
        var obj = new TestValueObject("Test", 42) { OptionalDate = null };

        // Act
        var json = obj.ToJson();

        // Assert
        Assert.Contains("\"Name\": \"Test\"", json);
        Assert.Contains("\"Value\": 42", json);
        Assert.DoesNotContain("OptionalDate", json);
    }

    [Fact]
    public void ShouldFormatWithIndentation_WhenUsingToJson()
    {
        // Arrange
        var obj = new TestValueObject("Test", 42);

        // Act
        var json = obj.ToJson();

        // Assert
        // Check for indentation by looking for newlines and spaces
        Assert.Contains("\n", json);
        Assert.Contains("  ", json); // Two spaces for indentation
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEnsureNotNullOrWhiteSpaceWithValidString()
    {
        // Arrange
        var obj = new ValidatedValueObject("Valid Name", 1);

        // Assert
        Assert.Equal("Valid Name", obj.RequiredName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingEnsureNotNullOrWhiteSpaceWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ValidatedValueObject(null!, 1));
        Assert.Contains("cannot be null or whitespace", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingEnsureNotNullOrWhiteSpaceWithEmptyString()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ValidatedValueObject("", 1));
        Assert.Contains("cannot be null or whitespace", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingEnsureNotNullOrWhiteSpaceWithWhitespace()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ValidatedValueObject("   ", 1));
        Assert.Contains("cannot be null or whitespace", exception.Message);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEnsureNotNullWithValidValue()
    {
        // Arrange
        var testObject = new object();

        // Act
        var result = TestHelper.TestEnsureNotNull(testObject, "test");

        // Assert
        Assert.Same(testObject, result);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingEnsureNotNullWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            TestHelper.TestEnsureNotNull<string>(null, "test"));
        Assert.Equal("test", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEnsureInRangeWithValueInRange()
    {
        // Act
        var result = TestHelper.TestEnsureInRange(50, 0, 100, "value");

        // Assert
        Assert.Equal(50, result);
    }

    [Fact]
    public void ShouldThrowArgumentOutOfRangeException_WhenUsingEnsureInRangeWithValueBelowMin()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TestHelper.TestEnsureInRange(-1, 0, 100, "value"));
        Assert.Equal("value", exception.ParamName);
        Assert.Contains("must be between 0 and 100", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentOutOfRangeException_WhenUsingEnsureInRangeWithValueAboveMax()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TestHelper.TestEnsureInRange(101, 0, 100, "value"));
        Assert.Equal("value", exception.ParamName);
        Assert.Contains("must be between 0 and 100", exception.Message);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEnsureInRangeWithBoundaryValues()
    {
        // Act
        var minResult = TestHelper.TestEnsureInRange(0, 0, 100, "value");
        var maxResult = TestHelper.TestEnsureInRange(100, 0, 100, "value");

        // Assert
        Assert.Equal(0, minResult);
        Assert.Equal(100, maxResult);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEnsurePositiveWithPositiveValue()
    {
        // Arrange
        var obj = new ValidatedValueObject("Test", 10);

        // Assert
        Assert.Equal(10, obj.PositiveValue);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingEnsurePositiveWithZero()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ValidatedValueObject("Test", 0));
        Assert.Contains("must be positive", exception.Message);
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingEnsurePositiveWithNegativeValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ValidatedValueObject("Test", -5));
        Assert.Contains("must be positive", exception.Message);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEnsureNonNegativeWithPositiveValue()
    {
        // Act
        var result = TestHelper.TestEnsureNonNegative(10, "value");

        // Assert
        Assert.Equal(10, result);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEnsureNonNegativeWithZero()
    {
        // Act
        var result = TestHelper.TestEnsureNonNegative(0, "value");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingEnsureNonNegativeWithNegativeValue()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            TestHelper.TestEnsureNonNegative(-1, "value"));
        Assert.Contains("cannot be negative", exception.Message);
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateIndependentCopy_WhenCreatingDefensiveCopyDictionary()
    {
        // Arrange
        var original = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var obj = new CollectionValueObject(Array.Empty<string>(), original);

        // Act
        original["a"] = 999; // Modify original
        original["c"] = 3;   // Add to original

        // Assert
        Assert.Equal(1, obj.Mapping["a"]); // Should still be 1
        Assert.Equal(2, obj.Mapping.Count); // Should still have 2 items
        Assert.False(obj.Mapping.ContainsKey("c")); // Should not have new key
    }

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenCreatingDefensiveCopyDictionaryWithNull()
    {
        // Act
        var obj = new CollectionValueObject(Array.Empty<string>(), null!);

        // Assert
        Assert.NotNull(obj.Mapping);
        Assert.Empty(obj.Mapping);
    }

    [Fact]
    public void ShouldCreateIndependentCopy_WhenCreatingDefensiveCopyList()
    {
        // Arrange
        var original = new List<string> { "a", "b" };
        var obj = new CollectionValueObject(original, new Dictionary<string, int>());

        // Act
        original.Add("c"); // Modify original
        original[0] = "z"; // Change existing element

        // Assert
        Assert.Equal(2, obj.Items.Count); // Should still have 2 items
        Assert.Equal("a", obj.Items[0]); // Should still be "a"
        Assert.DoesNotContain("c", obj.Items); // Should not have new item
    }

    [Fact]
    public void ShouldReturnEmptyList_WhenCreatingDefensiveCopyListWithNull()
    {
        // Act
        var obj = new CollectionValueObject(null!, new Dictionary<string, int>());

        // Assert
        Assert.NotNull(obj.Items);
        Assert.Empty(obj.Items);
    }

    [Fact]
    public void ShouldBeCalledDuringConstruction_WhenValidatingWhenOverridden()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ValidatedValueObject(new string('a', 101), 1));
        Assert.Contains("Name cannot exceed 100 characters", exception.Message);
    }

    [Fact]
    public void ShouldDoNothing_WhenValidatingWithDefaultImplementation()
    {
        // Act
        var obj = new TestValueObject("Test", 42);

        // Assert - No exception should be thrown
        Assert.NotNull(obj);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenRecordingEquality()
    {
        // Arrange
        var obj1 = new TestValueObject("Test", 42);
        var obj2 = new TestValueObject("Test", 42);
        var obj3 = new TestValueObject("Other", 42);

        // Assert
        Assert.Equal(obj1, obj2);
        Assert.NotEqual(obj1, obj3);
        Assert.Equal(obj1.GetHashCode(), obj2.GetHashCode());
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenRecordingWith()
    {
        // Arrange
        var original = new TestValueObject("Test", 42);

        // Act
        var modified = original with { Value = 100 };

        // Assert
        Assert.Equal("Test", modified.Name);
        Assert.Equal(100, modified.Value);
        Assert.NotEqual(original, modified);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingEnsurePositiveWithDouble()
    {
        // Act
        var result = TestHelper.TestEnsurePositive(3.14, "value");

        // Assert
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingEnsurePositiveWithDecimal()
    {
        // Act
        var result = TestHelper.TestEnsurePositive(99.99m, "value");

        // Assert
        Assert.Equal(99.99m, result);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingEnsureNonNegativeWithDouble()
    {
        // Act
        var result = TestHelper.TestEnsureNonNegative(0.0, "value");

        // Assert
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingEnsureInRangeWithDateTime()
    {
        // Arrange
        var min = new DateTime(2024, 1, 1);
        var max = new DateTime(2024, 12, 31);
        var value = new DateTime(2024, 6, 15);

        // Act
        var result = TestHelper.TestEnsureInRange(value, min, max, "date");

        // Assert
        Assert.Equal(value, result);
    }
}
