using System.Text;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests.Structural;

public sealed class MarkdownMatcherTests
{
    private static Dictionary<string, object?> Obj(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    private static IReadOnlyList<StructuralMatch> Run(string markdown, Dictionary<string, object?> rule)
    {
        using var pool = new TreeSitterParserPool();
        return new Matcher(pool).Match("/workspace/doc.md", Encoding.UTF8.GetBytes(markdown), StructuralRule.FromInline("markdown", rule));
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldMatchMermaidFence_WhenKindIsFenceAndRegexIsMermaid()
    {
        const string md = "# Title\n\n```csharp\nvar x = 1;\n```\n\n```mermaid\ngraph TD; A-->B;\n```\n";

        var m = Run(md, Obj(("kind", "fence"), ("regex", "mermaid")));

        var only = Assert.Single(m);
        Assert.Equal(7, only.Line);
        Assert.Equal("/workspace/doc.md", only.Path);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldMatchHeadingInsideSection_WhenInsideNamesParentHeading()
    {
        const string md = "# Guide\n\n## Install\n\n### Install steps\n\n# Other\n\n## Install\n";
        var direct = Obj(("kind", "heading"), ("regex", "^Guide$"));
        var ancestor = Obj(("kind", "heading"), ("regex", "^Guide$"), ("stopBy", "end"));

        var near = Run(md, Obj(("kind", "heading"), ("regex", "^Install"), ("inside", direct)));
        var any = Run(md, Obj(("kind", "heading"), ("regex", "^Install"), ("inside", ancestor)));

        Assert.Equal([3], near.Select(x => x.Line).ToArray());
        Assert.Equal([3, 5], any.Select(x => x.Line).ToArray());
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldRefusePattern_WhenLanguageIsMarkdown()
    {
        var ex = Assert.Throws<UnsupportedRuleKeyException>(() => Run("# Guide\n", Obj(("pattern", "foo($A)"))));

        Assert.Equal("pattern", ex.Key);
    }
}
