using System.Reflection;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Domain.Tests.Common;

public class ToolSchemaGeneratorTests
{
    // ── Test types ───────────────────────────────────────────────────

    private enum TestColor { Red, Green, Blue }

    private sealed record TestRequest
    {
        [FieldSchema(Description = "The query text")]
        public string Query { get; init; } = "";

        [FieldSchema(Description = "Max results")]
        public int MaxResults { get; init; } = 10;

        [FieldSchema(Description = "Optional tag")]
        public string? Tag { get; init; }

        [FieldSchema(Description = "Is verbose")]
        public bool Verbose { get; init; }

        [FieldSchema(Description = "Optional count")]
        public int? OptionalCount { get; init; }

        [FieldSchema(Description = "Color choice")]
        public TestColor Color { get; init; }

        [FieldSchema(Description = "Items list")]
        public List<string> Items { get; init; } = [];

        [FieldSchema(Enum = new[] { "asc", "desc" }, Description = "Sort order")]
        public string SortOrder { get; init; } = "asc";

        [FieldSchema(Default = "foo", Description = "With default")]
        public string WithDefault { get; init; } = "foo";

        // No attribute — should be excluded
        public string Ignored { get; init; } = "";
    }

    private sealed record TestResponse
    {
        [ReturnSchema(Description = "Whether it succeeded")]
        public bool Success { get; init; }

        [ReturnSchema(Description = "Result text")]
        public string Result { get; init; } = "";

        [ReturnSchema(Description = "Item count")]
        public int Count { get; init; }

        // No attribute — excluded
        public string Internal { get; init; } = "";
    }

    // Helper records for InferRequired
    private sealed record RequiredTestRecord
    {
        public string NonNullableString { get; init; } = "";
        public string? NullableString { get; init; }
        public int NonNullableInt { get; init; }
        public int? NullableInt { get; init; }
        public bool NonNullableBool { get; init; }
        public bool? NullableBool { get; init; }
    }

    private sealed record ArrayTestRequest
    {
        [FieldSchema(Description = "String array")]
        public string[] StringArray { get; init; } = [];

        [FieldSchema(Description = "Int list")]
        public List<int> IntList { get; init; } = [];

        [FieldSchema(Description = "Guid collection")]
        public ICollection<Guid> GuidCollection { get; init; } = [];

        [FieldSchema(Description = "Enum items")]
        public IEnumerable<int> EnumerableInts { get; init; } = [];
    }

    private class CustomClass { }

    private sealed class ResponseWithDict
    {
        [ReturnSchema(Description = "Some headers")]
        public Dictionary<string, string> Headers { get; set; } = [];
    }

    // ── InferSchemaType tests ────────────────────────────────────────

    [Theory]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(short), "integer")]
    [InlineData(typeof(byte), "integer")]
    [InlineData(typeof(uint), "integer")]
    [InlineData(typeof(ulong), "integer")]
    [InlineData(typeof(ushort), "integer")]
    [InlineData(typeof(sbyte), "integer")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(decimal), "string")]
    [InlineData(typeof(Guid), "string")]
    public void InferSchemaType_PrimitiveTypes_ReturnsExpected(Type type, string expected)
    {
        Assert.Equal(expected, ToolSchemaGenerator.InferSchemaType(type));
    }

    [Theory]
    [InlineData(typeof(DateTime), "string")]
    [InlineData(typeof(DateTimeOffset), "string")]
    [InlineData(typeof(DateOnly), "string")]
    [InlineData(typeof(TimeOnly), "string")]
    [InlineData(typeof(TimeSpan), "string")]
    public void ShouldReturnString_WhenUsingInferSchemaTypeUsingDateTimeTypes(Type type, string expected)
    {
        Assert.Equal(expected, ToolSchemaGenerator.InferSchemaType(type));
    }

    [Fact]
    public void ShouldReturnString_WhenUsingInferSchemaTypeUsingEnumType()
    {
        Assert.Equal("string", ToolSchemaGenerator.InferSchemaType(typeof(TestColor)));
    }

    [Fact]
    public void ShouldReturnString_WhenUsingInferSchemaTypeUsingNullableString()
    {
        // string? is still typeof(string) at runtime, so we test nullable value types
        Assert.Equal("string", ToolSchemaGenerator.InferSchemaType(typeof(string)));
    }

    [Fact]
    public void ShouldReturnInteger_WhenUsingInferSchemaTypeUsingNullableInt()
    {
        Assert.Equal("integer", ToolSchemaGenerator.InferSchemaType(typeof(int?)));
    }

    [Fact]
    public void ShouldReturnBoolean_WhenUsingInferSchemaTypeUsingNullableBool()
    {
        Assert.Equal("boolean", ToolSchemaGenerator.InferSchemaType(typeof(bool?)));
    }

    [Theory]
    [InlineData(typeof(List<string>), "array")]
    [InlineData(typeof(string[]), "array")]
    [InlineData(typeof(IEnumerable<int>), "array")]
    [InlineData(typeof(ICollection<Guid>), "array")]
    public void ShouldReturnArray_WhenUsingInferSchemaTypeUsingCollectionTypes(Type type, string expected)
    {
        Assert.Equal(expected, ToolSchemaGenerator.InferSchemaType(type));
    }

    [Fact]
    public void ShouldReturnObject_WhenUsingInferSchemaTypeUsingObject()
    {
        Assert.Equal("object", ToolSchemaGenerator.InferSchemaType(typeof(object)));
    }

    [Fact]
    public void ShouldReturnObject_WhenUsingInferSchemaTypeWithCustomClass()
    {
        Assert.Equal("object", ToolSchemaGenerator.InferSchemaType(typeof(CustomClass)));
    }

    // ── Dictionary bug fix tests (P2-REF-02) ─────────────────────────

    [Fact]
    public void InferSchemaType_ShouldReturnObject_ForDictionaryStringString()
    {
        Assert.Equal("object", ToolSchemaGenerator.InferSchemaType(typeof(Dictionary<string, string>)));
    }

    [Fact]
    public void InferSchemaType_ShouldReturnObject_ForDictionaryStringObject()
    {
        Assert.Equal("object", ToolSchemaGenerator.InferSchemaType(typeof(Dictionary<string, object?>)));
    }

    [Fact]
    public void InferSchemaType_ShouldReturnArray_ForListOfString()
    {
        // Verify we didn't break regular collections
        Assert.Equal("array", ToolSchemaGenerator.InferSchemaType(typeof(List<string>)));
    }

    [Fact]
    public void GenerateReturns_ShouldReturnObject_ForDictionaryProperty()
    {
        var (returns, _) = ToolSchemaGenerator.GenerateReturnsWithTypes<ResponseWithDict>();
        Assert.True(returns.ContainsKey("headers"));
        var headerSchema = (Dictionary<string, object?>)returns["headers"]!;
        Assert.Equal("object", headerSchema["type"]);
    }

    // ── InferSchemaFormat tests ──────────────────────────────────────

    [Theory]
    [InlineData(typeof(int), "int32")]
    [InlineData(typeof(uint), "int32")]
    [InlineData(typeof(short), "int32")]
    [InlineData(typeof(ushort), "int32")]
    [InlineData(typeof(byte), "int32")]
    [InlineData(typeof(sbyte), "int32")]
    [InlineData(typeof(long), "int64")]
    [InlineData(typeof(ulong), "int64")]
    [InlineData(typeof(float), "float")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(decimal), "decimal")]
    public void ShouldReturnExpected_WhenUsingInferSchemaFormatNumericTypes(Type type, string expected)
    {
        Assert.Equal(expected, ToolSchemaGenerator.InferSchemaFormat(type));
    }

    [Theory]
    [InlineData(typeof(DateTime), "date-time")]
    [InlineData(typeof(DateTimeOffset), "date-time")]
    [InlineData(typeof(DateOnly), "date")]
    [InlineData(typeof(TimeOnly), "time")]
    [InlineData(typeof(TimeSpan), "duration")]
    public void ShouldReturnExpected_WhenUsingInferSchemaFormatDateTimeTypes(Type type, string expected)
    {
        Assert.Equal(expected, ToolSchemaGenerator.InferSchemaFormat(type));
    }

    [Fact]
    public void ShouldReturnUuid_WhenUsingInferSchemaFormatGuid()
    {
        Assert.Equal("uuid", ToolSchemaGenerator.InferSchemaFormat(typeof(Guid)));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(object))]
    public void ShouldReturnNull_WhenUsingInferSchemaFormatWithNoFormatTypes(Type type)
    {
        Assert.Null(ToolSchemaGenerator.InferSchemaFormat(type));
    }

    [Fact]
    public void ShouldReturnInt32_WhenUsingInferSchemaFormatNullableInt()
    {
        Assert.Equal("int32", ToolSchemaGenerator.InferSchemaFormat(typeof(int?)));
    }

    // ── InferRequired tests ─────────────────────────────────────────

    [Fact]
    public void ShouldReturnTrue_WhenUsingInferRequiredWithNonNullableString()
    {
        var prop = typeof(RequiredTestRecord).GetProperty(nameof(RequiredTestRecord.NonNullableString))!;
        Assert.True(ToolSchemaGenerator.InferRequired(prop));
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingInferRequiredNullableString()
    {
        var prop = typeof(RequiredTestRecord).GetProperty(nameof(RequiredTestRecord.NullableString))!;
        Assert.False(ToolSchemaGenerator.InferRequired(prop));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingInferRequiredWithNonNullableInt()
    {
        var prop = typeof(RequiredTestRecord).GetProperty(nameof(RequiredTestRecord.NonNullableInt))!;
        Assert.True(ToolSchemaGenerator.InferRequired(prop));
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingInferRequiredNullableInt()
    {
        var prop = typeof(RequiredTestRecord).GetProperty(nameof(RequiredTestRecord.NullableInt))!;
        Assert.False(ToolSchemaGenerator.InferRequired(prop));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingInferRequiredWithNonNullableBool()
    {
        var prop = typeof(RequiredTestRecord).GetProperty(nameof(RequiredTestRecord.NonNullableBool))!;
        Assert.True(ToolSchemaGenerator.InferRequired(prop));
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingInferRequiredNullableBool()
    {
        var prop = typeof(RequiredTestRecord).GetProperty(nameof(RequiredTestRecord.NullableBool))!;
        Assert.False(ToolSchemaGenerator.InferRequired(prop));
    }

    // ── GetEnumValues tests ─────────────────────────────────────────

    [Fact]
    public void ShouldContainNamesAndIntegers_WhenGettingEnumValuesSimpleEnum()
    {
        var values = ToolSchemaGenerator.GetEnumValues(typeof(TestColor));

        // Red=0, Green=1, Blue=2 → 3 entries as name→value map
        Assert.Equal(3, values.Count);
        Assert.Equal(0, values["Red"]);
        Assert.Equal(1, values["Green"]);
        Assert.Equal(2, values["Blue"]);
    }

    [Fact]
    public void ShouldStillWorks_WhenGettingEnumValuesNullableEnum()
    {
        var values = ToolSchemaGenerator.GetEnumValues(typeof(TestColor?));

        Assert.Equal(3, values.Count);
        Assert.Equal(0, values["Red"]);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingEnumValuesWithNonEnumType()
    {
        var values = ToolSchemaGenerator.GetEnumValues(typeof(string));
        Assert.Empty(values);
    }

    // ── GenerateSchema tests ────────────────────────────────────────

    [Fact]
    public void ShouldSetNameAndDescription_WhenUsingGenerateSchema()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "A test tool");

        Assert.Equal("test_tool", schema.Name);
        Assert.Equal("A test tool", schema.Description);
    }

    [Fact]
    public void ShouldIncludedInParameters_WhenUsingGenerateSchemaUsingFieldSchemaProperties()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.True(schema.Parameters.ContainsKey(ParamQuery));
        Assert.True(schema.Parameters.ContainsKey("max_results"));
        Assert.True(schema.Parameters.ContainsKey("tag"));
        Assert.True(schema.Parameters.ContainsKey("verbose"));
        Assert.True(schema.Parameters.ContainsKey("optional_count"));
        Assert.True(schema.Parameters.ContainsKey("color"));
        Assert.True(schema.Parameters.ContainsKey("items"));
        Assert.True(schema.Parameters.ContainsKey("sort_order"));
        Assert.True(schema.Parameters.ContainsKey("with_default"));
    }

    [Fact]
    public void ShouldExcluded_WhenUsingGenerateSchemaUsingPropertiesWithoutFieldSchema()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.False(schema.Parameters.ContainsKey("ignored"));
    }

    [Fact]
    public void ShouldAreSnakeCase_WhenUsingGenerateSchemaUsingPropertyNames()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        // "MaxResults" → "max_results"
        Assert.True(schema.Parameters.ContainsKey("max_results"));
        Assert.False(schema.Parameters.ContainsKey("MaxResults"));
    }

    [Fact]
    public void ShouldCorrectly_WhenUsingGenerateSchemaUsingTypesInferred()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.Equal("string", schema.Parameters[ParamQuery].Type);
        Assert.Equal("integer", schema.Parameters["max_results"].Type);
        Assert.Equal("boolean", schema.Parameters["verbose"].Type);
        Assert.Equal("string", schema.Parameters["color"].Type);
        Assert.Equal("array", schema.Parameters["items"].Type);
    }

    [Fact]
    public void ShouldRequiredTrue_WhenUsingGenerateSchemaWithNonNullableProperty()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.True(schema.Parameters[ParamQuery].Required);
        Assert.True(schema.Parameters["max_results"].Required);
        Assert.True(schema.Parameters["verbose"].Required);
    }

    [Fact]
    public void ShouldRequiredFalse_WhenUsingGenerateSchemaUsingNullableProperty()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.False(schema.Parameters["tag"].Required);
        Assert.False(schema.Parameters["optional_count"].Required);
    }

    [Fact]
    public void ShouldPopulated_WhenUsingGenerateSchemaUsingExplicitEnumValues()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        var sortEnum = schema.Parameters["sort_order"].Enum;
        Assert.NotNull(sortEnum);
        Assert.Contains("asc", sortEnum.Cast<string>());
        Assert.Contains("desc", sortEnum.Cast<string>());
    }

    [Fact]
    public void ShouldFromEnumType_WhenUsingGenerateSchemaUsingInferredEnumValues()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        var colorEnumMap = schema.Parameters["color"].EnumMap;
        Assert.NotNull(colorEnumMap);
        Assert.Equal(0, colorEnumMap!["Red"]);
        Assert.Equal(1, colorEnumMap["Green"]);
        Assert.Equal(2, colorEnumMap["Blue"]);
    }

    [Fact]
    public void ShouldPopulated_WhenUsingGenerateSchemaWithDefaultValue()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.Equal("foo", schema.Parameters["with_default"].Default);
    }

    [Fact]
    public void ShouldItemsTypePopulated_WhenUsingGenerateSchemaUsingArrayProperty()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.Equal("string", schema.Parameters["items"].ItemsType);
    }

    [Fact]
    public void ShouldItemsTypeInferred_WhenUsingGenerateSchemaUsingArrayPropertyVariants()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<ArrayTestRequest>("arr_tool", "desc");

        Assert.Equal("string", schema.Parameters["string_array"].ItemsType);
        Assert.Equal("integer", schema.Parameters["int_list"].ItemsType);
        Assert.Equal("string", schema.Parameters["guid_collection"].ItemsType); // Guid → string
        Assert.Equal("integer", schema.Parameters["enumerable_ints"].ItemsType);
    }

    [Fact]
    public void ShouldFromAttribute_WhenUsingGenerateSchemaUsingDescription()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.Equal("The query text", schema.Parameters[ParamQuery].Description);
        Assert.Equal("Max results", schema.Parameters["max_results"].Description);
    }

    [Fact]
    public void ShouldInferred_WhenUsingGenerateSchemaFormatting()
    {
        var schema = ToolSchemaGenerator.GenerateSchema<TestRequest>("test_tool", "desc");

        Assert.Equal("int32", schema.Parameters["max_results"].Format);
    }

    // ── GenerateReturns tests ───────────────────────────────────────

    [Fact]
    public void ShouldIncluded_WhenGeneratingReturnsReturnSchemaProperties()
    {
        var returns = ToolSchemaGenerator.GenerateReturns<TestResponse>();

        Assert.True(returns.ContainsKey("success"));
        Assert.True(returns.ContainsKey("result"));
        Assert.True(returns.ContainsKey("count"));
    }

    [Fact]
    public void ShouldExcluded_WhenGeneratingReturnsPropertiesWithoutReturnSchema()
    {
        var returns = ToolSchemaGenerator.GenerateReturns<TestResponse>();

        Assert.False(returns.ContainsKey("internal"));
    }

    [Fact]
    public void ShouldCorrectly_WhenGeneratingReturnsTypeInferred()
    {
        var returns = ToolSchemaGenerator.GenerateReturns<TestResponse>();

        var success = (Dictionary<string, object?>)returns["success"]!;
        Assert.Equal("boolean", success["type"]);

        var result = (Dictionary<string, object?>)returns["result"]!;
        Assert.Equal("string", result["type"]);

        var count = (Dictionary<string, object?>)returns["count"]!;
        Assert.Equal("integer", count["type"]);
    }

    [Fact]
    public void ShouldIncluded_WhenGeneratingReturnsDescription()
    {
        var returns = ToolSchemaGenerator.GenerateReturns<TestResponse>();

        var success = (Dictionary<string, object?>)returns["success"]!;
        Assert.Equal("Whether it succeeded", success["description"]);
    }

    [Fact]
    public void ShouldWhenApplicable_WhenGeneratingReturnsFormatIncluded()
    {
        var returns = ToolSchemaGenerator.GenerateReturns<TestResponse>();

        var count = (Dictionary<string, object?>)returns["count"]!;
        Assert.Equal("int32", count["format"]);

        // string has no format
        var result = (Dictionary<string, object?>)returns["result"]!;
        Assert.False(result.ContainsKey("format"));
    }

    // ── ToSnakeCase tests ───────────────────────────────────────────

    [Theory]
    [InlineData("CoworkerRole", "coworker_role")]
    [InlineData("URL", "u_r_l")]
    [InlineData("name", "name")]
    [InlineData("MaxResults", "max_results")]
    [InlineData("", "")]
    [InlineData("A", "a")]
    [InlineData("alreadySnake", "already_snake")]
    public void ShouldReturnExpected_WhenUsingToSnakeCaseWithVariousInputs(string input, string expected)
    {
        // ToSnakeCase is internal, so we use InternalsVisibleTo or call via GenerateSchema.
        // Since ToolSchemaGenerator.ToSnakeCase is internal static, we access via reflection
        // or rely on the fact that tests project has InternalsVisibleTo.
        var method = typeof(ToolSchemaGenerator).GetMethod("ToSnakeCase",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (string)method.Invoke(null, [input])!;
        Assert.Equal(expected, result);
    }

    // ── Caching tests ───────────────────────────────────────────────

    [Fact]
    public void ShouldReturnSameReference_WhenUsingGenerateSchemaWithSameTypeTwice()
    {
        var schema1 = ToolSchemaGenerator.GenerateSchema<TestRequest>("cached_tool", "desc");
        var schema2 = ToolSchemaGenerator.GenerateSchema<TestRequest>("cached_tool", "desc");

        Assert.Same(schema1, schema2);
    }

    [Fact]
    public void ShouldReturnSameReference_WhenGeneratingReturnsWithSameTypeTwice()
    {
        var returns1 = ToolSchemaGenerator.GenerateReturns<TestResponse>();
        var returns2 = ToolSchemaGenerator.GenerateReturns<TestResponse>();

        Assert.Same(returns1, returns2);
    }

    [Fact]
    public void ShouldReturnDifferentReferences_WhenUsingGenerateSchemaWithDifferentNames()
    {
        var schema1 = ToolSchemaGenerator.GenerateSchema<TestRequest>("tool_a", "desc");
        var schema2 = ToolSchemaGenerator.GenerateSchema<TestRequest>("tool_b", "desc");

        Assert.NotSame(schema1, schema2);
    }
}
