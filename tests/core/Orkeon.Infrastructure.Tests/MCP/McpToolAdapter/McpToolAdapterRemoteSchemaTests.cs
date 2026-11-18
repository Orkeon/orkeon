using Orkeon.Domain.Tools.Protocol;
using McpToolAdapterSut = Orkeon.Infrastructure.MCP.McpToolAdapter;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>What the client keeps of a remote tool's <c>inputSchema</c> (#415), and that no schema makes it throw (#417).</summary>
public sealed class McpToolAdapterRemoteSchemaTests
{
    private static Dictionary<string, ParameterSchema> Convert(string schemaJson) =>
        McpToolAdapterSut.ConvertToToolSchema(
            McpToolAdapterTestsFixture.CreateDefinitionWithSchema("remote", "A remote tool", schemaJson)).Parameters;

    // --- #417 ---

    [Fact]
    public void ShouldReadANullableTypeAsItsOneType_WhenTheTypeIsAnArray()
    {
        var parameters = Convert("""{"type":"object","properties":{"name":{"type":["string","null"]},"other":{"type":["null","integer"]}}}""");

        Assert.Equal("string", parameters["name"].Type);
        Assert.Equal("integer", parameters["other"].Type);
    }

    [Fact]
    public void ShouldLeaveTheTypeOpen_WhenTheTypeArrayNamesSeveralTypes()
    {
        var parameters = Convert("""{"type":"object","properties":{"value":{"type":["string","integer","null"]},"odd":{"type":5}}}""");

        Assert.Null(parameters["value"].Type);
        Assert.Null(parameters["odd"].Type);
    }

    [Fact]
    public void ShouldKeepEveryScalarEnumValue_WhenTheEnumIsNotMadeOfStrings()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "level":{"type":"integer","enum":[1,2,3]},
              "ratio":{"type":"number","enum":[0.5,1.5]},
              "flag":{"type":"boolean","enum":[true,false]},
              "mixed":{"enum":["a",2,null,{"x":1},[1]]}}}
            """);

        Assert.Equal(new object[] { 1L, 2L, 3L }, parameters["level"].Enum);
        Assert.Equal(new object[] { 0.5, 1.5 }, parameters["ratio"].Enum);
        Assert.Equal(new object[] { true, false }, parameters["flag"].Enum);
        Assert.Equal(new object[] { "a", 2L }, parameters["mixed"].Enum);
    }

    [Fact]
    public void ShouldSkipWhatIsNotAString_WhenRequiredOrDescriptionAreMalformed()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "a":{"type":"string","description":["not","a","string"]},
              "b":{"type":"string","description":"kept"}},
             "required":[5,"b",null,{"x":1}]}
            """);

        Assert.Equal("", parameters["a"].Description);
        Assert.False(parameters["a"].Required);
        Assert.Equal("kept", parameters["b"].Description);
        Assert.True(parameters["b"].Required);
    }

    [Fact]
    public void ShouldNotThrow_WhenAPropertySchemaIsNotAnObject()
    {
        var parameters = Convert("""{"type":"object","properties":{"anything":true,"odd":"x"}}""");

        Assert.Null(parameters["anything"].Type);
        Assert.Null(parameters["odd"].Type);
    }

    [Fact]
    public void ShouldDropANumberNoDoubleHolds_WhenAnEnumOrADefaultCarriesOne()
    {
        // Infinity cannot be written back as JSON: the tool list an agent's model reads would fail to serialize.
        var parameters = Convert("""{"type":"object","properties":{"n":{"type":"number","enum":[1,1e400,-1e400,2.5],"default":1e400}}}""");

        Assert.Equal(new object[] { 1L, 2.5 }, parameters["n"].Enum);
        Assert.Null(parameters["n"].Default);
        Assert.NotNull(System.Text.Json.JsonSerializer.Serialize(parameters["n"].ToDocument()));
    }

    // --- #415 ---

    [Fact]
    public void ShouldReadItemsAsObjects_WhenTheyDeclarePropertiesAndNoType()
    {
        var parameters = Convert("""{"type":"object","properties":{"rows":{"type":"array","items":{"properties":{"id":{"type":"integer"}},"required":["id"]}}}}""");

        Assert.Equal("object", parameters["rows"].ItemsType);
        Assert.True(parameters["rows"].ItemProperties!["id"].Required);
        var items = Assert.IsType<Dictionary<string, object>>(parameters["rows"].ToDocument()["items"]);
        Assert.True(items.ContainsKey("properties"));
    }

    [Fact]
    public void ShouldKeepTheItems_WhenTheParameterIsAnArrayOfStrings()
    {
        var parameters = Convert("""{"type":"object","properties":{"dates":{"type":"array","items":{"type":"string","format":"date-time"}}}}""");

        var dates = parameters["dates"];
        Assert.Equal("array", dates.Type);
        Assert.Equal("string", dates.ItemsType);
        Assert.Equal("date-time", dates.ItemsFormat);
        Assert.Null(dates.ItemProperties);
    }

    [Fact]
    public void ShouldKeepTheItemProperties_WhenTheParameterIsAnArrayOfObjects()
    {
        var parameters = Convert("""
            {"type":"object","properties":{"edits":{"type":"array","items":{
              "type":"object",
              "properties":{"path":{"type":"string","description":"The file"},"line":{"type":["integer","null"]}},
              "required":["path"]}}}}
            """);

        var edits = parameters["edits"];
        Assert.Equal("object", edits.ItemsType);
        Assert.NotNull(edits.ItemProperties);
        Assert.Equal("string", edits.ItemProperties["path"].Type);
        Assert.Equal("The file", edits.ItemProperties["path"].Description);
        Assert.True(edits.ItemProperties["path"].Required);
        Assert.Equal("integer", edits.ItemProperties["line"].Type);
        Assert.False(edits.ItemProperties["line"].Required);
        Assert.Equal(["path"], edits.RequiredItemProperties());
    }

    [Fact]
    public void ShouldKeepTheFormat_WhenTheParameterDeclaresOne()
    {
        var parameters = Convert("""{"type":"object","properties":{"when":{"type":"string","format":"date"}}}""");

        Assert.Equal("date", parameters["when"].Format);
    }

    [Fact]
    public void ShouldKeepTheValues_WhenTheParameterIsADictionary()
    {
        var parameters = Convert("""{"type":"object","properties":{"labels":{"type":"object","additionalProperties":{"type":"integer","format":"int32"}}}}""");

        var labels = parameters["labels"];
        Assert.Equal("object", labels.Type);
        Assert.NotNull(labels.Values);
        Assert.Equal("integer", labels.Values.Type);
        Assert.Equal("int32", labels.Values.Format);
    }

    [Fact]
    public void ShouldLeaveTheTypeOpen_WhenThePropertyDeclaresNone()
    {
        var parameters = Convert("""{"type":"object","properties":{"any":{},"either":{"anyOf":[{"type":"string"},{"type":"integer"}]},"ref":{"$ref":"#/$defs/x"}}}""");

        Assert.Null(parameters["any"].Type);
        Assert.Null(parameters["either"].Type);
        Assert.Null(parameters["ref"].Type);
    }

    [Fact]
    public void ShouldPublishWhatItKept_WhenTheSchemaIsWrittenForTheModel()
    {
        var parameters = Convert("""{"type":"object","properties":{"tags":{"type":"array","items":{"type":"string"}}}}""");

        var document = parameters["tags"].ToDocument();

        var items = Assert.IsType<Dictionary<string, object>>(document["items"]);
        Assert.Equal("string", items["type"]);
    }
}
