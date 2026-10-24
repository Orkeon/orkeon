using System.Text;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests.Structural;

public sealed class StructuralRuleTests
{
    private static Dictionary<string, object?> Obj(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static IReadOnlyList<StructuralMatch> Run(string source, StructuralRule rule)
    {
        using var pool = new TreeSitterParserPool();
        return new Matcher(pool).Match("/workspace/a.cs", Encoding.UTF8.GetBytes(source), rule);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldHonourInsideAndNot_WhenRuleIsComposed()
    {
        const string source = "class C { void A() { Log.Write(1); } void B() { Log.Write(2); } }";
        var inside = StructuralRule.FromInline("csharp", Obj(
            ("pattern", "Log.Write($X)"),
            ("inside", Obj(
                ("kind", "method_declaration"),
                ("stopBy", "end"),
                ("has", Obj(("field", "name"), ("regex", "^A$")))))));
        var negated = StructuralRule.FromInline("csharp", Obj(
            ("all", new List<object?> { Obj(("pattern", "Log.Write($X)")), Obj(("not", Obj(("pattern", "Log.Write(2)")))) })));

        Assert.Equal("Log.Write(1)", Assert.Single(Run(source, inside)).Text);
        Assert.Equal("Log.Write(1)", Assert.Single(Run(source, negated)).Text);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldHonourAllAnyHasRegex_WhenRuleCombinesThem()
    {
        const string source = "class C { void M() { Foo(1); Bar(22); Baz(33); Bar(a22); } }";
        var rule = StructuralRule.FromInline("csharp", Obj(
            ("all", new List<object?>
            {
                Obj(("any", new List<object?> { Obj(("pattern", "Foo($X)")), Obj(("pattern", "Bar($X)")) })),
                Obj(("regex", "\\d\\d")),
                Obj(("has", Obj(("kind", "integer_literal"), ("stopBy", "end")))),
            })));

        Assert.Equal("Bar(22)", Assert.Single(Run(source, rule)).Text);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldRefuseUnsupportedKeyByName_WhenRuleUsesFollows()
    {
        var top = Assert.Throws<UnsupportedRuleKeyException>(() =>
            StructuralRule.FromInline("csharp", Obj(("pattern", "Foo()"), ("follows", Obj(("pattern", "Bar()"))))));
        var nested = Assert.Throws<UnsupportedRuleKeyException>(() =>
            StructuralRule.FromInline("csharp", Obj(("all", new List<object?> { Obj(("constraints", "x")) }))));

        Assert.Equal("follows", top.Key);
        Assert.Contains("follows", top.Message, StringComparison.Ordinal);
        Assert.Equal("constraints", nested.Key);
        Assert.Contains("constraints", nested.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "RT-31")]
    [InlineData("cs", "csharp")]
    [InlineData("ts", "typescript")]
    public void ShouldResolveLanguageAlias_WhenLanguageIsCsOrTs(string alias, string canonical)
    {
        var rule = StructuralRule.FromInline(alias, Obj(("pattern", "Foo()")));

        Assert.Equal(canonical, rule.Language);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldAcceptStopByNeighborEndAndRuleObject_WhenInsideUsesIt()
    {
        const string source = "class C { void M() { for (;;) { if (a) { Foo(1); } } } }";
        static StructuralRule Inside(string kind, object? stopBy) => StructuralRule.FromInline("csharp", Obj(
            ("pattern", "Foo($X)"),
            ("inside", stopBy is null ? Obj(("kind", kind)) : Obj(("kind", kind), ("stopBy", stopBy)))));

        Assert.Empty(Run(source, Inside("for_statement", "neighbor")));
        Assert.Empty(Run(source, Inside("for_statement", null)));
        Assert.Equal("Foo(1)", Assert.Single(Run(source, Inside("for_statement", "end"))).Text);
        Assert.Equal("Foo(1)", Assert.Single(Run(source, Inside("block", Obj(("kind", "for_statement"))))).Text);
        Assert.Empty(Run(source, Inside("for_statement", Obj(("kind", "if_statement")))));
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldExposeBoundMetavariableNames_WhenRuleHasSeveralPatterns()
    {
        var rule = StructuralRule.FromInline("csharp", Obj(
            ("any", new List<object?> { Obj(("pattern", "foo($Name, $$$rest)")), Obj(("pattern", "bar($arg, $_X1)")) })));

        Assert.Equal(["Name", "_X1", "arg", "rest"], rule.MetavariableNames.Order(StringComparer.Ordinal).ToArray());
    }
}
