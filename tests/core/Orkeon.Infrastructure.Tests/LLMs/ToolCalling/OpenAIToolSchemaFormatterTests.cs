using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCalling;

public class OpenAIToolSchemaFormatterTests
{
    private readonly OpenAIToolSchemaFormatter _formatter = new();

    [Fact]
    public void FormatsBasicToolSchema_WithRequiredAndOptionalParams()
    {
        // Arrange
        var schema = new ToolSchema(
            "directory_read",
            "Lists files in a directory",
            new Dictionary<string, ParameterSchema>
            {
                ["path"] = new("string", "Directory path", Required: true),
                ["recursive"] = new("boolean", "Recurse into subdirectories", Required: false, Default: false)
            });

        // Act
        var result = _formatter.FormatToolsForPayload([schema]);

        // Assert
        var tools = (List<Dictionary<string, object>>)result["tools"];
        Assert.Single(tools);

        var tool = tools[0];
        Assert.Equal("function", tool["type"]);

        var function = (Dictionary<string, object>)tool["function"];
        Assert.Equal("directory_read", function["name"]);
        Assert.Equal("Lists files in a directory", function["description"]);

        var parameters = (Dictionary<string, object>)function["parameters"];
        Assert.Equal("object", parameters["type"]);

        var properties = (Dictionary<string, object>)parameters["properties"];
        Assert.Equal(2, properties.Count);
        Assert.True(properties.ContainsKey("path"));
        Assert.True(properties.ContainsKey("recursive"));

        var required = (List<string>)parameters["required"];
        Assert.Single(required);
        Assert.Contains("path", required);
    }

    [Fact]
    public void HandlesEnumWithEnumMap_KeysAsEnumValues()
    {
        // Arrange
        var schema = new ToolSchema(
            "set_color",
            "Sets a color",
            new Dictionary<string, ParameterSchema>
            {
                ["color"] = new("string", "The color", Required: true,
                    EnumMap: new Dictionary<string, int> { ["Red"] = 0, ["Green"] = 1, ["Blue"] = 2 })
            });

        // Act
        var result = _formatter.FormatToolsForPayload([schema]);

        // Assert
        var tools = (List<Dictionary<string, object>>)result["tools"];
        var function = (Dictionary<string, object>)tools[0]["function"];
        var parameters = (Dictionary<string, object>)function["parameters"];
        var properties = (Dictionary<string, object>)parameters["properties"];
        var colorProp = (Dictionary<string, object>)properties["color"];
        var enumValues = (List<string>)colorProp["enum"];

        Assert.Equal(3, enumValues.Count);
        Assert.Contains("Red", enumValues);
        Assert.Contains("Green", enumValues);
        Assert.Contains("Blue", enumValues);
    }

    [Fact]
    public void HandlesEnumWithFlatList_WhenNoEnumMap()
    {
        // Arrange
        var schema = new ToolSchema(
            "set_level",
            "Sets a level",
            new Dictionary<string, ParameterSchema>
            {
                ["level"] = new("integer", "The level", Required: true,
                    Enum: new List<object> { 1, 2, 3 })
            });

        // Act
        var result = _formatter.FormatToolsForPayload([schema]);

        // Assert
        var tools = (List<Dictionary<string, object>>)result["tools"];
        var function = (Dictionary<string, object>)tools[0]["function"];
        var parameters = (Dictionary<string, object>)function["parameters"];
        var properties = (Dictionary<string, object>)parameters["properties"];
        var levelProp = (Dictionary<string, object>)properties["level"];
        var enumValues = (List<object>)levelProp["enum"];

        Assert.Equal(3, enumValues.Count);
        Assert.Contains(1, enumValues);
        Assert.Contains(2, enumValues);
        Assert.Contains(3, enumValues);
    }

    [Fact]
    public void HandlesArrayType_WithItemsTypeAndFormat()
    {
        // Arrange
        var schema = new ToolSchema(
            "process_files",
            "Process files",
            new Dictionary<string, ParameterSchema>
            {
                ["paths"] = new("array", "File paths", Required: true,
                    ItemsType: "string", ItemsFormat: "uri")
            });

        // Act
        var result = _formatter.FormatToolsForPayload([schema]);

        // Assert
        var tools = (List<Dictionary<string, object>>)result["tools"];
        var function = (Dictionary<string, object>)tools[0]["function"];
        var parameters = (Dictionary<string, object>)function["parameters"];
        var properties = (Dictionary<string, object>)parameters["properties"];
        var pathsProp = (Dictionary<string, object>)properties["paths"];

        Assert.Equal("array", pathsProp["type"]);
        var items = (Dictionary<string, object>)pathsProp["items"];
        Assert.Equal("string", items["type"]);
        Assert.Equal("uri", items["format"]);
    }

    [Fact]
    public void SetsRequiredList_OnlyForRequiredParams()
    {
        // Arrange
        var schema = new ToolSchema(
            "search",
            "Search tool",
            new Dictionary<string, ParameterSchema>
            {
                ["query"] = new("string", "Search query", Required: true),
                ["limit"] = new("integer", "Max results", Required: false, Default: 10),
                ["format"] = new("string", "Output format", Required: true)
            });

        // Act
        var result = _formatter.FormatToolsForPayload([schema]);

        // Assert
        var tools = (List<Dictionary<string, object>>)result["tools"];
        var function = (Dictionary<string, object>)tools[0]["function"];
        var parameters = (Dictionary<string, object>)function["parameters"];
        var required = (List<string>)parameters["required"];

        Assert.Equal(2, required.Count);
        Assert.Contains("query", required);
        Assert.Contains("format", required);
        Assert.DoesNotContain("limit", required);
    }

    [Fact]
    public void MapsToolCallMode_Auto()
    {
        // Arrange
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>());

        // Act
        var result = _formatter.FormatToolsForPayload([schema], ToolCallMode.Auto);

        // Assert
        Assert.Equal("auto", result["tool_choice"]);
    }

    [Fact]
    public void MapsToolCallMode_Required()
    {
        // Arrange
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>());

        // Act
        var result = _formatter.FormatToolsForPayload([schema], ToolCallMode.Required);

        // Assert
        Assert.Equal("required", result["tool_choice"]);
    }

    [Fact]
    public void MapsToolCallMode_None()
    {
        // Arrange
        var schema = new ToolSchema("t", "d", new Dictionary<string, ParameterSchema>());

        // Act
        var result = _formatter.FormatToolsForPayload([schema], ToolCallMode.None);

        // Assert
        Assert.Equal("none", result["tool_choice"]);
    }

    [Fact]
    public void EmptyDescription_FallsBackToName()
    {
        // Arrange
        var schema = new ToolSchema(
            "my_tool",
            "",
            new Dictionary<string, ParameterSchema>());

        // Act
        var result = _formatter.FormatToolsForPayload([schema]);

        // Assert
        var tools = (List<Dictionary<string, object>>)result["tools"];
        var function = (Dictionary<string, object>)tools[0]["function"];
        Assert.Equal("my_tool", function["description"]);
    }

    [Fact]
    public void ZeroParameters_EmptyPropertiesAndRequired()
    {
        // Arrange
        var schema = new ToolSchema(
            "no_params",
            "Tool with no params",
            new Dictionary<string, ParameterSchema>());

        // Act
        var result = _formatter.FormatToolsForPayload([schema]);

        // Assert
        var tools = (List<Dictionary<string, object>>)result["tools"];
        var function = (Dictionary<string, object>)tools[0]["function"];
        var parameters = (Dictionary<string, object>)function["parameters"];
        var properties = (Dictionary<string, object>)parameters["properties"];
        var required = (List<string>)parameters["required"];

        Assert.Empty(properties);
        Assert.Empty(required);
    }
}
