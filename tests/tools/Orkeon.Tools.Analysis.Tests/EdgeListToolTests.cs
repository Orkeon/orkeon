using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Analysis.Tests;

public class EdgeListToolTests
{
    [Fact]
    public void Has_stable_tool_contract()
    {
        using var tool = new EdgeListTool(TestGraph.Store([], []));
        Assert.Equal("edge_list", tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldReturnEveryInheritsEdgeAcrossPages_WhenTheIndexHoldsMoreThanAThousandSymbols()
    {
        // 1500 derived classes, each extending one of three bases, plus 1500 unrelated functions with calls between
        // them: more symbols than dependency_graph's max_nodes cap, and the inherits edges are spread among them.
        var nodes = new List<RaggableNode>();
        var edges = new List<RaggableEdge>();
        var bases = Enumerable.Range(0, 3)
            .Select(i => TestGraph.Symbol($"/src/base.ts::Base{i}", "/src/base.ts", kind: UniversalNodeKind.Class))
            .ToList();
        nodes.AddRange(bases);
        for (var i = 0; i < 1500; i++)
        {
            var fn = TestGraph.Symbol($"/src/a{i:D4}.ts::fn{i:D4}", $"/src/a{i:D4}.ts");
            var derived = TestGraph.Symbol($"/src/z{i:D4}.ts::Derived{i:D4}", $"/src/z{i:D4}.ts", kind: UniversalNodeKind.Class);
            nodes.Add(fn);
            nodes.Add(derived);
            edges.Add(TestGraph.Edge(derived, bases[i % 3], EdgeKind.Extends));
            if (i > 0) edges.Add(TestGraph.Edge(nodes[^4], fn, EdgeKind.Calls));
        }

        using var tool = new EdgeListTool(TestGraph.Store(nodes, edges));

        var seen = new List<EdgeListItem>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await tool.ExecuteTypedForTest(
                new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits"), Cursor = cursor, PageSize = 400 },
                CancellationToken.None);
            Assert.Equal(1500, page.Total);
            seen.AddRange(page.Edges);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(4, pages);
        Assert.Equal(1500, seen.Count);
        Assert.All(seen, e => Assert.Equal(EdgeKind.Extends, e.Kind));
        Assert.Equal(1500, seen.Select(e => e.From).Distinct(StringComparer.Ordinal).Count());
        // Every base class is one call away: the targets of the inherits edges.
        Assert.Equal(["/src/base.ts::Base0", "/src/base.ts::Base1", "/src/base.ts::Base2"],
            seen.Select(e => e.To).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(seen.Select(e => e.From).Order(StringComparer.Ordinal), seen.Select(e => e.From));
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldGiveBothEndsAsFqnsAndIds_WhenListingImplementsAndExtends()
    {
        var shape = TestGraph.Symbol("/src/s.ts::IShape", "/src/s.ts", kind: UniversalNodeKind.Interface);
        var baseClass = TestGraph.Symbol("/src/s.ts::ShapeBase", "/src/s.ts", kind: UniversalNodeKind.Class);
        var circle = TestGraph.Symbol("/src/c.ts::Circle", "/src/c.ts", kind: UniversalNodeKind.Class);
        var draw = TestGraph.Symbol("/src/c.ts::draw", "/src/c.ts");
        using var tool = new EdgeListTool(TestGraph.Store(
            [shape, baseClass, circle, draw],
            [
                TestGraph.Edge(circle, shape, EdgeKind.Implements),
                TestGraph.Edge(circle, baseClass, EdgeKind.Extends),
                TestGraph.Edge(draw, circle, EdgeKind.Calls),
            ]));

        var page = await tool.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits") },
            CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Null(page.NextCursor);
        Assert.Collection(
            page.Edges,
            e =>
            {
                Assert.Equal(("/src/c.ts::Circle", "/src/s.ts::IShape", EdgeKind.Implements), (e.From, e.To, e.Kind));
                Assert.Equal((circle.Id, shape.Id), (e.FromId, e.ToId));
            },
            e => Assert.Equal(("/src/c.ts::Circle", "/src/s.ts::ShapeBase", EdgeKind.Extends), (e.From, e.To, e.Kind)));
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldKeepTheEdgesTouchingTheRoot_WhenRootFqnIsGiven()
    {
        var a = TestGraph.Symbol("/src/a.ts::A", "/src/a.ts", kind: UniversalNodeKind.Class);
        var b = TestGraph.Symbol("/src/b.ts::B", "/src/b.ts", kind: UniversalNodeKind.Class);
        var c = TestGraph.Symbol("/src/c.ts::C", "/src/c.ts", kind: UniversalNodeKind.Class);
        var d = TestGraph.Symbol("/src/d.ts::D", "/src/d.ts", kind: UniversalNodeKind.Class);
        using var tool = new EdgeListTool(TestGraph.Store(
            [a, b, c, d],
            [TestGraph.Edge(a, b, EdgeKind.Extends), TestGraph.Edge(c, a, EdgeKind.Extends), TestGraph.Edge(d, c, EdgeKind.Extends)]));

        var page = await tool.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits"), RootFqn = "/src/a.ts" },
            CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal(["/src/a.ts::A", "/src/c.ts::C"], page.Edges.Select(e => e.From));
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldResumeAfterTheLastEdge_WhenAnEdgeOfAPreviousPageIsGone()
    {
        var target = TestGraph.Symbol("/src/t.ts::T", "/src/t.ts", kind: UniversalNodeKind.Class);
        var sources = Enumerable.Range(0, 5)
            .Select(i => TestGraph.Symbol($"/src/s{i}.ts::S{i}", $"/src/s{i}.ts", kind: UniversalNodeKind.Class))
            .ToList();
        var edges = sources.Select(s => TestGraph.Edge(s, target, EdgeKind.Extends)).ToList();
        using var first = new EdgeListTool(TestGraph.Store([target, .. sources], edges));
        var page1 = await first.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits"), PageSize = 2 },
            CancellationToken.None);

        // The index changed between two pages: the first edge is gone. The cursor still resumes after S1.
        using var second = new EdgeListTool(TestGraph.Store([target, .. sources], edges.Skip(1).ToList()));
        var page2 = await second.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits"), PageSize = 2, Cursor = page1.NextCursor },
            CancellationToken.None);

        Assert.Equal(["/src/s0.ts::S0", "/src/s1.ts::S1"], page1.Edges.Select(e => e.From));
        Assert.Equal(["/src/s2.ts::S2", "/src/s3.ts::S3"], page2.Edges.Select(e => e.From));
        Assert.NotNull(page2.NextCursor);
    }

    [Fact]
    public async Task ShouldRefuseTheRequest_WhenTheCursorIsNotOneItGave()
    {
        using var tool = new EdgeListTool(TestGraph.Store([], []));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits"), Cursor = "not-a-cursor" },
            CancellationToken.None));

        Assert.Contains("cursor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldRefuseUnknownEdgeKindByName_WhenListingEdges()
    {
        using var tool = new EdgeListTool(TestGraph.Store([], []));

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => tool.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("nonsense") },
            CancellationToken.None));

        Assert.Contains("nonsense", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldRequireEdgeKindsAndABoundedPageSize_WhenValidated()
    {
        using var tool = new EdgeListTool(TestGraph.Store([], []));

        Assert.Contains("edge_kinds", tool.ValidateTypedForTest(new EdgeListRequest()), StringComparison.Ordinal);
        Assert.Contains("page_size", tool.ValidateTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("calls"), PageSize = EdgeListRequest.MaxPageSize + 1 }), StringComparison.Ordinal);
        Assert.Null(tool.ValidateTypedForTest(new EdgeListRequest { EdgeKinds = TestGraph.Arr("calls") }));
    }


    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldStopAtTheNamespaceBoundary_WhenRootFqnNamesANamespace()
    {
        var baseType = TestGraph.Symbol("/r/b.cs::Orkeon.Base", "/r/b.cs", kind: UniversalNodeKind.Class, language: "csharp");
        var foo = TestGraph.Symbol("/r/d.cs::Orkeon.Domain.Foo", "/r/d.cs", kind: UniversalNodeKind.Class, language: "csharp");
        var fooTests = TestGraph.Symbol("/r/t.cs::Orkeon.DomainTests.FooTests", "/r/t.cs", kind: UniversalNodeKind.Class, language: "csharp");
        using var tool = new EdgeListTool(TestGraph.Store(
            [baseType, foo, fooTests],
            [TestGraph.Edge(foo, baseType, EdgeKind.Extends), TestGraph.Edge(fooTests, baseType, EdgeKind.Extends)]));

        var page = await tool.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits"), RootFqn = "Orkeon.Domain" },
            CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal(["/r/d.cs::Orkeon.Domain.Foo"], page.Edges.Select(e => e.From));
    }

    [Trait("BR", "#280")]
    [Fact]
    public async Task ShouldFindTheEdgesOfTheTypesOfANamespace_WhenRootFqnIsANamespaceNode()
    {
        var baseType = TestGraph.Symbol("/r/A/Base.cs::Base", "/r/A/Base.cs", kind: UniversalNodeKind.Class, language: "csharp");
        var beta = TestGraph.Symbol("/r/B/Options.cs::Beta", "/r/B/Options.cs", kind: UniversalNodeKind.Namespace, language: "csharp");
        var options = TestGraph.Symbol("/r/B/Options.cs::Options", "/r/B/Options.cs", kind: UniversalNodeKind.Class, language: "csharp");
        var gamma = TestGraph.Symbol("/r/G/G.cs::Gamma", "/r/G/G.cs", kind: UniversalNodeKind.Namespace, language: "csharp");
        var g = TestGraph.Symbol("/r/G/G.cs::G", "/r/G/G.cs", kind: UniversalNodeKind.Class, language: "csharp");
        using var tool = new EdgeListTool(TestGraph.Store(
            [baseType, beta, options, gamma, g],
            [TestGraph.Edge(options, baseType, EdgeKind.Extends), TestGraph.Edge(g, baseType, EdgeKind.Extends)]));

        var page = await tool.ExecuteTypedForTest(
            new EdgeListRequest { EdgeKinds = TestGraph.Arr("inherits"), RootFqn = beta.Fqn },
            CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal(["/r/B/Options.cs::Options"], page.Edges.Select(e => e.From));
    }
}
