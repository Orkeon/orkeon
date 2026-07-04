using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.Tests.CovToolCalling;

public class CovToolCalling_AnthropicToolSchemaFormatterTests
{
    private readonly AnthropicToolSchemaFormatter _formatter = new();

    private static ToolSchema SimpleSchema() => new(
        "search",
        "Search the web",
        new Dictionary<string, ParameterSchema>
        {
            ["query"] = new("string", "The query", Required: true),
            ["limit"] = new("integer", "Max results", Required: false, Default: 10)
        });

    [Fact]
    public void FormatTools_Auto_AddsAutoToolChoice()
    {
        var payload = _formatter.FormatToolsForPayload([SimpleSchema()], ToolCallMode.Auto);

        var choice = Assert.IsType<Dictionary<string, object>>(payload["tool_choice"]);
        Assert.Equal("auto", choice["type"]);
        var tools = Assert.IsType<List<Dictionary<string, object>>>(payload["tools"]);
        Assert.Single(tools);
    }

    [Fact]
    public void FormatTools_Required_AddsAnyToolChoice()
    {
        var payload = _formatter.FormatToolsForPayload([SimpleSchema()], ToolCallMode.Required);
        var choice = Assert.IsType<Dictionary<string, object>>(payload["tool_choice"]);
        Assert.Equal("any", choice["type"]);
    }

    [Fact]
    public void FormatTools_None_OmitsToolChoiceAndEmptiesTools()
    {
        var payload = _formatter.FormatToolsForPayload([SimpleSchema()], ToolCallMode.None);

        Assert.False(payload.ContainsKey("tool_choice"));
        var tools = Assert.IsType<List<Dictionary<string, object>>>(payload["tools"]);
        Assert.Empty(tools);
    }

    [Fact]
    public void FormatTools_DefaultModeIsAuto()
    {
        var payload = _formatter.FormatToolsForPayload([SimpleSchema()]);
        Assert.True(payload.ContainsKey("tool_choice"));
    }

    [Fact]
    public void FormatSingleTool_BuildsNameDescriptionInputSchema()
    {
        var payload = _formatter.FormatToolsForPayload([SimpleSchema()], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];

        Assert.Equal("search", tool["name"]);
        Assert.Equal("Search the web", tool["description"]);

        var inputSchema = Assert.IsType<Dictionary<string, object>>(tool["input_schema"]);
        Assert.Equal("object", inputSchema["type"]);
        var props = Assert.IsType<Dictionary<string, object>>(inputSchema["properties"]);
        Assert.True(props.ContainsKey("query"));
        Assert.True(props.ContainsKey("limit"));

        var required = Assert.IsType<List<string>>(inputSchema["required"]);
        Assert.Contains("query", required);
        Assert.DoesNotContain("limit", required);
    }

    [Fact]
    public void FormatSingleTool_NoRequiredParams_OmitsRequiredKey()
    {
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>
        {
            ["opt"] = new("string", "optional", Required: false)
        });

        var payload = _formatter.FormatToolsForPayload([schema], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];
        var inputSchema = Assert.IsType<Dictionary<string, object>>(tool["input_schema"]);

        Assert.False(inputSchema.ContainsKey("required"));
    }

    [Fact]
    public void FormatParameterProperty_IncludesDefault()
    {
        var payload = _formatter.FormatToolsForPayload([SimpleSchema()], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];
        var props = (Dictionary<string, object>)((Dictionary<string, object>)tool["input_schema"])["properties"];
        var limit = Assert.IsType<Dictionary<string, object>>(props["limit"]);

        Assert.Equal("integer", limit["type"]);
        Assert.Equal("Max results", limit["description"]);
        Assert.Equal(10, limit["default"]);
    }

    [Fact]
    public void FormatParameterProperty_EnumMapTakesPriority()
    {
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>
        {
            ["mode"] = new("string", "the mode", Required: true,
                Enum: ["ignored"],
                EnumMap: new Dictionary<string, int> { ["Fast"] = 0, ["Slow"] = 1 })
        });

        var payload = _formatter.FormatToolsForPayload([schema], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];
        var props = (Dictionary<string, object>)((Dictionary<string, object>)tool["input_schema"])["properties"];
        var mode = (Dictionary<string, object>)props["mode"];

        var enumValues = Assert.IsType<List<string>>(mode["enum"]);
        Assert.Contains("Fast", enumValues);
        Assert.Contains("Slow", enumValues);
        Assert.DoesNotContain("ignored", enumValues);
    }

    [Fact]
    public void FormatParameterProperty_FlatEnumWhenNoEnumMap()
    {
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>
        {
            ["color"] = new("string", "color", Required: true, Enum: ["red", "green"])
        });

        var payload = _formatter.FormatToolsForPayload([schema], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];
        var props = (Dictionary<string, object>)((Dictionary<string, object>)tool["input_schema"])["properties"];
        var color = (Dictionary<string, object>)props["color"];

        var enumValues = Assert.IsType<IReadOnlyList<object>>(color["enum"], exactMatch: false);
        Assert.Equal(2, enumValues.Count);
    }

    [Fact]
    public void FormatParameterProperty_Format()
    {
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>
        {
            ["when"] = new("string", "timestamp", Required: true, Format: "date-time")
        });

        var payload = _formatter.FormatToolsForPayload([schema], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];
        var props = (Dictionary<string, object>)((Dictionary<string, object>)tool["input_schema"])["properties"];
        var when = (Dictionary<string, object>)props["when"];

        Assert.Equal("date-time", when["format"]);
    }

    [Fact]
    public void FormatParameterProperty_ArrayItemsWithFormat()
    {
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>
        {
            ["dates"] = new("array", "list of dates", Required: true, ItemsType: "string", ItemsFormat: "date")
        });

        var payload = _formatter.FormatToolsForPayload([schema], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];
        var props = (Dictionary<string, object>)((Dictionary<string, object>)tool["input_schema"])["properties"];
        var dates = (Dictionary<string, object>)props["dates"];

        var items = Assert.IsType<Dictionary<string, object>>(dates["items"]);
        Assert.Equal("string", items["type"]);
        Assert.Equal("date", items["format"]);
    }

    [Fact]
    public void FormatParameterProperty_ArrayItemsWithoutFormat()
    {
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>
        {
            ["tags"] = new("array", "list", Required: true, ItemsType: "string")
        });

        var payload = _formatter.FormatToolsForPayload([schema], ToolCallMode.Auto);
        var tool = ((List<Dictionary<string, object>>)payload["tools"])[0];
        var props = (Dictionary<string, object>)((Dictionary<string, object>)tool["input_schema"])["properties"];
        var tags = (Dictionary<string, object>)props["tags"];

        var items = Assert.IsType<Dictionary<string, object>>(tags["items"]);
        Assert.Equal("string", items["type"]);
        Assert.False(items.ContainsKey("format"));
    }

    [Fact]
    public void FormatTools_EmptyToolList()
    {
        var payload = _formatter.FormatToolsForPayload([], ToolCallMode.Auto);
        var tools = Assert.IsType<List<Dictionary<string, object>>>(payload["tools"]);
        Assert.Empty(tools);
    }

    [Fact]
    public void FormatTools_MultipleSchemas()
    {
        var s1 = new ToolSchema("a", "da", []);
        var s2 = new ToolSchema("b", "db", []);
        var payload = _formatter.FormatToolsForPayload([s1, s2], ToolCallMode.Auto);
        var tools = Assert.IsType<List<Dictionary<string, object>>>(payload["tools"]);
        Assert.Equal(2, tools.Count);
    }
}
