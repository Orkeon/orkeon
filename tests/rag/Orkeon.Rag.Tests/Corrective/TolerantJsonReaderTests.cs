using System.Text.Json;
using Orkeon.Rag.Corrective;

namespace Orkeon.Rag.Tests.Corrective;

/// <summary>
/// <see cref="TolerantJsonReader"/>: balanced-object extraction from noisy LLM
/// responses (prose, code fences, escaped string literals) and tolerant property
/// reads. The escaped-literal cases also stand as the proof that the
/// <c>escaped</c> state flag of <c>FindBalancedEnd</c> is genuinely reachable —
/// SonarQube's S2583 claims otherwise, and these cases are what make that a
/// false positive rather than an assertion.
/// </summary>
public class TolerantJsonReaderTests
{
    private static string Extract(string text)
    {
        using var document = TolerantJsonReader.ExtractFirstObject(text);
        Assert.NotNull(document);
        return document.RootElement.GetRawText();
    }

    [Fact]
    public void ExtractFirstObject_EscapedQuoteInsideString_DoesNotEndObjectEarly()
    {
        // The `\"` must NOT be treated as the closing quote of the string: if the
        // `escaped` flag were unreachable (as S2583 claims), scanning would leave
        // the string state at `hi` and the object would close on the wrong brace.
        using var document = TolerantJsonReader.ExtractFirstObject("""{"a":"say \"hi\""}""");

        Assert.NotNull(document);
        Assert.Equal("say \"hi\"", document.RootElement.GetProperty("a").GetString());
    }

    [Fact]
    public void ExtractFirstObject_EscapedBackslashBeforeClosingQuote_TerminatesString()
    {
        // `"c:\\"` — the second backslash is consumed as escaped, so the following
        // quote DOES close the string. Verifies `escaped` is reset, not just set.
        using var document = TolerantJsonReader.ExtractFirstObject("""{"p":"c:\\","k":1}""");

        Assert.NotNull(document);
        Assert.Equal(@"c:\", document.RootElement.GetProperty("p").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("k").GetInt32());
    }

    [Fact]
    public void ExtractFirstObject_EscapedBraceInsideString_IgnoredForNesting()
    {
        using var document = TolerantJsonReader.ExtractFirstObject("""{"a":"{ \" }","b":2}""");

        Assert.NotNull(document);
        Assert.Equal("{ \" }", document.RootElement.GetProperty("a").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("b").GetInt32());
    }

    [Fact]
    public void ExtractFirstObject_NestedObject_ReturnsOutermostBalanced()
        => Assert.Equal("""{"a":{"b":1}}""", Extract("""{"a":{"b":1}}"""));

    [Fact]
    public void ExtractFirstObject_WrappedInProseAndCodeFence_IsFound()
        => Assert.Equal("""{"verdict":"CORRECT"}""",
            Extract("Sure! Here is the result:\n```json\n{\"verdict\":\"CORRECT\"}\n```\nHope that helps."));

    [Fact]
    public void ExtractFirstObject_LeadingUnparseableBrace_SkipsToNextCandidate()
        => Assert.Equal("""{"ok":true}""", Extract("""{not json} then {"ok":true}"""));

    [Fact]
    public void ExtractFirstObject_UnterminatedObject_ReturnsNull()
        => Assert.Null(TolerantJsonReader.ExtractFirstObject("""{"a":"unclosed"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no json at all")]
    public void ExtractFirstObject_NoObject_ReturnsNull(string? text)
        => Assert.Null(TolerantJsonReader.ExtractFirstObject(text));

    [Fact]
    public void ExtractFirstObject_ArrayOnly_ReturnsNull()
        => Assert.Null(TolerantJsonReader.ExtractFirstObject("""[1,2,3]"""));

    [Fact]
    public void CoerceDouble_NumberAndNumericString_BothCoerce()
    {
        using var document = JsonDocument.Parse("""{"n":0.75,"s":"0.75","x":"nope"}""");
        var root = document.RootElement;

        Assert.Equal(0.75, TolerantJsonReader.CoerceDouble(root.GetProperty("n")));
        Assert.Equal(0.75, TolerantJsonReader.CoerceDouble(root.GetProperty("s")));
        Assert.Null(TolerantJsonReader.CoerceDouble(root.GetProperty("x")));
    }
}
