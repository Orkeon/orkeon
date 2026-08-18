using Orkeon.Application.Crew.Execution;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// SONAR-14 T2: pins the text/XML tool-call parser — the [TOOL_CALL] format, the XML
/// &lt;invoke&gt; format (MiniMax/Anthropic proxies), parameter-name normalization,
/// value sanitization, scalar coercion and LLM escape handling.
/// </summary>
public class ToolCallTextParserTests
{
    // ── [TOOL_CALL] format ────────────────────────────────────────────────

    [Fact]
    public void ParsesAToolCallBlock_WithQuotedArguments()
    {
        var response = """
            I will read the file now.
            [TOOL_CALL]{tool => "file_read", args => {--path "docs/readme.md" --recursive "true"}}[/TOOL_CALL]
            """;

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        var call = Assert.Single(calls);
        Assert.Equal("file_read", call.ToolName);
        Assert.Equal("docs/readme.md", call.Parameters["path"]);
        Assert.Equal(true, call.Parameters["recursive"]);
        Assert.Contains("[TOOL_CALL]", call.RawBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesUnquotedArgumentValues()
    {
        var response = """[TOOL_CALL]{tool => "counter", args => {--count 42}}[/TOOL_CALL]""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        Assert.Equal(42, Assert.Single(calls).Parameters["count"]);
    }

    [Fact]
    public void ParsesMultipleBlocks_InOrder()
    {
        var response = """
            [TOOL_CALL]{tool => "a", args => {--path "x"}}[/TOOL_CALL]
            some prose
            [TOOL_CALL]{tool => "b", args => {--path "y"}}[/TOOL_CALL]
            """;

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        Assert.Equal(["a", "b"], calls.Select(c => c.ToolName));
    }

    [Fact]
    public void LastDuplicateKeyWins()
    {
        var response = """[TOOL_CALL]{tool => "t", args => {--path "first" --path "second"}}[/TOOL_CALL]""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        Assert.Equal("second", Assert.Single(calls).Parameters["path"]);
    }

    [Fact]
    public void ReturnsEmpty_ForPlainProse()
    {
        Assert.Empty(ToolCallTextParser.ParseToolCallBlocks("Just a final answer, no tools."));
    }

    // ── XML <invoke> format ───────────────────────────────────────────────

    [Fact]
    public void ParsesXmlInvoke_WithInlineAttributes()
    {
        var response = """<invoke name="directory_read" path="src" recursive="true"/>""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        var call = Assert.Single(calls);
        Assert.Equal("directory_read", call.ToolName);
        Assert.Equal("src", call.Parameters["path"]);
        Assert.Equal(true, call.Parameters["recursive"]);
    }

    [Fact]
    public void ParsesXmlInvoke_WithParameterChildren()
    {
        var response = """
            <invoke name="file_write">
              <parameter name="path">out.txt</parameter>
              <parameter name="content">hello world</parameter>
            </invoke>
            """;

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        var call = Assert.Single(calls);
        Assert.Equal("file_write", call.ToolName);
        Assert.Equal("out.txt", call.Parameters["path"]);
        Assert.Equal("hello world", call.Parameters["content"]);
    }

    [Fact]
    public void ParsesXmlInvoke_MixingInlineAttributesAndChildren()
    {
        var response = """
            <invoke name="tool" path="src"><parameter name="query">agents</parameter></invoke>
            """;

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        var call = Assert.Single(calls);
        Assert.Equal("src", call.Parameters["path"]);
        Assert.Equal("agents", call.Parameters["query"]);
    }

    [Fact]
    public void PrefersToolCallBlocks_OverXml_WhenBothArePresent()
    {
        var response = """
            [TOOL_CALL]{tool => "structured", args => {--path "a"}}[/TOOL_CALL]
            <invoke name="xml_tool" path="b"/>
            """;

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        Assert.Equal("structured", Assert.Single(calls).ToolName);
    }

    // ── Parameter-name normalization ──────────────────────────────────────

    [Theory]
    [InlineData("file_path", "path")]
    [InlineData("filepath", "path")]
    [InlineData("input_file", "path")]
    [InlineData("input", "path")]
    [InlineData("files", "path")]
    [InlineData("file", "path")]
    [InlineData("directory", "path")]
    [InlineData("dir", "path")]
    [InlineData("folder", "path")]
    [InlineData("dir_path", "path")]
    [InlineData("directory_path", "path")]
    [InlineData("text", "content")]
    [InlineData("body", "content")]
    [InlineData("data", "content")]
    [InlineData("css_selector", "selector")]
    [InlineData("xpath_selector", "selector")]
    [InlineData("recurse", "recursive")]
    [InlineData("include_subdirs", "recursive")]
    [InlineData("custom_name", "custom_name")]
    public void NormalizesHallucinatedParameterNames(string llmName, string expected)
    {
        var response = $$$"""[TOOL_CALL]{tool => "t", args => {--{{{llmName}}} "v"}}[/TOOL_CALL]""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(response);

        Assert.True(Assert.Single(calls).Parameters.ContainsKey(expected),
            $"expected key '{expected}' for LLM name '{llmName}'");
    }

    // ── Value sanitization and coercion ───────────────────────────────────

    [Fact]
    public void UnwrapsJsonArrayWrappedValues()
    {
        var xml = """<invoke name="t"><parameter name="path">["some/path"]</parameter></invoke>""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(xml);

        Assert.Equal("some/path", Assert.Single(calls).Parameters["path"]);
    }

    [Fact]
    public void StripsBrackets_WhenTheArrayIsNotValidJson()
    {
        var xml = """<invoke name="t"><parameter name="path">[not-json]</parameter></invoke>""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(xml);

        Assert.Equal("not-json", Assert.Single(calls).Parameters["path"]);
    }

    [Fact]
    public void CoercesBooleansAndIntegers()
    {
        var xml = """
            <invoke name="t">
              <parameter name="recursive">TRUE</parameter>
              <parameter name="limit">7</parameter>
              <parameter name="query">plain</parameter>
            </invoke>
            """;

        var calls = ToolCallTextParser.ParseToolCallBlocks(xml);
        var parameters = Assert.Single(calls).Parameters;

        Assert.Equal(true, parameters["recursive"]);
        Assert.Equal(7, parameters["limit"]);
        Assert.Equal("plain", parameters["query"]);
    }

    [Fact]
    public void TrimsTrailingPunctuation_AndUnescapesSequences()
    {
        var xml = """<invoke name="t"><parameter name="content">line1\nline2\tend;</parameter></invoke>""";

        var calls = ToolCallTextParser.ParseToolCallBlocks(xml);

        Assert.Equal("line1\nline2\tend", Assert.Single(calls).Parameters["content"]);
    }

    // ── UnescapeLlmText ───────────────────────────────────────────────────

    [Theory]
    [InlineData("no escapes here", "no escapes here")]
    [InlineData(@"a\nb", "a\nb")]
    [InlineData(@"a\tb", "a\tb")]
    [InlineData(@"a\rb", "a\rb")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"keep \q unknown", @"keep \q unknown")]
    [InlineData("", "")]
    public void UnescapeLlmText_HandlesEverySequence(string input, string expected)
    {
        Assert.Equal(expected, ToolCallTextParser.UnescapeLlmText(input));
    }

    [Fact]
    public void UnescapeLlmText_KeepsATrailingBackslash()
    {
        Assert.Equal("tail\\", ToolCallTextParser.UnescapeLlmText("tail\\"));
    }
}
