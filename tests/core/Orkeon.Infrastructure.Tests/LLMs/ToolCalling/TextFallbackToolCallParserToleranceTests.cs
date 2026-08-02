using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.LLMs.ToolCalling;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCalling;

/// <summary>
/// Tolerance of the text tool-call protocol to the ways models actually deviate from it.
/// </summary>
/// <remarks>
/// <para>
/// This parser is the only route to tool use for models without native function calling —
/// which is to say, the weakest models, the ones least likely to follow an unusual format
/// exactly. Brittleness here costs a tool call outright, and it costs it precisely where there
/// is no alternative path.
/// </para>
/// <para>
/// The deviations pinned below are not hypothetical. <c>llama3.2</c> produced the first one
/// verbatim in the campaign of 2026-08-01: it substituted the tool's name into the opening tag
/// and wrote the arguments as JSON, while closing the block correctly.
/// </para>
/// </remarks>
public class TextFallbackToolCallParserToleranceTests
{
    private readonly TextFallbackToolCallParser _parser =
        new(NullLogger<TextFallbackToolCallParser>.Instance);

    private static JsonElement OpenAiText(string content)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } },
        }));
        return document.RootElement.Clone();
    }

    /// <summary>The block llama3.2 emitted, character for character.</summary>
    [Fact]
    public void ShouldParseTheBlockLlama32ActuallyEmitted()
    {
        const string emitted =
            """[ORKEON_PROBE_LOOKUP]{tool => "orkeon_probe_lookup", args => {"city" : "Lyon"}}[/TOOL_CALL]""";

        var calls = _parser.ParseToolCalls(OpenAiText(emitted));

        var call = Assert.Single(calls);
        Assert.Equal("orkeon_probe_lookup", call.ToolName);
        Assert.Equal("Lyon", Assert.Contains("city", call.Arguments));
    }

    /// <summary>The documented form must keep working exactly as before.</summary>
    [Fact]
    public void ShouldStillParseTheDocumentedForm()
    {
        const string documented =
            """[TOOL_CALL]{tool => "directory_read", args => {--path "/src"}}[/TOOL_CALL]""";

        var calls = _parser.ParseToolCalls(OpenAiText(documented));

        var call = Assert.Single(calls);
        Assert.Equal("directory_read", call.ToolName);
        Assert.Equal("/src", Assert.Contains("path", call.Arguments));
    }

    /// <remarks>
    /// The expected values are <see langword="int"/> and <see langword="bool"/>, not strings:
    /// the parser coerces primitives before handing arguments on, and that predates this change.
    /// </remarks>
    [Theory]
    [InlineData("""[TOOL_CALL]{tool => "t", args => {"a" : "1", "b" : "2"}}[/TOOL_CALL]""")]
    [InlineData("""[TOOL_CALL]{tool => "t", args => {"a":"1","b":"2"}}[/TOOL_CALL]""")]
    public void ShouldReadEveryJsonArgument_WhateverTheSpacing(string block)
    {
        var call = Assert.Single(_parser.ParseToolCalls(OpenAiText(block)));

        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(1, Assert.Contains("a", call.Arguments));
        Assert.Equal(2, Assert.Contains("b", call.Arguments));
    }

    /// <summary>Unquoted JSON values (numbers, booleans) are common and must not be dropped.</summary>
    [Fact]
    public void ShouldReadUnquotedJsonValues()
    {
        const string block = """[TOOL_CALL]{tool => "t", args => {"count" : 5, "deep" : true}}[/TOOL_CALL]""";

        var call = Assert.Single(_parser.ParseToolCalls(OpenAiText(block)));

        Assert.Equal(5, Assert.Contains("count", call.Arguments));
        Assert.Equal(true, Assert.Contains("deep", call.Arguments));
    }

    /// <summary>
    /// The documented dialect wins outright when present, so nothing that parsed before can
    /// change meaning now — the JSON reader only sees blocks that would have yielded nothing.
    /// </summary>
    [Fact]
    public void ShouldPreferTheDocumentedDialect_WhenBothCouldMatch()
    {
        const string block =
            """[TOOL_CALL]{tool => "t", args => {--path "/from-flags"}}[/TOOL_CALL]""";

        var call = Assert.Single(_parser.ParseToolCalls(OpenAiText(block)));

        Assert.Equal("/from-flags", Assert.Contains("path", call.Arguments));
    }

    /// <summary>
    /// Tolerance has a floor. The body and the terminator are what identify a tool call; without
    /// them any bracketed word in ordinary prose would become one.
    /// </summary>
    [Theory]
    [InlineData("Consider the [SUMMARY] of the report, then continue.")]
    [InlineData("""[TOOL_CALL]{tool => "t", args => {--a "1"}}""")]
    [InlineData("""[lowercase]{tool => "t", args => {--a "1"}}[/TOOL_CALL]""")]
    [InlineData("I would call orkeon_probe_lookup with the city Lyon.")]
    public void ShouldNotInventAToolCall(string text)
    {
        Assert.Empty(_parser.ParseToolCalls(OpenAiText(text)));
    }
}
