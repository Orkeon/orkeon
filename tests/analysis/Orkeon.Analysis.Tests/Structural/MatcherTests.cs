using System.Text;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests.Structural;

public sealed class MatcherTests
{
    private static Task<IReadOnlyList<StructuralMatch>> RunAsync(string name, string source, string language, string pattern)
    {
        using var pool = new TreeSitterParserPool();
        return Task.FromResult(new Matcher(pool).Match("/workspace/" + name, Encoding.UTF8.GetBytes(source), StructuralRule.FromPattern(language, pattern)));
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldCaptureMetavariable_WhenPatternMatchesCall()
    {
        var m = await RunAsync("a.cs", "class C { void M() { Log.Write(\"a\"); Log.Write(\"b\"); } }", "csharp", "Log.Write($A)");

        Assert.Equal(2, m.Count);
        Assert.Equal("\"a\"", m[0].Captures["A"]);
        Assert.Equal("\"b\"", m[1].Captures["A"]);
        Assert.All(m, x => Assert.Equal(1, x.Line));
        Assert.Equal("/workspace/a.cs", m[0].Path);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldCaptureVariadicArguments_WhenPatternUsesTripleDollar()
    {
        var m = await RunAsync("a.cs", "class C { void M() { Foo(1, x); Foo(); } }", "csharp", "Foo($$$ARGS)");

        Assert.Equal(2, m.Count);
        Assert.Equal("1, x", m[0].Captures["ARGS"]);
        Assert.Equal("", m[1].Captures["ARGS"]);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldRequireEqualText_WhenMetavariableRepeats()
    {
        var m = await RunAsync("a.cs", "class C { void M() { Pair(a, a); Pair(a, b); } }", "csharp", "Pair($A, $A)");

        var only = Assert.Single(m);
        Assert.Equal("a", only.Captures["A"]);
        Assert.Equal("Pair(a, a)", only.Text);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldMatchTypeScript_WhenLanguageIsTypescript()
    {
        var m = await RunAsync("a.ts", "function f() { console.log(x); }", "typescript", "console.log($A)");

        var only = Assert.Single(m);
        Assert.Equal("x", only.Captures["A"]);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldRefusePattern_WhenPatternDoesNotParse()
    {
        const string source = "class C { void M() { Log.Write(\"a\"); } }";

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => RunAsync("a.cs", source, "csharp", "Log.Write($X"));

        Assert.Contains("Log.Write($X", ex.Message);
        var ok = await RunAsync("a.cs", source, "csharp", "Log.Write($X)");
        Assert.Single(ok);
    }
}
