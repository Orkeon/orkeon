using System.Collections.Immutable;
using Orkeon.Analysis.Core.Structural;

namespace Orkeon.Analysis.Tests.Structural;

public sealed class FixTemplateTests
{
    private static StructuralMatch MatchWith(params (string Name, string Value)[] captures) => new()
    {
        Path = "/workspace/a.cs",
        Line = 1,
        Column = 1,
        StartByte = 0,
        EndByte = 1,
        Text = "x",
        Captures = captures.ToImmutableDictionary(c => c.Name, c => c.Value),
    };

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldRenderFixWithCaptures_WhenTemplateUsesMetavariables()
    {
        var fix = new FixTemplate("Log.Info($A, $$$ARGS)");

        var text = fix.Render(MatchWith(("A", "\"msg\""), ("ARGS", "x,  y")));

        Assert.Equal("Log.Info(\"msg\", x,  y)", text);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldRefuseByName_WhenFixUsesUnboundMetavariable()
    {
        var fix = new FixTemplate("Log.Info($A, $MISSING)");

        var ex = Assert.Throws<UnboundMetavariableException>(() => fix.Validate(["A"]));

        Assert.Equal("MISSING", ex.Name);
        Assert.Contains("MISSING", ex.Message);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldRenderEmptyAndEscapedDollar_WhenEmptyVariadicCaptureOrDoubleDollar()
    {
        var fix = new FixTemplate("Foo($$$ARGS) // costs $$5");

        fix.Validate(["ARGS"]);
        var text = fix.Render(MatchWith(("ARGS", "")));

        Assert.Equal("Foo() // costs $5", text);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldRenderMixedCaseAndLowercaseNames_WhenPatternBindsThem()
    {
        var fix = new FixTemplate("bar($Name, $arg, $$$rest)");

        fix.Validate(["Name", "arg", "rest"]);
        var text = fix.Render(MatchWith(("Name", "x"), ("arg", "y"), ("rest", "a, b")));

        Assert.Equal("bar(x, y, a, b)", text);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldKeepDollarLiteral_WhenNotAMetavariable()
    {
        var fix = new FixTemplate("a $1 b $ c $Name$1 d $");

        fix.Validate(["Name"]);
        var text = fix.Render(MatchWith(("Name", "x")));

        Assert.Equal("a $1 b $ c x$1 d $", text);
    }
}
