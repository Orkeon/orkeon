using System.Text.Json;
using Orkeon.Domain.Task;

namespace Orkeon.Domain.Tests.Task;

public class JsonSchemaTests
{
    [Fact]
    public void ShouldInitialize_WhenConstructingWithValidSchema()
    {
        // Arrange
        var schemaJson = @"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" },
                ""age"": { ""type"": ""integer"" }
            }
        }";

        // Act
        var schema = JsonSchema.From(schemaJson);

        // Assert
        Assert.NotNull(schema);
        Assert.Equal(schemaJson, schema.Schema);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithNullSchema()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => JsonSchema.From(null!));
        Assert.Equal("schema", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithEmptySchema()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => JsonSchema.From(""));
        Assert.Equal("schema", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithWhitespaceSchema()
    {
        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => JsonSchema.From("   "));
        Assert.Equal("schema", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenConstructingWithInvalidJson()
    {
        // Arrange
        var invalidJson = "{ invalid json }";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => JsonSchema.From(invalidJson));
        Assert.Equal("schema", exception.ParamName);
        Assert.Contains("Invalid JSON schema", exception.Message);
        Assert.NotNull(exception.InnerException);
        Assert.True(exception.InnerException is JsonException);
    }

    [Fact]
    public void ShouldReturnTrue_WhenValidatingWithMatchingStringType()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""string"" }");
        var json = @"""hello world""";

        // Act
        var result = schema.Validate(json);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingWithMismatchingType()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""string"" }");
        var json = "123";

        // Act
        var result = schema.Validate(json);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingWithNullJson()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""string"" }");

        // Act
        var result = schema.Validate(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingWithEmptyJson()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""string"" }");

        // Act
        var result = schema.Validate("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenValidatingWithInvalidJson()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""object"" }");
        var invalidJson = "{ invalid }";

        // Act
        var result = schema.Validate(invalidJson);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldAcceptNumbers_WhenValidatingNumberType()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""number"" }");

        // Act & Assert
        Assert.True(schema.Validate("123"));
        Assert.True(schema.Validate("123.45"));
        Assert.True(schema.Validate("-123.45"));
        Assert.False(schema.Validate(@"""123"""));
        Assert.False(schema.Validate("true"));
    }

    [Fact]
    public void ShouldAcceptOnlyIntegers_WhenValidatingIntegerType()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""integer"" }");

        // Act & Assert
        Assert.True(schema.Validate("123"));
        Assert.True(schema.Validate("-123"));
        Assert.False(schema.Validate("123.45")); // Not an integer
        Assert.False(schema.Validate(@"""123"""));
    }

    [Fact]
    public void ShouldAcceptBooleans_WhenValidatingBooleanType()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""boolean"" }");

        // Act & Assert
        Assert.True(schema.Validate("true"));
        Assert.True(schema.Validate("false"));
        Assert.False(schema.Validate(@"""true"""));
        Assert.False(schema.Validate("1"));
    }

    [Fact]
    public void ShouldAcceptNull_WhenValidatingWithNullType()
    {
        // Arrange
        var schema = JsonSchema.From(@"{ ""type"": ""null"" }");

        // Act & Assert
        Assert.True(schema.Validate("null"));
        Assert.False(schema.Validate(@"""null"""));
        Assert.False(schema.Validate("0"));
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenValidatingObjectWithRequiredProperties()
    {
        // Arrange
        var schema = JsonSchema.From(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" },
                ""age"": { ""type"": ""integer"" }
            },
            ""required"": [""name""]
        }");

        // Act & Assert
        Assert.True(schema.Validate(@"{ ""name"": ""John"", ""age"": 30 }"));
        Assert.True(schema.Validate(@"{ ""name"": ""Jane"" }")); // age is optional
        Assert.False(schema.Validate(@"{ ""age"": 30 }")); // name is required
        Assert.False(schema.Validate(@"{ ""name"": 123 }")); // wrong type
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenValidatingObjectWithAdditionalProperties()
    {
        // Arrange
        var schemaWithAdditional = JsonSchema.From(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" }
            }
        }");

        var schemaNoAdditional = JsonSchema.From(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" }
            },
            ""additionalProperties"": false
        }");

        var json = @"{ ""name"": ""John"", ""extra"": ""value"" }";

        // Act & Assert
        Assert.True(schemaWithAdditional.Validate(json)); // Additional properties allowed by default
        Assert.False(schemaNoAdditional.Validate(json)); // Additional properties not allowed
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenValidatingArray()
    {
        // Arrange
        var schema = JsonSchema.From(@"{
            ""type"": ""array"",
            ""items"": { ""type"": ""string"" }
        }");

        // Act & Assert
        Assert.True(schema.Validate(@"[""a"", ""b"", ""c""]"));
        Assert.True(schema.Validate(@"[]")); // Empty array
        Assert.False(schema.Validate(@"[""a"", 123, ""c""]")); // Mixed types
        Assert.False(schema.Validate(@"""not an array""")); // Not an array
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenValidatingArrayWithMinMaxItems()
    {
        // Arrange
        var schema = JsonSchema.From(@"{
            ""type"": ""array"",
            ""items"": { ""type"": ""integer"" },
            ""minItems"": 2,
            ""maxItems"": 4
        }");

        // Act & Assert
        Assert.False(schema.Validate(@"[1]")); // Too few
        Assert.True(schema.Validate(@"[1, 2]")); // Min
        Assert.True(schema.Validate(@"[1, 2, 3]")); // Within range
        Assert.True(schema.Validate(@"[1, 2, 3, 4]")); // Max
        Assert.False(schema.Validate(@"[1, 2, 3, 4, 5]")); // Too many
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenValidatingWithNestedObject()
    {
        // Arrange
        var schema = JsonSchema.From(@"{
            ""type"": ""object"",
            ""properties"": {
                ""person"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""name"": { ""type"": ""string"" },
                        ""age"": { ""type"": ""integer"" }
                    },
                    ""required"": [""name""]
                }
            }
        }");

        // Act & Assert
        Assert.True(schema.Validate(@"{ ""person"": { ""name"": ""John"", ""age"": 30 } }"));
        Assert.False(schema.Validate(@"{ ""person"": { ""age"": 30 } }")); // Missing required name
        Assert.False(schema.Validate(@"{ ""person"": ""not an object"" }")); // Wrong type
    }

    [Fact]
    public void ShouldAcceptAnyValue_WhenValidatingSchemaWithoutType()
    {
        // Arrange
        var schema = JsonSchema.From(@"{}"); // Schema without type constraint

        // Act & Assert
        Assert.True(schema.Validate(@"""string"""));
        Assert.True(schema.Validate("123"));
        Assert.True(schema.Validate("true"));
        Assert.True(schema.Validate("{}"));
        Assert.True(schema.Validate("[]"));
        Assert.True(schema.Validate("null"));
    }

    [Fact]
    public void ShouldGenerateStringSchema_WhenUsingFromTypeWithStringType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(string));

        // Assert
        Assert.Contains(@"""type"": ""string""", schema.Schema);
        Assert.True(schema.Validate(@"""test string"""));
        Assert.False(schema.Validate("123"));
    }

    [Fact]
    public void ShouldGenerateIntegerSchema_WhenUsingFromTypeWithIntType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(int));

        // Assert
        Assert.Contains(@"""type"": ""integer""", schema.Schema);
        Assert.True(schema.Validate("123"));
        Assert.False(schema.Validate("123.45"));
        Assert.False(schema.Validate(@"""123"""));
    }

    [Fact]
    public void ShouldGenerateBooleanSchema_WhenUsingFromTypeWithBoolType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(bool));

        // Assert
        Assert.Contains(@"""type"": ""boolean""", schema.Schema);
        Assert.True(schema.Validate("true"));
        Assert.True(schema.Validate("false"));
        Assert.False(schema.Validate(@"""true"""));
    }

    [Fact]
    public void ShouldGenerateDateTimeSchema_WhenUsingFromTypeWithDateTimeType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(DateTime));

        // Assert
        Assert.Contains(@"""type"": ""string""", schema.Schema);
        Assert.Contains(@"""format"": ""date-time""", schema.Schema);
    }

    [Fact]
    public void ShouldGenerateUuidSchema_WhenUsingFromTypeWithGuidType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(Guid));

        // Assert
        Assert.Contains(@"""type"": ""string""", schema.Schema);
        Assert.Contains(@"""format"": ""uuid""", schema.Schema);
    }

    [Fact]
    public void ShouldGenerateArraySchema_WhenUsingFromTypeWithArrayType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(int[]));

        // Assert
        Assert.Contains(@"""type"": ""array""", schema.Schema);
        Assert.Contains(@"""items""", schema.Schema);
        Assert.True(schema.Validate("[1, 2, 3]"));
        Assert.False(schema.Validate(@"[""a"", ""b""]"));
    }

    [Fact]
    public void ShouldGenerateArraySchema_WhenUsingFromTypeWithListType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(List<string>));

        // Assert
        Assert.Contains(@"""type"": ""array""", schema.Schema);
        Assert.Contains(@"""items""", schema.Schema);
        Assert.True(schema.Validate(@"[""a"", ""b"", ""c""]"));
        Assert.False(schema.Validate("[1, 2, 3]"));
    }

    [Fact]
    public void ShouldGenerateObjectSchema_WhenUsingFromTypeWithClassType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(TestPerson));

        // Assert
        Assert.Contains(@"""type"": ""object""", schema.Schema);
        Assert.Contains(@"""properties""", schema.Schema);
        Assert.Contains(@"""Name""", schema.Schema);
        Assert.Contains(@"""Age""", schema.Schema);
        Assert.Contains(@"""required""", schema.Schema);

        // Validate correct object
        Assert.True(schema.Validate(@"{ ""Name"": ""John"", ""Age"": 30 }"));

        // Age is required (non-nullable value type)
        Assert.False(schema.Validate(@"{ ""Name"": ""John"" }"));
    }

    [Fact]
    public void ShouldNotRequireProperty_WhenUsingFromTypeWithNullableType()
    {
        // Act
        var schema = JsonSchema.FromType(typeof(TestPersonWithNullable));

        // Assert
        // NullableAge should not be in required list
        var schemaJson = schema.Schema;
        Assert.Contains(@"""Name""", schemaJson);
        if (schemaJson.Contains(@"""required"""))
        {
            Assert.DoesNotContain(@"""NullableAge""", schemaJson.Substring(schemaJson.IndexOf(@"""required""")));
        }
    }

    [Fact]
    public void ShouldReturnSchema_WhenCallingToString()
    {
        // Arrange
        var schemaJson = @"{ ""type"": ""string"" }";
        var schema = JsonSchema.From(schemaJson);

        // Act
        var result = schema.ToString();

        // Assert
        Assert.Equal(schemaJson, result);
    }

    [Fact]
    public void ShouldGenerateCorrectSchemas_WhenUsingFromTypeWithNumericTypes()
    {
        // Act & Assert
        var floatSchema = JsonSchema.FromType(typeof(float));
        Assert.Contains(@"""type"": ""number""", floatSchema.Schema);

        var doubleSchema = JsonSchema.FromType(typeof(double));
        Assert.Contains(@"""type"": ""number""", doubleSchema.Schema);

        var decimalSchema = JsonSchema.FromType(typeof(decimal));
        Assert.Contains(@"""type"": ""number""", decimalSchema.Schema);

        var longSchema = JsonSchema.FromType(typeof(long));
        Assert.Contains(@"""type"": ""integer""", longSchema.Schema);

        var shortSchema = JsonSchema.FromType(typeof(short));
        Assert.Contains(@"""type"": ""integer""", shortSchema.Schema);
    }

    // Test helper classes
    private class TestPerson
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private class TestPersonWithNullable
    {
        public string Name { get; set; } = string.Empty;
        public int? NullableAge { get; set; }
    }
}
