using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.LLM;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.Tests.CovToolCalling;

public class CovToolCalling_TextFallbackToolCallParserTests
{
    private readonly TextFallbackToolCallParser _parser =
        new(NullLogger<TextFallbackToolCallParser>.Instance);

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement OpenAiText(string content)
        => Parse($$"""
        { "choices": [ { "message": { "role": "assistant", "content": {{JsonSerializer.Serialize(content)}} } } ] }
        """);

    private static JsonElement AnthropicText(string content)
        => Parse($$"""
        { "content": [ { "type": "text", "text": {{JsonSerializer.Serialize(content)}} } ] }
        """);

    // ── Constructor ───────────────────────────────────────────────

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new TextFallbackToolCallParser(null!));
        Assert.Equal("logger", ex.ParamName);
    }

    // ── ExtractTextContent / ParseToolCalls entry ─────────────────

    [Fact]
    public void ParseToolCalls_NoText_ReturnsEmpty()
    {
        var result = _parser.ParseToolCalls(Parse("{}"));
        Assert.Empty(result);
    }

    [Fact]
    public void ParseToolCalls_EmptyContentString_ReturnsEmpty()
    {
        var result = _parser.ParseToolCalls(OpenAiText(""));
        Assert.Empty(result);
    }

    [Fact]
    public void ParseToolCalls_ContentNotString_ReturnsEmpty()
    {
        var json = Parse("""
        { "choices": [ { "message": { "content": { "nested": true } } } ] }
        """);
        Assert.Empty(_parser.ParseToolCalls(json));
    }

    [Fact]
    public void ParseToolCalls_AnthropicNonTextBlock_ReturnsEmpty()
    {
        var json = Parse("""
        { "content": [ { "type": "tool_use", "id": "x", "name": "y", "input": {} } ] }
        """);
        Assert.Empty(_parser.ParseToolCalls(json));
    }

    [Fact]
    public void ParseToolCalls_PlainTextNoToolCall_ReturnsEmpty()
    {
        var result = _parser.ParseToolCalls(OpenAiText("Just a regular reply with no tool."));
        Assert.Empty(result);
    }

    // ── Structured [TOOL_CALL] format ─────────────────────────────

    [Fact]
    public void ParseToolCalls_StructuredFormat_QuotedArg()
    {
        var text = "[TOOL_CALL]{tool => \"file_read\", args => {--path \"/src/app.cs\"}}[/TOOL_CALL]";
        var result = _parser.ParseToolCalls(OpenAiText(text));

        Assert.Single(result);
        Assert.Equal("file_read", result[0].ToolName);
        Assert.Equal("text_call_0", result[0].Id);
        Assert.Equal("/src/app.cs", result[0].Arguments["path"]);
    }

    [Fact]
    public void ParseToolCalls_StructuredFormat_UnquotedArg()
    {
        var text = "[TOOL_CALL]{tool => \"counter\", args => {--count 42}}[/TOOL_CALL]";
        var result = _parser.ParseToolCalls(OpenAiText(text));

        Assert.Single(result);
        Assert.Equal(42, result[0].Arguments["count"]);
    }

    [Fact]
    public void ParseToolCalls_StructuredFormat_BooleanCoercion()
    {
        var text = "[TOOL_CALL]{tool => \"t\", args => {--recursive true}}[/TOOL_CALL]";
        var result = _parser.ParseToolCalls(OpenAiText(text));
        Assert.Equal(true, result[0].Arguments["recursive"]);
    }

    [Fact]
    public void ParseToolCalls_StructuredFormat_MultipleCalls()
    {
        var text =
            "[TOOL_CALL]{tool => \"a\", args => {--x \"1\"}}[/TOOL_CALL]" +
            " then " +
            "[TOOL_CALL]{tool => \"b\", args => {--y \"2\"}}[/TOOL_CALL]";
        var result = _parser.ParseToolCalls(OpenAiText(text));

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].ToolName);
        Assert.Equal("b", result[1].ToolName);
        Assert.Equal("text_call_1", result[1].Id);
    }

    [Fact]
    public void ParseToolCalls_StructuredFormat_AnthropicSource()
    {
        var text = "[TOOL_CALL]{tool => \"file_read\", args => {--file_path \"/a.txt\"}}[/TOOL_CALL]";
        var result = _parser.ParseToolCalls(AnthropicText(text));

        Assert.Single(result);
        // file_path is normalized to "path"
        Assert.True(result[0].Arguments.ContainsKey("path"));
    }

    // ── XML <invoke> format ───────────────────────────────────────

    [Fact]
    public void ParseToolCalls_XmlInlineAttributes()
    {
        var text = "<invoke name=\"web_scrape\" url=\"http://x\" css_selector=\"div\" />";
        var result = _parser.ParseToolCalls(OpenAiText(text));

        Assert.Single(result);
        Assert.Equal("web_scrape", result[0].ToolName);
        Assert.Equal("http://x", result[0].Arguments["url"]);
        // css_selector normalized to selector
        Assert.Equal("div", result[0].Arguments["selector"]);
        // name attribute skipped
        Assert.False(result[0].Arguments.ContainsKey("name"));
    }

    [Fact]
    public void ParseToolCalls_XmlParameterBody()
    {
        var text =
            "<invoke name=\"file_write\">" +
            "<parameter name=\"path\">/out.txt</parameter>" +
            "<parameter name=\"text\">hello</parameter>" +
            "</invoke>";
        var result = _parser.ParseToolCalls(OpenAiText(text));

        Assert.Single(result);
        Assert.Equal("file_write", result[0].ToolName);
        Assert.Equal("/out.txt", result[0].Arguments["path"]);
        // text normalized to content
        Assert.Equal("hello", result[0].Arguments["content"]);
    }

    [Fact]
    public void ParseToolCalls_XmlEmptyToolName_Skipped()
    {
        var text = "<invoke name=\"\"></invoke>";
        Assert.Empty(_parser.ParseToolCalls(OpenAiText(text)));
    }

    [Fact]
    public void ParseToolCalls_XmlSelfClosingNoAttrs()
    {
        var text = "<invoke name=\"ping\" />";
        var result = _parser.ParseToolCalls(OpenAiText(text));
        Assert.Single(result);
        Assert.Equal("ping", result[0].ToolName);
        Assert.Empty(result[0].Arguments);
    }

    // ── FormatToolResult / FormatAssistantToolCallMessage ─────────

    [Fact]
    public void FormatToolResult_Success()
    {
        var call = new ParsedToolCall("text_call_0", "my_tool", []);
        var dict = (Dictionary<string, object>)_parser.FormatToolResult(call, "done", success: true);

        Assert.Equal("user", dict["role"]);
        Assert.Equal("Tool my_tool result: done", dict["content"]);
    }

    [Fact]
    public void FormatToolResult_Error()
    {
        var call = new ParsedToolCall("text_call_0", "my_tool", []);
        var dict = (Dictionary<string, object>)_parser.FormatToolResult(call, "boom", success: false);

        Assert.Equal("Tool my_tool result: Error: boom", dict["content"]);
    }

    [Fact]
    public void FormatAssistantToolCallMessage_ReturnsText()
    {
        var dict = (Dictionary<string, object>)_parser.FormatAssistantToolCallMessage(OpenAiText("hi there"));
        Assert.Equal("assistant", dict["role"]);
        Assert.Equal("hi there", dict["content"]);
    }

    [Fact]
    public void FormatAssistantToolCallMessage_NoText_EmptyContent()
    {
        var dict = (Dictionary<string, object>)_parser.FormatAssistantToolCallMessage(Parse("{}"));
        Assert.Equal("", dict["content"]);
    }

    // ── NormalizeParameterName ────────────────────────────────────

    [Theory]
    [InlineData("css_selector", "selector")]
    [InlineData("xpath_selector", "selector")]
    [InlineData("query", "query")]
    [InlineData("file_path", "path")]
    [InlineData("filepath", "path")]
    [InlineData("input_file", "path")]
    [InlineData("input", "path")]
    [InlineData("files", "path")]
    [InlineData("file", "path")]
    [InlineData("text", "content")]
    [InlineData("body", "content")]
    [InlineData("data", "content")]
    [InlineData("directory", "path")]
    [InlineData("dir", "path")]
    [InlineData("folder", "path")]
    [InlineData("dir_path", "path")]
    [InlineData("directory_path", "path")]
    [InlineData("recurse", "recursive")]
    [InlineData("include_subdirs", "recursive")]
    [InlineData("unmapped_thing", "unmapped_thing")]
    public void NormalizeParameterName_MapsKnownAliases(string raw, string expected)
    {
        Assert.Equal(expected, TextFallbackToolCallParser.NormalizeParameterName(raw));
    }

    [Fact]
    public void NormalizeParameterName_StripsLeadingDashes()
    {
        Assert.Equal("path", TextFallbackToolCallParser.NormalizeParameterName("--file_path"));
    }

    // ── SanitizeParameterValue ────────────────────────────────────

    [Fact]
    public void Sanitize_NonString_ReturnedAsIs()
    {
        Assert.Equal(123, TextFallbackToolCallParser.SanitizeParameterValue("x", 123));
    }

    [Fact]
    public void Sanitize_EmptyString_ReturnedAsIs()
    {
        Assert.Equal("", TextFallbackToolCallParser.SanitizeParameterValue("x", ""));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void Sanitize_BooleanCoercion(string input, bool expected)
    {
        Assert.Equal(expected, TextFallbackToolCallParser.SanitizeParameterValue("k", input));
    }

    [Fact]
    public void Sanitize_IntegerCoercion()
    {
        Assert.Equal(100, TextFallbackToolCallParser.SanitizeParameterValue("k", "100"));
    }

    [Fact]
    public void Sanitize_TrailingPunctuationStripped()
    {
        Assert.Equal(100, TextFallbackToolCallParser.SanitizeParameterValue("k", "100;"));
        Assert.Equal(true, TextFallbackToolCallParser.SanitizeParameterValue("k", "true,"));
    }

    [Fact]
    public void Sanitize_JsonArrayWrappedSingle_Unwrapped()
    {
        Assert.Equal("/some/path", TextFallbackToolCallParser.SanitizeParameterValue("k", "[\"/some/path\"]"));
    }

    [Fact]
    public void Sanitize_JsonArrayMultiple_NotUnwrapped()
    {
        // Length != 1 -> not unwrapped, falls through. Result is the trimmed string.
        var result = TextFallbackToolCallParser.SanitizeParameterValue("k", "[\"a\",\"b\"]");
        Assert.Equal("[\"a\",\"b\"]", result);
    }

    [Fact]
    public void Sanitize_MalformedArray_FallbackInner()
    {
        // Not valid JSON string[] but starts/ends with brackets -> catch branch
        var result = TextFallbackToolCallParser.SanitizeParameterValue("k", "[notjson]");
        Assert.Equal("notjson", result);
    }

    [Fact]
    public void Sanitize_EmptyBrackets_NotUnwrapped()
    {
        var result = TextFallbackToolCallParser.SanitizeParameterValue("k", "[]");
        Assert.Equal("[]", result);
    }

    [Fact]
    public void Sanitize_DoubleQuotedString_Stripped()
    {
        Assert.Equal("/x/y", TextFallbackToolCallParser.SanitizeParameterValue("k", "\"/x/y\""));
    }

    [Fact]
    public void Sanitize_PlainString_Unchanged()
    {
        Assert.Equal("hello world", TextFallbackToolCallParser.SanitizeParameterValue("k", "hello world"));
    }

    // ── UnescapeLlmText ───────────────────────────────────────────

    [Fact]
    public void Unescape_Empty_ReturnedAsIs()
    {
        Assert.Equal("", TextFallbackToolCallParser.UnescapeLlmText(""));
    }

    [Fact]
    public void Unescape_NoBackslash_ReturnedAsIs()
    {
        Assert.Equal("plain", TextFallbackToolCallParser.UnescapeLlmText("plain"));
    }

    [Fact]
    public void Unescape_Newline()
    {
        Assert.Equal("a\nb", TextFallbackToolCallParser.UnescapeLlmText("a\\nb"));
    }

    [Fact]
    public void Unescape_Tab()
    {
        Assert.Equal("a\tb", TextFallbackToolCallParser.UnescapeLlmText("a\\tb"));
    }

    [Fact]
    public void Unescape_CarriageReturn()
    {
        Assert.Equal("a\rb", TextFallbackToolCallParser.UnescapeLlmText("a\\rb"));
    }

    [Fact]
    public void Unescape_Backslash()
    {
        Assert.Equal("a\\b", TextFallbackToolCallParser.UnescapeLlmText("a\\\\b"));
    }

    [Fact]
    public void Unescape_UnknownEscape_Preserved()
    {
        Assert.Equal("a\\zb", TextFallbackToolCallParser.UnescapeLlmText("a\\zb"));
    }

    [Fact]
    public void Unescape_TrailingBackslash_Preserved()
    {
        Assert.Equal("a\\", TextFallbackToolCallParser.UnescapeLlmText("a\\"));
    }

    // ── Debug logger path (covers IsEnabled(Debug) branch) ────────

    [Fact]
    public void ParseToolCalls_WithDebugLogger_StillParses()
    {
        var logger = new RecordingLogger<TextFallbackToolCallParser>();
        var parser = new TextFallbackToolCallParser(logger);
        var text = "[TOOL_CALL]{tool => \"a\", args => {--x \"1\"}}[/TOOL_CALL]";

        var result = parser.ParseToolCalls(OpenAiText(text));

        Assert.Single(result);
        Assert.Contains(logger.Messages, m => m.Contains("extracted"));
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
