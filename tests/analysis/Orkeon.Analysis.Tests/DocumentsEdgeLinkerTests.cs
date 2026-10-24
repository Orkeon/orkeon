using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// Contract of the Markdown mapper for RT-37: the mapper tags the innermost <see cref="UniversalNodeKind.Section"/>
/// holding the span with <c>code:&lt;text&gt;</c> for an inline code span and <c>link:&lt;text&gt;</c> for a link text;
/// the heading is the Section node's Name. <see cref="DocumentsEdgeLinker"/> resolves each by local name (the part
/// of the Fqn after the last '::' or '.') and links every candidate with one <see cref="EdgeKind.Documents"/> edge.
/// </summary>
public class DocumentsEdgeLinkerTests
{
    private const string Orders = "public class OrderService { }";

    private static RaggableNode Symbol(string id, string fqn, string name) => new()
    {
        Id = id,
        Kind = UniversalNodeKind.Class,
        Name = name,
        Fqn = fqn,
        VirtualFilePath = "/src/Orders.cs",
        Range = new NodeRange(0, 1, 0, 0, 0),
        Level = NodeLevel.L3_Symbol,
        Language = "csharp",
        SourceSnippet = "",
        Sha256 = "x",
    };

    private static bool Documents(RaggableTree tree, string sectionName, string symbolName)
    {
        var section = tree.Nodes.Single(n => n.Kind == UniversalNodeKind.Section && n.Name == sectionName);
        var symbol = tree.Nodes.Single(n => n.Kind == UniversalNodeKind.Class && n.Name == symbolName);
        return tree.Edges.Any(e => e.Kind == EdgeKind.Documents && e.FromId == section.Id && e.ToId == symbol.Id);
    }

    [Fact]
    [Trait("BR", "RT-37")]
    public async Task ShouldLinkSectionToSymbol_WhenCodeSpanNamesIt()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/Orders.cs", Orders),
            ("docs/guide.md", "# Guide\n\nCall `OrderService` first.\n"),
        ]);
        try
        {
            var builder = new RaggableTreeBuilder(new CSharpAdapter(), TestFixtures.CreateFs(dir));
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            Assert.True(Documents(result.Tree, "Guide", "OrderService"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "RT-37")]
    public void ShouldLinkSectionToSymbol_WhenLinkOrHeadingNamesIt()
    {
        const string md = "# Guide\n\nUse `OrderService` and `OrderService` again, never `Nothing`.\n\n"
            + "## Links\n\nSee [Parser](docs/parser.md).\n\n# Renderer\n\n# Shared\n\nThe `Helper` is shared.\n";
        var mapped = new MarkdownDocumentMapper().Map("/docs/a.md", md);
        var symbols = new[]
        {
            Symbol("s1", "/src/Orders.cs::OrderService", "OrderService"),
            Symbol("s2", "/src/P.cs::Parser", "Parser"),
            Symbol("s3", "/src/R.cs::Renderer", "Renderer"),
            Symbol("s4", "/src/A.cs::A.Helper", "Helper"),
            Symbol("s5", "/src/B.cs::B.Helper", "Helper"),
        };
        var nodes = mapped.Concat(symbols).ToList();

        var edges = DocumentsEdgeLinker.Link(nodes, []);

        Assert.All(edges, e => Assert.Equal(EdgeKind.Documents, e.Kind));
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var actual = edges.Select(e => $"{byId[e.FromId].Name}->{byId[e.ToId].Fqn}").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "Guide->/src/Orders.cs::OrderService",
                "Links->/src/P.cs::Parser",
                "Renderer->/src/R.cs::Renderer",
                "Shared->/src/A.cs::A.Helper",
                "Shared->/src/B.cs::B.Helper",
            ],
            actual);
    }

    [Fact]
    [Trait("BR", "RT-37")]
    public async Task ShouldRelinkDocuments_WhenMarkdownFileReindexedIncrementally()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/Orders.cs", Orders),
            ("docs/guide.md", "# Guide\n\nNothing here.\n"),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var request = new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot };
            var built = await new RaggableTreeBuilder(new CSharpAdapter(), fs)
                .BuildAsync(TestFixtures.TestVirtualRoot, request, CancellationToken.None);
            Assert.DoesNotContain(built.Tree.Edges, e => e.Kind == EdgeKind.Documents);

            await File.WriteAllTextAsync(
                Path.Combine(dir, "docs", "guide.md"), "# Guide\n\nCall `OrderService` first.\n", TestContext.Current.CancellationToken);
            var result = await new IncrementalReindexEngine(new CSharpAdapter(), fs).ReindexAsync(
                built.Tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/docs/guide.md"], request, CancellationToken.None);

            Assert.True(Documents(result.Tree, "Guide", "OrderService"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
