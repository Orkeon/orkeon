using System.Text.Json;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.Tests.CovToolCalling;

public class CovToolCalling_AnthropicToolCallParserTests
{
    private readonly AnthropicToolCallParser _parser = new();

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ParseToolCalls_NoContent_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseToolCalls(Parse("{}")));
    }

    [Fact]
    public void ParseToolCalls_ContentNotArray_ReturnsEmpty()
    {
        Assert.Empty(_parser.ParseToolCalls(Parse("""{ "content": "text" }""")));
    }

    [Fact]
    public void ParseToolCalls_OnlyTextBlocks_ReturnsEmpty()
    {
        var json = Parse("""
        { "content": [ { "type": "text", "text": "hello" } ] }
        """);
        Assert.Empty(_parser.ParseToolCalls(json));
    }

    [Fact]
    public void ParseToolCalls_SingleToolUse_ParsesAllScalarTypes()
    {
        var json = Parse("""
        {
            "content": [
                {
                    "type": "tool_use",
                    "id": "toolu_01",
                    "name": "search",
                    "input": {
                        "query": "weather",
                        "limit": 5,
                        "ratio": 0.75,
                        "enabled": true,
                        "disabled": false
                    }
                }
            ]
        }
        """);

        var result = _parser.ParseToolCalls(json);

        Assert.Single(result);
        var call = result[0];
        Assert.Equal("toolu_01", call.Id);
        Assert.Equal("search", call.ToolName);
        Assert.Equal("weather", call.Arguments["query"]);
        Assert.Equal(5L, call.Arguments["limit"]);
        Assert.Equal(0.75, call.Arguments["ratio"]);
        Assert.Equal(true, call.Arguments["enabled"]);
        Assert.Equal(false, call.Arguments["disabled"]);
    }

    [Fact]
    public void ParseToolCalls_NestedObjectsAndArrays()
    {
        var json = Parse("""
        {
            "content": [
                {
                    "type": "tool_use",
                    "id": "id1",
                    "name": "complex",
                    "input": {
                        "tags": ["a", "b"],
                        "config": { "deep": { "value": 1 } }
                    }
                }
            ]
        }
        """);

        var result = _parser.ParseToolCalls(json);
        var args = result[0].Arguments;

        var tags = Assert.IsType<List<object>>(args["tags"]);
        Assert.Equal(2, tags.Count);
        Assert.Equal("a", tags[0]);

        var config = Assert.IsType<Dictionary<string, object?>>(args["config"]);
        var deep = Assert.IsType<Dictionary<string, object?>>(config["deep"]);
        Assert.Equal(1L, deep["value"]);
    }

    [Fact]
    public void ParseToolCalls_InputNotObject_EmptyArguments()
    {
        var json = Parse("""
        { "content": [ { "type": "tool_use", "id": "id", "name": "n", "input": "not-an-object" } ] }
        """);

        var result = _parser.ParseToolCalls(json);
        Assert.Single(result);
        Assert.Empty(result[0].Arguments);
    }

    [Fact]
    public void ParseToolCalls_MultipleToolUseBlocks_MixedWithText()
    {
        var json = Parse("""
        {
            "content": [
                { "type": "text", "text": "Let me help" },
                { "type": "tool_use", "id": "a", "name": "first", "input": {} },
                { "type": "tool_use", "id": "b", "name": "second", "input": {} }
            ]
        }
        """);

        var result = _parser.ParseToolCalls(json);
        Assert.Equal(2, result.Count);
        Assert.Equal("first", result[0].ToolName);
        Assert.Equal("second", result[1].ToolName);
    }

    [Fact]
    public void FormatToolResult_Success()
    {
        var call = new ParsedToolCall("toolu_x", "tool", []);
        var dict = (Dictionary<string, object>)_parser.FormatToolResult(call, "output", success: true);

        Assert.Equal("user", dict["role"]);
        var content = Assert.IsType<List<Dictionary<string, object>>>(dict["content"]);
        var block = content[0];
        Assert.Equal("tool_result", block["type"]);
        Assert.Equal("toolu_x", block["tool_use_id"]);
        Assert.Equal("output", block["content"]);
    }

    [Fact]
    public void FormatToolResult_Error_PrefixesError()
    {
        var call = new ParsedToolCall("toolu_y", "tool", []);
        var dict = (Dictionary<string, object>)_parser.FormatToolResult(call, "bad", success: false);
        var content = Assert.IsType<List<Dictionary<string, object>>>(dict["content"]);
        Assert.Equal("Error: bad", content[0]["content"]);
    }

    [Fact]
    public void FormatAssistantToolCallMessage_DeserializesContent()
    {
        var json = Parse("""
        {
            "content": [
                { "type": "text", "text": "reasoning" },
                { "type": "tool_use", "id": "a", "name": "n", "input": {} }
            ]
        }
        """);

        var dict = (Dictionary<string, object>)_parser.FormatAssistantToolCallMessage(json);
        Assert.Equal("assistant", dict["role"]);
        var content = Assert.IsType<List<object>>(dict["content"]);
        Assert.Equal(2, content.Count);
    }
}
