using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Analysis.Tests;

public class DocumentedInToolTests
{
    private static RaggableNode Section(string path, string fqn, int startLine)
        => TestGraph.Symbol(fqn, path, UniversalNodeKind.Section, "markdown", startLine: startLine, endLine: startLine + 3);

    [Fact]
    [Trait("BR", "RT-37")]
    public async Task ShouldReturnSectionsWithHeadingPathAndLine_WhenDocumentedInAsked()
    {
        var symbol = TestGraph.Symbol("/src/Orders.cs::OrderService", "/src/Orders.cs", UniversalNodeKind.Class, "csharp");
        var setup = Section("/src/docs/guide.md", "/src/docs/guide.md::Guide/Setup", 12);
        var other = Section("/src/docs/other.md", "/src/docs/other.md::Other", 3);
        using var tool = new DocumentedInTool(TestGraph.Store(
            [TestGraph.Monorepo(), symbol, setup, other],
            [TestGraph.Edge(setup, symbol, EdgeKind.Documents), TestGraph.Edge(other, symbol, EdgeKind.Documents)]));

        var resp = await tool.ExecuteTypedForTest(new DocumentedInRequest { Fqn = "/src/Orders.cs::OrderService" }, CancellationToken.None);

        Assert.Equal(
            ["docs/guide.md|Guide > Setup|12", "docs/other.md|Other|3"],
            resp.Sections.Select(s => $"{s.Path}|{s.HeadingPath}|{s.StartLine}").Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-37")]
    public async Task ShouldReturnSymbolsOfDocument_WhenDocumentsOfAsked()
    {
        var service = TestGraph.Symbol("/src/Orders.cs::OrderService", "/src/Orders.cs", UniversalNodeKind.Class, "csharp");
        var parser = TestGraph.Symbol("/src/P.cs::Parser", "/src/P.cs", UniversalNodeKind.Class, "csharp");
        var guide = Section("/src/docs/guide.md", "/src/docs/guide.md::Guide", 1);
        var setup = Section("/src/docs/guide.md", "/src/docs/guide.md::Guide/Setup", 12);
        var elsewhere = Section("/src/docs/other.md", "/src/docs/other.md::Other", 3);
        using var tool = new DocumentsOfTool(TestGraph.Store(
            [TestGraph.Monorepo(), service, parser, guide, setup, elsewhere],
            [
                TestGraph.Edge(guide, service, EdgeKind.Documents),
                TestGraph.Edge(setup, parser, EdgeKind.Documents),
                TestGraph.Edge(elsewhere, service, EdgeKind.Documents),
            ]));

        var resp = await tool.ExecuteTypedForTest(new DocumentsOfRequest { Path = "docs/guide.md" }, CancellationToken.None);

        Assert.Equal(
            [
                "/src/Orders.cs::OrderService|Class|Orders.cs|Guide",
                "/src/P.cs::Parser|Class|P.cs|Guide > Setup",
            ],
            resp.Symbols.Select(s => $"{s.Fqn}|{s.Kind}|{s.File}|{s.HeadingPath}").Order(StringComparer.Ordinal));
        Assert.Empty(resp.Unresolved);
    }

    [Fact]
    [Trait("BR", "RT-37")]
    public async Task ShouldReturnRepoRelativeFile_WhenDocumentsOfAsked()
    {
        var service = TestGraph.Symbol("/src/Orders.cs::OrderService", "/src/Orders.cs", UniversalNodeKind.Class, "csharp");
        var guide = Section("/src/docs/guide.md", "/src/docs/guide.md::Guide", 1);
        using var tool = new DocumentsOfTool(TestGraph.Store(
            [TestGraph.Monorepo(), service, guide],
            [TestGraph.Edge(guide, service, EdgeKind.Documents)]));

        var resp = await tool.ExecuteTypedForTest(new DocumentsOfRequest { Path = "docs/guide.md" }, CancellationToken.None);

        Assert.Equal(["Orders.cs"], resp.Symbols.Select(s => s.File));
    }
}
