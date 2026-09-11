using System.Text.Json;
using Orkeon.Application.Crew.Execution;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// The third text shape: a JSON tool-call envelope written as the assistant text, the way
/// small local models answer when the server's own parser gives up (measured on the README
/// quickstart, llama3.2:1b under Ollama 0.34.0, 2026-09-11).
/// </summary>
public class ToolCallTextParserJsonTests
{
    private static string Str(object? value) => ((JsonElement)value!).GetString()!;

    [Fact]
    public void ParsesTheOpenAiEnvelope_WithAFunctionObject()
    {
        var response = """{"type":"function","function":{"name":"file_write","parameters":{"path":"/output/hello.md","content":"hi"}}}""";

        var call = Assert.Single(ToolCallTextParser.ParseToolCallBlocks(response));

        Assert.Equal("file_write", call.ToolName);
        Assert.Equal("/output/hello.md", Str(call.Parameters["path"]));
        Assert.Equal("hi", Str(call.Parameters["content"]));
    }

    [Fact]
    public void ParsesTheFlatEnvelope_WhereFunctionIsAName_AndArgumentsSitBesideIt()
    {
        var response = """{"type":"function","function":"file_write","parameters":{"path":"/output/a.md","content":"x"}}""";

        var call = Assert.Single(ToolCallTextParser.ParseToolCallBlocks(response));

        Assert.Equal("file_write", call.ToolName);
        Assert.Equal("/output/a.md", Str(call.Parameters["path"]));
    }

    [Fact]
    public void ParsesNameAndArguments_InsideAFence_WithProseAround()
    {
        var response = """
            Sure, writing the file:
            ```json
            {"name": "file_write", "arguments": {"path": "/output/b.md", "content": "line"}}
            ```
            Done.
            """;

        var call = Assert.Single(ToolCallTextParser.ParseToolCallBlocks(response));

        Assert.Equal("file_write", call.ToolName);
        Assert.Equal("line", Str(call.Parameters["content"]));
    }

    [Fact]
    public void ParsesArgumentsGivenAsAJsonString_AndAToolCallsArray()
    {
        var response = """{"tool_calls":[{"name":"a","arguments":"{\"x\":1}"},{"name":"b","parameters":{"y":"2"}}]}""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        Assert.Equal(2, calls.Count);
        Assert.Equal("a", calls[0].ToolName);
        Assert.Equal(1, ((JsonElement)calls[0].Parameters["x"]!).GetInt32());
        Assert.Equal("b", calls[1].ToolName);
    }

    [Fact]
    public void RepairsRawLineBreaksInsideStrings_TheUsualDefectOfAHandWrittenEnvelope()
    {
        var response = "{\"name\":\"file_write\",\"parameters\":{\"path\":\"/output/hello.md\",\"content\":\"# Hello\nsecond line\"}}";

        var call = Assert.Single(ToolCallTextParser.ParseToolCallBlocks(response));

        Assert.Equal("# Hello\nsecond line", Str(call.Parameters["content"]));
    }

    [Fact]
    public void AnObjectThatIsNotAnEnvelope_OrIsBrokenBeyondRepair_IsNotAToolCall()
    {
        // The exact answer llama3.2:1b gave on the GitHub runner: a stray "content" key
        // followed by a second key where a value should be.
        var broken = "{\"type\":\"function\",\"function\":{\"name\":\"file_write\",\"parameters\":{\"path\":\"/output/hello.md\",\"content\":\"# Hello\", \"append\": \"False\", \"content\": \"create_backup\": \"False\"}}}";

        Assert.Empty(ToolCallTextParser.ParseToolCallBlocks(broken));
        Assert.Empty(ToolCallTextParser.ParseToolCallBlocks("""{"summary": "all good", "items": [1, 2]}"""));
        Assert.Empty(ToolCallTextParser.ParseToolCallBlocks("The result is {not json} and 42."));
    }

    [Fact]
    public void LooksLikeToolCallAttempt_RecognisesTheShape_NotTheValidity()
    {
        string[] tools = ["file_write"];
        var broken = "{\"type\":\"function\",\"function\":{\"name\":\"file_write\",\"parameters\":{\"content\": \"a\": \"b\"}}}";

        Assert.True(ToolCallTextParser.LooksLikeToolCallAttempt(broken, tools));
        Assert.True(ToolCallTextParser.LooksLikeToolCallAttempt("```json\n{\"name\":\"file_write\",\"arguments\":{}}\n```", tools));
        Assert.False(ToolCallTextParser.LooksLikeToolCallAttempt("""{"name":"report","parameters":{"count":3}}""", tools));
        Assert.True(ToolCallTextParser.LooksLikeToolCallAttempt("<invoke name=\"x\">", tools));
        Assert.False(ToolCallTextParser.LooksLikeToolCallAttempt("DONE", tools));
        Assert.False(ToolCallTextParser.LooksLikeToolCallAttempt("""{"summary": "a json deliverable", "count": 3}""", tools));
        Assert.False(ToolCallTextParser.LooksLikeToolCallAttempt("", tools));
    }
}
