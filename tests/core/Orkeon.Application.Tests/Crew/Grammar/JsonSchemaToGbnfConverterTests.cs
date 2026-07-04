using Orkeon.Application.Crew.Grammar;

namespace Orkeon.Application.Tests.Crew.Grammar;

public class JsonSchemaToGbnfConverterTests
{
    [Fact]
    public void Convert_ObjectWithRequiredString_ProducesRootRule()
    {
        const string schema = """
            { "type": "object",
              "properties": { "name": { "type": "string" } },
              "required": ["name"] }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        Assert.StartsWith("root ::=", grammar);
        Assert.Contains("\"\\\"name\\\"\"", grammar);
        Assert.Contains("string ::=", grammar);
        Assert.Contains("ws ::=", grammar);
    }

    [Fact]
    public void Convert_IntegerField_IncludesIntegerRule()
    {
        const string schema = """
            { "type": "object",
              "properties": { "count": { "type": "integer" } },
              "required": ["count"] }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        Assert.Contains("integer ::=", grammar);
        Assert.Contains("\"\\\"count\\\"\"", grammar);
    }

    [Fact]
    public void Convert_ArrayOfStrings_ProducesArrayRule()
    {
        const string schema = """
            { "type": "object",
              "properties": { "tags": { "type": "array", "items": { "type": "string" } } },
              "required": ["tags"] }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        Assert.Contains("arr-", grammar);
        Assert.Contains("\"[\"", grammar);
        Assert.Contains("\"]\"", grammar);
    }

    [Fact]
    public void Convert_EnumString_EmitsAlternation()
    {
        const string schema = """
            { "type": "object",
              "properties": { "status": { "type": "string", "enum": ["ok", "fail"] } },
              "required": ["status"] }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        Assert.Contains("\"\\\"ok\\\"\"", grammar);
        Assert.Contains("\"\\\"fail\\\"\"", grammar);
    }

    [Fact]
    public void Convert_RequiredAndOptionalProperties_OptionalWrappedInParenQuestion()
    {
        const string schema = """
            { "type": "object",
              "properties": {
                "id": { "type": "string" },
                "note": { "type": "string" }
              },
              "required": ["id"] }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        Assert.Contains("\"\\\"id\\\"\"", grammar);
        Assert.Contains("\"\\\"note\\\"\"", grammar);
        Assert.Contains(")?", grammar);
    }

    [Fact]
    public void Convert_RefToDefinitions_Resolved()
    {
        const string schema = """
            { "type": "object",
              "properties": { "user": { "$ref": "#/definitions/User" } },
              "required": ["user"],
              "definitions": {
                "User": { "type": "object",
                          "properties": { "email": { "type": "string" } },
                          "required": ["email"] }
              } }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        Assert.Contains("ref-", grammar);
        Assert.Contains("\"\\\"email\\\"\"", grammar);
    }

    [Fact]
    public void Convert_UnknownRef_Throws()
    {
        const string schema = """
            { "type": "object",
              "properties": { "x": { "$ref": "#/definitions/Missing" } },
              "required": ["x"] }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => JsonSchemaToGbnfConverter.Convert(schema));
        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public void Convert_NullInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => JsonSchemaToGbnfConverter.Convert("   "));
    }

    [Fact]
    public void Convert_BooleanAndNumber_EmitsPrimitiveRules()
    {
        const string schema = """
            { "type": "object",
              "properties": {
                "flag": { "type": "boolean" },
                "score": { "type": "number" }
              },
              "required": ["flag", "score"] }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        Assert.Contains("boolean ::=", grammar);
        Assert.Contains("number ::=", grammar);
    }

    [Fact]
    public void Convert_NestedObject_EmitsNestedRule()
    {
        const string schema = """
            { "type": "object",
              "properties": {
                "meta": {
                  "type": "object",
                  "properties": { "v": { "type": "integer" } },
                  "required": ["v"]
                }
              },
              "required": ["meta"] }
            """;

        var grammar = JsonSchemaToGbnfConverter.Convert(schema);

        var objRuleCount = grammar.Split("obj-").Length - 1;
        Assert.True(objRuleCount >= 2, $"expected ≥2 'obj-' rule references, got {objRuleCount}");
    }
}
