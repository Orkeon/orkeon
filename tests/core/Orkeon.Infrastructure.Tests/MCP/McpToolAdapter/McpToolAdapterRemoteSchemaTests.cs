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

    [Fact]
    public void ShouldKeepADefaultAsTheValueItIs_NeverAsItsText()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "none":{"type":["string","null"],"default":null},
              "whole":{"type":"integer","default":9007199254740993},
              "ratio":{"type":"number","default":0.5},
              "list":{"type":"array","default":["a","b"]},
              "map":{"type":"object","default":{"a":1}}}}
            """);

        Assert.Null(parameters["none"].Default);
        Assert.Equal(9007199254740993L, parameters["whole"].Default);
        Assert.Equal(0.5, parameters["ratio"].Default);
        Assert.Equal("""["a","b"]""", System.Text.Json.JsonSerializer.Serialize(parameters["list"].Default));
        Assert.Equal("""{"a":1}""", System.Text.Json.JsonSerializer.Serialize(parameters["map"].Default));
        Assert.False(parameters["none"].ToDocument().ContainsKey("default"));
    }

    [Fact]
    public void ShouldNotInventObjectItems_WhenTheItemsNameAnotherTypeOrSeveral()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "mixed":{"type":"array","items":{"type":["object","string"],"properties":{"id":{"type":"integer"}}}},
              "texts":{"type":"array","items":{"type":"string","properties":{"id":{"type":"integer"}}}}}}
            """);

        Assert.Null(parameters["mixed"].ItemsType);
        Assert.Null(parameters["mixed"].ItemProperties);
        Assert.Equal("string", parameters["texts"].ItemsType);
        Assert.Null(parameters["texts"].ItemProperties);
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

    // --- The whole schema, kept for 1.3.0 ---

    /// <summary>Thirteen definitions, each naming the next twice: 8192 schemas once expanded, over the budget.</summary>
    internal static string RefBomb()
    {
        var defs = string.Join(",", Enumerable.Range(0, 13).Select(i =>
            "\"d" + i + "\":{\"type\":\"object\",\"properties\":{\"a\":{\"$ref\":\"#/$defs/d" + (i + 1) +
            "\"},\"b\":{\"$ref\":\"#/$defs/d" + (i + 1) + "\"}}}"));
        return "{\"type\":\"object\",\"properties\":{\"x\":{\"$ref\":\"#/$defs/d0\"}},\"$defs\":{" + defs + ",\"d13\":{\"type\":\"string\"}}}";
    }

    [Fact]
    public void ShouldKeepTheProperties_WhenAParameterIsAnObject()
    {
        var schema = McpToolAdapterSut.ConvertToToolSchema(McpToolAdapterTestsFixture.CreateDefinitionWithSchema("remote", "A remote tool", """
            {"type":"object","additionalProperties":false,"properties":{
              "filter":{"type":"object","description":"Filter","additionalProperties":false,"required":["field"],
                "properties":{"field":{"type":"string"},"range":{"properties":{"from":{"type":"integer"}}}}}}}
            """));

        Assert.False(schema.AllowsAdditionalParameters);
        var filter = schema.Parameters["filter"];
        Assert.False(filter.AllowsAdditionalProperties);
        Assert.True(filter.Properties!["field"].Required);
        Assert.False(filter.Properties["range"].Required);
        Assert.Equal("object", filter.Properties["range"].Type); // `properties` and no `type`: an object
        Assert.Equal("integer", filter.Properties["range"].Properties!["from"].Type);
    }

    [Fact]
    public void ShouldKeepTheBounds_InEitherDraft()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "city":{"type":"string","minLength":1,"maxLength":80,"pattern":"^[A-Z]"},
              "days":{"type":"integer","minimum":1,"maximum":10,"exclusiveMaximum":11},
              "ratio":{"type":"number","minimum":0,"exclusiveMinimum":true},
              "tags":{"type":"array","minItems":1,"maxItems":5,"items":{"type":"string"}},
              "odd":{"type":"integer","minimum":"one","maxLength":-1,"minItems":1.5}}}
            """);

        Assert.Equal((1, 80, "^[A-Z]"), (parameters["city"].MinLength, parameters["city"].MaxLength, parameters["city"].Pattern));
        Assert.Equal((1d, 10d, 11d), (parameters["days"].Minimum, parameters["days"].Maximum, parameters["days"].ExclusiveMaximum));
        // Draft 04: `exclusiveMinimum: true` makes `minimum` itself exclusive.
        Assert.Null(parameters["ratio"].Minimum);
        Assert.Equal(0d, parameters["ratio"].ExclusiveMinimum);
        Assert.Equal((1, 5), (parameters["tags"].MinItems, parameters["tags"].MaxItems));
        // A bound of the wrong JSON type, or no count, is no bound.
        Assert.Equal((null, null, null), (parameters["odd"].Minimum, parameters["odd"].MaxLength, parameters["odd"].MinItems));
    }

    [Fact]
    public void ShouldKeepTheWholeItems_WhenAnArrayHoldsArraysOrDescribedValues()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "grid":{"type":"array","items":{"type":"array","items":{"type":"integer","minimum":0}}},
              "tags":{"type":"array","items":{"type":"string","enum":["a","b"],"description":"A tag","default":"a"}},
              "pair":{"type":"array","items":[{"type":"string"},{"type":"integer"}]},
              "none":{"type":"array","items":false}}}
            """);

        Assert.Equal("array", parameters["grid"].ItemsType);
        Assert.Equal("integer", parameters["grid"].Items!.Items!.Type);
        Assert.Equal(0d, parameters["grid"].Items!.Items!.Minimum);
        var tag = parameters["tags"].Items!;
        Assert.Equal(["a", "b"], tag.Enum!.Cast<string>());
        Assert.Equal(("A tag", "a"), (tag.Description, tag.Default));
        // A tuple is the alternatives of its positions; the position itself is lost.
        Assert.Equal(["string", "integer"], parameters["pair"].Items!.AnyOf!.Select(a => a.Type));
        Assert.Equal(0, parameters["none"].MaxItems);
    }

    [Fact]
    public void ShouldKeepTheAlternatives_AndReadANullableOneAsItsSchema()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "when":{"description":"A date or a day","anyOf":[{"type":"string","format":"date-time"},{"type":"integer"}]},
              "pick":{"oneOf":[{"const":"a"},{"const":"b"}]},
              "maybe":{"description":"Optional","anyOf":[{"type":"string","maxLength":3},{"type":"null"}],"default":null},
              "wrapped":{"description":"Wrapped","allOf":[{"type":"integer","minimum":2}]}}}
            """);

        Assert.Null(parameters["when"].Type);
        Assert.Equal("A date or a day", parameters["when"].Description);
        Assert.Equal(["string", "integer"], parameters["when"].AnyOf!.Select(a => a.Type));
        Assert.Equal("date-time", parameters["when"].AnyOf![0].Format);
        Assert.Equal(["a", "b"], parameters["pick"].AnyOf!.Select(a => Assert.Single(a.Enum!)));
        // Pydantic's Optional[str]: the string, with the property's own description.
        Assert.Equal(("string", 3, "Optional", null), (parameters["maybe"].Type, parameters["maybe"].MaxLength, parameters["maybe"].Description, parameters["maybe"].AnyOf));
        Assert.Equal(("integer", 2d, "Wrapped"), (parameters["wrapped"].Type, parameters["wrapped"].Minimum, parameters["wrapped"].Description));
    }

    [Fact]
    public void ShouldResolveTheLocalReferences_AndStopAtACycle()
    {
        var schema = McpToolAdapterSut.ConvertToToolSchema(McpToolAdapterTestsFixture.CreateDefinitionWithSchema("remote", "A remote tool", """
            {"$ref":"#/$defs/Input","$defs":{
              "Input":{"type":"object","required":["owner"],"properties":{
                "owner":{"$ref":"#/$defs/User","description":"Who owns it"},
                "tree":{"$ref":"#/definitions/Node"},
                "elsewhere":{"$ref":"https://example.com/schema.json","description":"Not followed"}}},
              "User":{"type":"object","description":"A user","properties":{"login":{"type":"string"}}}},
             "definitions":{"Node":{"type":"object","properties":{"children":{"type":"array","items":{"$ref":"#/definitions/Node"}}}}}}
            """));

        var owner = schema.Parameters["owner"];
        Assert.True(owner.Required);
        Assert.Equal(("object", "Who owns it"), (owner.Type, owner.Description)); // its own words over the definition's
        Assert.Equal("string", owner.Properties!["login"].Type);
        // A node that holds nodes: the node is read once, and the nodes it holds accept any value rather
        // than expanding without end.
        var children = schema.Parameters["tree"].Properties!["children"];
        Assert.Equal("array", children.Type);
        Assert.Null(children.Items!.Type);
        Assert.Equal((null, "Not followed"), (schema.Parameters["elsewhere"].Type, schema.Parameters["elsewhere"].Description));
    }

    [Fact]
    public void ShouldRefuseTheSchema_WhenItsReferencesExpandBeyondTheBudget()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Convert(RefBomb()));

        Assert.Contains($"beyond {McpToolAdapterSut.MaxSchemaNodes} schemas", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldPublishWhatItRead_AsTheJsonSchemaTheModelIsShown()
    {
        const string remote = """
            {"type":"object","additionalProperties":false,"required":["city"],"properties":{
              "city":{"type":"string","description":"City","minLength":1,"pattern":"^[A-Z]"},
              "grid":{"type":"array","items":{"type":"array","items":{"type":"integer"}}},
              "filter":{"type":"object","additionalProperties":false,"required":["field"],"properties":{"field":{"type":"string"}}},
              "when":{"anyOf":[{"type":"string","format":"date-time"},{"type":"integer","minimum":0}]},
              "loose":{"type":"array"}}}
            """;

        var published = System.Text.Json.JsonSerializer.Serialize(
            McpToolAdapterSut.ConvertToToolSchema(McpToolAdapterTestsFixture.CreateDefinitionWithSchema("remote", "A remote tool", remote)).ToInputJsonSchema());

        Assert.Equal(
            """{"type":"object","properties":{"city":{"type":"string","description":"City","minLength":1,"pattern":"^[A-Z]"},"grid":{"type":"array","items":{"type":"array","items":{"type":"integer"}}},"filter":{"type":"object","properties":{"field":{"type":"string"}},"required":["field"],"additionalProperties":false},"when":{"anyOf":[{"type":"string","format":"date-time"},{"type":"integer","minimum":0}]},"loose":{"type":"array","items":{}}},"required":["city"],"additionalProperties":false}""",
            published);
    }

    // --- The review of #415 / #417 ---

    private static ToolSchema ConvertWhole(string schemaJson) =>
        McpToolAdapterSut.ConvertToToolSchema(McpToolAdapterTestsFixture.CreateDefinitionWithSchema("remote", "A remote tool", schemaJson));

    /// <summary>One definition with a 16 KiB description, named by 100 properties: 200 schemas, but 1.6 MiB of text once copied at each use.</summary>
    internal static string TextBomb(string salt = "")
    {
        var description = salt + new string('x', 16 * 1024);
        var properties = string.Join(",", Enumerable.Range(0, 100).Select(i => "\"p" + i + "\":{\"$ref\":\"#/$defs/big\"}"));
        return "{\"type\":\"object\",\"properties\":{" + properties + "},\"$defs\":{\"big\":{\"type\":\"string\",\"description\":\"" + description + "\"}}}";
    }

    [Fact]
    public void ShouldRefuseTheSchema_WhenTheTextItCopiesExceedsTheBudget()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Convert(TextBomb()));

        Assert.Contains($"beyond {McpToolAdapterSut.MaxSchemaTextBytes} bytes of text", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRefuseTheSchema_WhenOneDescriptionExceedsTheTextBudget()
    {
        var huge = new string('y', McpToolAdapterSut.MaxSchemaTextBytes + 1);

        var ex = Assert.Throws<InvalidOperationException>(() => Convert("{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\",\"description\":\"" + huge + "\"}}}"));

        Assert.Contains("bytes of text", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldShareADefinitionReadSeveralTimes_AndStillCountItAtEachUse()
    {
        var shared = Convert("""
            {"type":"object","properties":{"a":{"$ref":"#/$defs/P"},"b":{"$ref":"#/$defs/P","description":"Its own"}},
             "$defs":{"P":{"type":"object","properties":{"x":{"type":"integer"},"y":{"type":"string"}}}}}
            """);

        // Read once: the two uses share the properties read; each keeps its own words.
        Assert.Same(shared["a"].Properties, shared["b"].Properties);
        Assert.Equal("Its own", shared["b"].Description);

        // 50 uses of a 100-schema definition: shared in memory, but written out 50 times for the model.
        var leaves = string.Join(",", Enumerable.Range(0, 99).Select(i => "\"f" + i + "\":{\"type\":\"integer\"}"));
        var uses = string.Join(",", Enumerable.Range(0, 50).Select(i => "\"u" + i + "\":{\"$ref\":\"#/$defs/D\"}"));
        var ex = Assert.Throws<InvalidOperationException>(() => Convert(
            "{\"type\":\"object\",\"properties\":{" + uses + "},\"$defs\":{\"D\":{\"type\":\"object\",\"properties\":{" + leaves + "}}}}"));
        Assert.Contains($"beyond {McpToolAdapterSut.MaxSchemaNodes} schemas", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"anyOf":[{"type":"object","required":["a"],"properties":{"a":{"type":"string"}}},{"type":"null"}]}""")]
    [InlineData("""{"allOf":[{"type":"object","required":["a"],"properties":{"a":{"type":"string"}}}]}""")]
    [InlineData("""{"$ref":"#/$defs/W","$defs":{"W":{"allOf":[{"$ref":"#/$defs/I"}]},"I":{"type":"object","required":["a"],"properties":{"a":{"type":"string"}}}}}""")]
    [InlineData("""{"type":["object","null"],"required":["a"],"properties":{"a":{"type":"string"}}}""")]
    public void ShouldGiveTheToolItsParameters_WhenTheRootStandsForOneObject(string root)
    {
        var parameters = ConvertWhole(root).Parameters;

        Assert.Equal("string", parameters["a"].Type);
        Assert.True(parameters["a"].Required);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("""[{"type":"object"}]""")]
    [InlineData("""{"type":"string","properties":{"a":{}}}""")]
    public void ShouldGiveNoParameter_WhenTheRootIsNoObjectSchema(string root)
    {
        var schema = ConvertWhole(root);

        Assert.Empty(schema.Parameters);
        Assert.True(schema.AllowsAdditionalParameters);
    }

    [Fact]
    public void ShouldMergeTheMembersOfAnAllOf_WhenEachIsAnObject()
    {
        var schema = ConvertWhole("""
            {"type":"object","properties":{"own":{"type":"boolean"}},"allOf":[
               {"$ref":"#/$defs/A"},
               {"type":"object","description":"Second","required":["a"],"additionalProperties":false,"properties":{"b":{"type":"integer"},"a":{"type":"number"}}}],
             "$defs":{"A":{"type":"object","description":"First","properties":{"a":{"type":"string"}}}}}
            """);

        Assert.Equal(["a", "b", "own"], schema.Parameters.Keys.Order());
        Assert.Equal("string", schema.Parameters["a"].Type); // the first member that names it
        Assert.True(schema.Parameters["a"].Required);         // required by another member
        Assert.False(schema.Parameters["b"].Required);
        Assert.False(schema.AllowsAdditionalParameters);

        var nested = Convert("""{"type":"object","properties":{"p":{"allOf":[{"type":"object","description":"First","properties":{"x":{}}},{"properties":{"y":{}}}]}}}""")["p"];
        Assert.Equal(("object", "First"), (nested.Type, nested.Description));
        Assert.Equal(["x", "y"], nested.Properties!.Keys.Order());
    }

    [Fact]
    public void ShouldLeaveAnAllOfAsBefore_WhenAMemberIsNoObject()
    {
        var parameters = Convert("""{"type":"object","properties":{"p":{"allOf":[{"type":"object","properties":{"x":{}}},{"type":"string"}]}}}""");

        Assert.Null(parameters["p"].Type);
        Assert.Null(parameters["p"].Properties);
    }

    [Fact]
    public void ShouldReadEveryNullSchema_AsTheNullAlternative()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "byEnum":{"anyOf":[{"type":"string"},{"enum":[null]}]},
              "byConst":{"oneOf":[{"type":"integer"},{"const":null}]},
              "byTypes":{"anyOf":[{"type":"boolean"},{"type":["null"]}]}}}
            """);

        Assert.Equal(("string", null), (parameters["byEnum"].Type, parameters["byEnum"].AnyOf));
        Assert.Equal(("integer", null), (parameters["byConst"].Type, parameters["byConst"].AnyOf));
        Assert.Equal(("boolean", null), (parameters["byTypes"].Type, parameters["byTypes"].AnyOf));
    }

    [Fact]
    public void ShouldReadPrefixItems_AndTheItemsThatFollowThem()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "row":{"type":"array","prefixItems":[{"type":"string"},{"type":"boolean"}],"items":{"type":"integer"}},
              "pair":{"type":"array","prefixItems":[{"type":"string"},{"type":"integer"}],"items":false}}}
            """);

        Assert.Equal(["string", "boolean", "integer"], parameters["row"].Items!.AnyOf!.Select(a => a.Type));
        Assert.Equal(["string", "integer"], parameters["pair"].Items!.AnyOf!.Select(a => a.Type));
        Assert.Equal(2, parameters["pair"].MaxItems);
    }

    [Fact]
    public void ShouldKeepTheBoundsAndEnumBesideAReference_WhereTheTargetHasNone()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "code":{"$ref":"#/$defs/S","maxLength":5,"minLength":9,"enum":["ab","cd"],"pattern":"^[a-z]+$","minimum":1}},
             "$defs":{"S":{"type":"string","minLength":2}}}
            """);

        var code = parameters["code"];
        Assert.Equal((2, 5, "^[a-z]+$", 1d), (code.MinLength, code.MaxLength, code.Pattern, code.Minimum)); // the target's own bound wins
        Assert.Equal(["ab", "cd"], code.Enum!.Cast<string>());
    }

    [Fact]
    public void ShouldFollowAJsonPointer_AsRfc6901WritesIt()
    {
        var parameters = Convert("""
            {"type":"object","properties":{
              "slash":{"$ref":"#/$defs/a~1b"},
              "tilde":{"$ref":"#/$defs/c~0d"},
              "dollar":{"$ref":"#/%24defs/plain"},
              "empty":{"$ref":"#/"},
              "inner":{"$ref":"#/n//m"},
              "index":{"$ref":"#/$defs/list/1"},
              "zero":{"$ref":"#/$defs/list/0"},
              "leading":{"$ref":"#/$defs/list/01"},
              "signed":{"$ref":"#/$defs/list/+1"},
              "anchor":{"$ref":"#plain"}},
             "":{"type":"number"},
             "n":{"":{"m":{"type":"boolean"}}},
             "$defs":{"a/b":{"type":"string"},"c~d":{"type":"integer"},"plain":{"type":"object","properties":{"k":{}}},
                      "list":[{"type":"null"},{"type":"array"}]}}
            """);

        Assert.Equal("string", parameters["slash"].Type);
        Assert.Equal("integer", parameters["tilde"].Type);
        Assert.Equal("object", parameters["dollar"].Type);
        Assert.Equal("number", parameters["empty"].Type);
        Assert.Equal("boolean", parameters["inner"].Type);
        Assert.Equal("array", parameters["index"].Type);
        Assert.Equal("null", parameters["zero"].Type);
        Assert.Null(parameters["leading"].Type);
        Assert.Null(parameters["signed"].Type);
        Assert.Null(parameters["anchor"].Type);
    }

    [Fact]
    public void ShouldKeepTheProperties_WhenTheTypeIsANullableObject()
    {
        var parameters = Convert("""{"type":"object","properties":{"o":{"type":["object","null"],"properties":{"id":{"type":"integer"}},"required":["id"]}}}""");

        Assert.Equal("object", parameters["o"].Type);
        Assert.True(parameters["o"].Properties!["id"].Required);
    }

    [Fact]
    public void ShouldChargeTheBooleansOfAnEnum_AgainstTheTextBudget()
    {
        // 300 uses of a definition whose enum holds 1000 booleans: 300 000 members, about 1.5 MB once written.
        var booleans = string.Join(",", Enumerable.Repeat("true,false", 500));
        var properties = string.Join(",", Enumerable.Range(0, 300).Select(i => "\"p" + i + "\":{\"$ref\":\"#/$defs/Flag\"}"));
        var schema = "{\"type\":\"object\",\"properties\":{" + properties + "},\"$defs\":{\"Flag\":{\"enum\":[" + booleans + "]}}}";

        var ex = Assert.Throws<InvalidOperationException>(() => Convert(schema));

        Assert.Contains($"beyond {McpToolAdapterSut.MaxSchemaTextBytes} bytes", ex.Message, StringComparison.Ordinal);
    }
}
