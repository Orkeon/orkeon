using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Structural;

public sealed class RewriteOperationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rewrite").FullName;
    private readonly TreeSitterParserPool _pool = new();

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private RewriteOutcome Run(string source, string pattern, string fixTemplate)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var rule = StructuralRule.FromPattern("csharp", pattern);
        var matches = new Matcher(_pool).Match("/workspace/a.cs", bytes, rule);
        var fs = new FakeCountingFileSystemService(new DiskBackedFileSystemService(_dir, "/workspace"));
        return new StructuralEditEngine(fs, _pool).Rewrite("/workspace/a.cs", bytes, matches, new FixTemplate(fixTemplate));
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldApplyBackToFront_WhenFileHasSeveralMatches()
    {
        var result = Run("class C\n{\n    void M()\n    {\n        f(1);\n        f(22);\n        f(333);\n    }\n}\n", "f($X)", "renamed($X, 0)");

        Assert.Equal(
            "class C\n{\n    void M()\n    {\n        renamed(1, 0);\n        renamed(22, 0);\n        renamed(333, 0);\n    }\n}\n",
            Encoding.UTF8.GetString(result.Bytes));
        Assert.Equal(3, result.Applied);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldKeepIndentation_WhenCaptureSpansLines()
    {
        var result = Run("class C\n{\n    void M()\n    {\n        Wrap(1);\n    }\n}\n", "Wrap($A)", "Outer(\n    $A\n)");

        Assert.Equal(
            "class C\n{\n    void M()\n    {\n        Outer(\n            1\n        );\n    }\n}\n",
            Encoding.UTF8.GetString(result.Bytes));
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldKeepCapturedLinesAsIs_WhenCaptureSpansLines()
    {
        var result = Run(
            "class C\n{\n    void M()\n    {\n        Wrap(x =>\n        {\n            return 1;\n        });\n    }\n}\n",
            "Wrap($ARG)",
            "Outer(\n    $ARG\n)");

        Assert.Equal(
            "class C\n{\n    void M()\n    {\n        Outer(\n            x =>\n        {\n            return 1;\n        }\n        );\n    }\n}\n",
            Encoding.UTF8.GetString(result.Bytes));
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldApplyOnlyOutermost_WhenMatchesOverlap()
    {
        var result = Run("class C { void M() { f(f(1)); } }", "f($X)", "g($X)");

        Assert.Equal("class C { void M() { g(f(1)); } }", Encoding.UTF8.GetString(result.Bytes));
        Assert.Equal(1, result.Applied);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("overlaps", skipped.Reason);
        Assert.Equal("f(1)", skipped.Match.Text);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public void ShouldRenderWithFileLineEndings_WhenFileUsesCrlf()
    {
        var result = Run("class C\r\n{\r\n    void M()\r\n    {\r\n        f(1);\r\n    }\r\n}\r\n", "f($X)", "g(\n    $X)");

        Assert.Equal(
            "class C\r\n{\r\n    void M()\r\n    {\r\n        g(\r\n            1);\r\n    }\r\n}\r\n",
            Encoding.UTF8.GetString(result.Bytes));
    }
}
