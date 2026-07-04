using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tools.Abstractions.Base;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Abstractions.Tests.Base;

public class ToolSchemaYamlTests
{
    // ── Test types ───────────────────────────────────────────────────

    public record SampleRequest
    {
        [FieldSchema(Description = "The search query")]
        public string Query { get; init; } = "";

        [FieldSchema(Description = "Max results", IsRequired = false)]
        public int TopK { get; init; } = 5;
    }

    public record SampleNestedItem
    {
        public string Id { get; init; } = "";
        public float Score { get; init; }
        public List<string> Tags { get; init; } = [];
    }

    public record SampleResponse
    {
        public string Query { get; init; } = "";
        public List<SampleNestedItem> Results { get; init; } = [];
        public int TotalCount { get; init; }
    }

    // Cross-referencing types
    public record ParentType
    {
        public string Name { get; init; } = "";
        public ChildType? Child { get; init; }
    }

    public record ChildType
    {
        public string Value { get; init; } = "";
        public ParentType? Parent { get; init; }
    }

    // ── Tests ────────────────────────────────────────────────────────

    [Fact]
    public void ShouldProduceValidYamlWithNameAndDescription_WhenConvertingToYaml()
    {
        var schema = BuildFullSchema<SampleRequest, SampleResponse>(
            "sample_tool", "A sample tool for testing");

        var yaml = schema.ToYaml();

        Assert.Contains("name: sample_tool", yaml);
        Assert.Contains("description: A sample tool for testing", yaml);
    }

    [Fact]
    public void ShouldIncludeParametersWithTypes_WhenConvertingToYaml()
    {
        var schema = BuildFullSchema<SampleRequest, SampleResponse>(
            "sample_tool", "A sample tool");

        var yaml = schema.ToYaml();

        Assert.Contains("query:", yaml);
        Assert.Contains("type: string", yaml);
        Assert.Contains("top_k:", yaml);
        Assert.Contains("type: integer", yaml);
    }

    [Fact]
    public void ShouldIncludeReturnsWithRefs_WhenConvertingToYaml()
    {
        var schema = BuildFullSchema<SampleRequest, SampleResponse>(
            "sample_tool", "A sample tool");

        var yaml = schema.ToYaml();

        // Returns section should have $ref for nested object
        Assert.Contains("returns:", yaml);
        Assert.Contains("total_count:", yaml);
        Assert.Contains("$ref", yaml);
        Assert.Contains("#/types/sample_nested_item", yaml);
    }

    [Fact]
    public void ShouldIncludeTypesWithProperties_WhenConvertingToYaml()
    {
        var schema = BuildFullSchema<SampleRequest, SampleResponse>(
            "sample_tool", "A sample tool");

        var yaml = schema.ToYaml();

        // Types section should describe SampleNestedItem
        Assert.Contains("types:", yaml);
        Assert.Contains("sample_nested_item:", yaml);
        Assert.Contains("id:", yaml);
        Assert.Contains("score:", yaml);
        Assert.Contains("tags:", yaml);
    }

    [Fact]
    public void ShouldHandleCircularReferences_WhenConvertingToYaml()
    {
        var schema = BuildFullSchema<SampleRequest, SampleResponse>(
            "parent_tool", "Tool with circular types");

        // Force generation of ParentType which references ChildType which references ParentType
        var (returns, types) = ToolSchemaGenerator.GenerateReturnsWithTypes<ParentType>();

        // Both types should be registered
        Assert.True(types.ContainsKey("child_type"), "child_type should be in types");
        // ParentType is the root → its properties are in returns, but ChildType.Parent → $ref
    }

    [Fact]
    public void ShouldProduceCleanDictionaryWithNoNullValues_WhenConvertingToDocument()
    {
        var schema = new ToolSchema(
            "test", "desc",
            new Dictionary<string, ParameterSchema>
            {
                ["input"] = new("string", "The input", true)
            });

        var doc = schema.ToDocument();

        Assert.Equal("test", doc["name"]);
        Assert.Equal("desc", doc["description"]);
        Assert.True(doc.ContainsKey("parameters"));
        Assert.False(doc.ContainsKey("returns"));  // null → omitted
        Assert.False(doc.ContainsKey("types"));     // null → omitted
    }

    [Fact]
    public void ShouldProduceReadableAndOrderedOutput_WhenConvertingToYaml()
    {
        var schema = BuildFullSchema<SampleRequest, SampleResponse>(
            "semantic_search", "Perform semantic search");

        var yaml = schema.ToYaml();

        // Verify structure order: name before description before parameters
        var nameIdx = yaml.IndexOf("name:");
        var descIdx = yaml.IndexOf("description:");
        var paramIdx = yaml.IndexOf("parameters:");
        var returnsIdx = yaml.IndexOf("returns:");
        var typesIdx = yaml.IndexOf("types:");

        Assert.True(nameIdx < descIdx, "name should come before description");
        Assert.True(descIdx < paramIdx, "description should come before parameters");
        Assert.True(paramIdx < returnsIdx, "parameters should come before returns");
        Assert.True(returnsIdx < typesIdx, "returns should come before types");
    }

    // ── Round-trip: YAML → ToolSchema → validation ─────────────────

    [Fact]
    public void ShouldPreserveAllFields_WhenRoundTrippingThroughYaml()
    {
        // Arrange: generate YAML from C# types
        var original = BuildFullSchema<SampleRequest, SampleResponse>(
            "round_trip_tool", "Test round-trip");
        var yaml = original.ToYaml();

        // Act: deserialize back
        var loaded = ToolSchemaYamlExtensions.FromYaml(yaml);

        // Assert: all fields preserved
        Assert.Equal("round_trip_tool", loaded.Name);
        Assert.Equal("Test round-trip", loaded.Description);
        Assert.Equal(2, loaded.Parameters.Count);
        Assert.True(loaded.Parameters.ContainsKey(ParamQuery));
        Assert.True(loaded.Parameters.ContainsKey("top_k"));

        // Parameter details
        Assert.Equal("string", loaded.Parameters[ParamQuery].Type);
        Assert.True(loaded.Parameters[ParamQuery].Required);
        Assert.Equal("integer", loaded.Parameters["top_k"].Type);
        Assert.False(loaded.Parameters["top_k"].Required);
        Assert.Equal("int32", loaded.Parameters["top_k"].Format);
    }

    [Fact]
    public void ShouldPreserveReturnsAndTypes_WhenLoadingFromYaml()
    {
        var original = BuildFullSchema<SampleRequest, SampleResponse>(
            "typed_tool", "With returns");
        var yaml = original.ToYaml();

        var loaded = ToolSchemaYamlExtensions.FromYaml(yaml);

        Assert.NotNull(loaded.Returns);
        Assert.True(loaded.Returns!.ContainsKey(ParamQuery));
        Assert.True(loaded.Returns.ContainsKey("results"));
        Assert.True(loaded.Returns.ContainsKey("total_count"));

        Assert.NotNull(loaded.Types);
        Assert.True(loaded.Types!.ContainsKey("sample_nested_item"));
    }

    [Fact]
    public void ShouldValidateParameters_WhenSchemaIsLoadedFromYaml()
    {
        var yaml = @"
name: my_tool
description: A tool loaded from YAML
parameters:
  query:
    type: string
    description: Search query
    required: true
  limit:
    type: integer
    description: Max results
    required: false
    format: int32
";
        var schema = ToolSchemaYamlExtensions.FromYaml(yaml);

        // Valid parameters
        var (isValid, _) = schema.ValidateParameters(
            new Dictionary<string, object> { [ParamQuery] = "test search" });
        Assert.True(isValid, "Valid params should pass");

        // Missing required parameter
        var (isInvalid, error) = schema.ValidateParameters(
            new Dictionary<string, object> { ["limit"] = 10 });
        Assert.False(isInvalid, "Missing 'query' should fail");
        Assert.Contains(ParamQuery, error);
    }

    [Fact]
    public void ShouldValidateCorrectly_WhenYamlContainsEnumValues()
    {
        var yaml = @"
name: format_tool
description: Format data
parameters:
  format:
    type: string
    description: Output format
    required: true
    enum:
      - json
      - xml
      - csv
";
        var schema = ToolSchemaYamlExtensions.FromYaml(yaml);

        Assert.NotNull(schema.Parameters["format"].Enum);
        Assert.Equal(3, schema.Parameters["format"].Enum!.Count);

        // Valid enum value
        var (valid, _) = schema.ValidateParameters(
            new Dictionary<string, object> { ["format"] = "json" });
        Assert.True(valid);

        // Invalid enum value
        var (invalid, enumError) = schema.ValidateParameters(
            new Dictionary<string, object> { ["format"] = "yaml" });
        Assert.False(invalid);
        Assert.Contains("must be one of", enumError);
    }

    [Fact]
    public void ShouldWork_WhenLoadingMinimalSchemaFromYaml()
    {
        var yaml = @"
name: simple_tool
description: Just a name
parameters: {}
";
        var schema = ToolSchemaYamlExtensions.FromYaml(yaml);

        Assert.Equal("simple_tool", schema.Name);
        Assert.Equal("Just a name", schema.Description);
        Assert.Empty(schema.Parameters);
        Assert.Null(schema.Returns);
        Assert.Null(schema.Types);
    }

    // ── Helper ───────────────────────────────────────────────────────

    private static ToolSchema BuildFullSchema<TReq, TRes>(string name, string description)
        where TReq : class where TRes : class
    {
        var paramSchema = ToolSchemaGenerator.GenerateSchema<TReq>(name, description);
        var (returns, types) = ToolSchemaGenerator.GenerateReturnsWithTypes<TRes>();
        return paramSchema with
        {
            Returns = returns.Count > 0 ? returns : null,
            Types = types.Count > 0 ? types : null
        };
    }
}
