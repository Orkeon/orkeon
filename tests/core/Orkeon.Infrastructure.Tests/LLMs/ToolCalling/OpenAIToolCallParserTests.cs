using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCalling;

public class OpenAIToolCallParserTests
{
    private readonly OpenAIToolCallParser _parser;

    public OpenAIToolCallParserTests()
    {
        _parser = new OpenAIToolCallParser(NullLogger<OpenAIToolCallParser>.Instance);
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ParsesSingleToolCall_ReturnsCorrectFields()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "tool_calls": [{
                        "id": "call_123",
                        "type": "function",
                        "function": {
                            "name": "directory_read",
                            "arguments": "{\"path\":\"/src\"}"
                        }
                    }]
                }
            }]
        }
        """;

        // Act
        var result = _parser.ParseToolCalls(Parse(json));

        // Assert
        Assert.Single(result);
        var call = result[0];
        Assert.Equal("call_123", call.Id);
        Assert.Equal("directory_read", call.ToolName);
        Assert.Equal("/src", call.Arguments["path"]);
    }

    [Fact]
    public void ParsesMultipleToolCalls_ReturnsAll()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "tool_calls": [
                        {"id": "call_1", "type": "function", "function": {"name": "tool_a", "arguments": "{}"}},
                        {"id": "call_2", "type": "function", "function": {"name": "tool_b", "arguments": "{}"}},
                        {"id": "call_3", "type": "function", "function": {"name": "tool_c", "arguments": "{}"}}
                    ]
                }
            }]
        }
        """;

        // Act
        var result = _parser.ParseToolCalls(Parse(json));

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("tool_a", result[0].ToolName);
        Assert.Equal("tool_b", result[1].ToolName);
        Assert.Equal("tool_c", result[2].ToolName);
    }

    [Fact]
    public void ReturnsEmpty_WhenNoToolCallsProperty()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "content": "Hello, how can I help?"
                }
            }]
        }
        """;

        // Act
        var result = _parser.ParseToolCalls(Parse(json));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReturnsEmpty_WhenToolCallsArrayIsEmpty()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "tool_calls": []
                }
            }]
        }
        """;

        // Act
        var result = _parser.ParseToolCalls(Parse(json));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void HandlesInvalidArgumentsJson_ReturnsEmptyDict()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "tool_calls": [{
                        "id": "call_bad",
                        "type": "function",
                        "function": {
                            "name": "broken_tool",
                            "arguments": "not valid json at all"
                        }
                    }]
                }
            }]
        }
        """;

        // Act
        var result = _parser.ParseToolCalls(Parse(json));

        // Assert
        Assert.Single(result);
        Assert.Equal("broken_tool", result[0].ToolName);
        Assert.Empty(result[0].Arguments);
    }

    [Fact]
    public void GeneratesId_WhenMissing()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "tool_calls": [{
                        "type": "function",
                        "function": {
                            "name": "some_tool",
                            "arguments": "{}"
                        }
                    }]
                }
            }]
        }
        """;

        // Act
        var result = _parser.ParseToolCalls(Parse(json));

        // Assert
        Assert.Single(result);
        Assert.StartsWith("call_", result[0].Id);
        Assert.True(result[0].Id.Length > 5); // "call_" + GUID
    }

    [Fact]
    public void FormatsToolResult_Success_ReturnsCorrectStructure()
    {
        // Arrange
        var call = new ParsedToolCall("call_123", "my_tool", new Dictionary<string, object?>());

        // Act
        var result = (Dictionary<string, object>)_parser.FormatToolResult(call, "result text", success: true);

        // Assert
        Assert.Equal("tool", result["role"]);
        Assert.Equal("call_123", result["tool_call_id"]);
        Assert.Equal("result text", result["content"]);
    }

    [Fact]
    public void FormatsToolResult_Error_PrefixesWithError()
    {
        // Arrange
        var call = new ParsedToolCall("call_456", "failing_tool", new Dictionary<string, object?>());

        // Act
        var result = (Dictionary<string, object>)_parser.FormatToolResult(call, "something failed", success: false);

        // Assert
        Assert.Equal("tool", result["role"]);
        Assert.Equal("call_456", result["tool_call_id"]);
        Assert.Equal("Error: something failed", result["content"]);
    }

    [Fact]
    public void FormatAssistantToolCallMessage_ReturnsDeserializedMessage()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "tool_calls": [{
                        "id": "call_abc",
                        "type": "function",
                        "function": {
                            "name": "test_tool",
                            "arguments": "{}"
                        }
                    }]
                }
            }]
        }
        """;

        // Act
        var result = (Dictionary<string, object>)_parser.FormatAssistantToolCallMessage(Parse(json));

        // Assert
        Assert.True(result.ContainsKey("role"));
        Assert.True(result.ContainsKey("tool_calls"));
    }

    [Fact]
    public void ParsesToolCall_WithNestedArguments()
    {
        // Arrange
        var json = """
        {
            "choices": [{
                "message": {
                    "role": "assistant",
                    "tool_calls": [{
                        "id": "call_nested",
                        "type": "function",
                        "function": {
                            "name": "complex_tool",
                            "arguments": "{\"count\":42,\"flag\":true,\"tags\":[\"a\",\"b\"]}"
                        }
                    }]
                }
            }]
        }
        """;

        // Act
        var result = _parser.ParseToolCalls(Parse(json));

        // Assert
        Assert.Single(result);
        var args = result[0].Arguments;
        Assert.Equal(42L, (long)args["count"]!);
        Assert.Equal(true, args["flag"]);
        var tags = (List<object?>)args["tags"]!;
        Assert.Equal(2, tags.Count);
        Assert.Equal("a", tags[0]);
        Assert.Equal("b", tags[1]);
    }
}
