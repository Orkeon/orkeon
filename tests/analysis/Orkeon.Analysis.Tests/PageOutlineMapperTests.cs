using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

public class PageOutlineMapperTests
{
    private const string Path = "page/1";

    private static PageElement Heading(int level, string name, int container = -1, string? anchor = null) => new()
    {
        Kind = PageElementKind.Heading,
        Role = "heading",
        Level = level,
        Name = name,
        Text = name + " body",
        Container = container,
        Anchor = anchor,
    };

    private static PageElement Region(string role, string name = "", int container = -1) => new()
    {
        Kind = PageElementKind.Region,
        Role = role,
        Name = name,
        Container = container,
    };

    private static PageElement Link(string name, string target, int container = -1) => new()
    {
        Kind = PageElementKind.Link,
        Role = "link",
        Name = name,
        Target = target,
        Container = container,
    };

    private static PageElement Control(string role, string name, int container = -1, bool visible = true) => new()
    {
        Kind = PageElementKind.Control,
        Role = role,
        Name = name,
        Container = container,
        Visible = visible,
    };

    private static RaggableTree Map(params PageElement[] elements) => PageOutlineMapper.Map(new PageOutline
    {
        Path = Path,
        Location = "https://example.test/a",
        Title = "Example",
        Elements = [.. elements],
    });

    private static RaggableNode At(RaggableTree tree, int index) =>
        tree.Nodes.Single(n => n.Id == PageOutlineMapper.NodeId(Path, index));

    private static string Root => PageOutlineMapper.RootId(Path);

    [Fact]
    public void Should_YieldAModuleNodeForThePage()
    {
        var tree = Map();

        var root = Assert.Single(tree.Nodes);
        Assert.Equal(Root, root.Id);
        Assert.Equal(UniversalNodeKind.Module, root.Kind);
        Assert.Equal(NodeLevel.L2_Module, root.Level);
        Assert.Equal("Example", root.Name);
        Assert.Equal(Path, root.VirtualFilePath);
        Assert.Equal(Path, root.Fqn);
        Assert.Equal("https://example.test/a", root.Signature);
        Assert.Equal(PageOutlineMapper.Language, root.Language);
        Assert.Empty(tree.Edges);
    }

    [Fact]
    public void Should_KeepOnThePageNodeTheTextNoHeadingHolds()
    {
        var tree = PageOutlineMapper.Map(new PageOutline
        {
            Path = Path,
            Location = "https://example.test/a",
            Title = "Example",
            Text = "What comes before the first heading.",
            Elements = [],
        });

        var root = Assert.Single(tree.Nodes);
        Assert.Equal("What comes before the first heading.", root.SourceSnippet);
        Assert.Equal("Example\nWhat comes before the first heading.", root.EmbeddingText);
    }

    [Fact]
    public void Should_EmbedThePageByItsTitle_When_NoTextComesBeforeItsHeadings()
    {
        var root = Assert.Single(Map().Nodes);

        Assert.Equal(string.Empty, root.SourceSnippet);
        Assert.Equal("Example", root.EmbeddingText);
    }

    [Fact]
    public void Should_NameThePageAfterItsUrl_When_ItHasNoTitle()
    {
        var tree = PageOutlineMapper.Map(new PageOutline { Path = Path, Location = "https://example.test/a", Elements = [] });

        Assert.Equal("https://example.test/a", Assert.Single(tree.Nodes).Name);
    }

    [Fact]
    public void Should_NestHeadingsByLevel()
    {
        var tree = Map(
            Heading(1, "Top"),      // 0
            Heading(2, "First"),    // 1
            Heading(3, "Deep"),     // 2
            Heading(2, "Second"),   // 3
            Heading(1, "Other"));   // 4

        Assert.Equal(Root, At(tree, 0).ParentId);
        Assert.Equal(At(tree, 0).Id, At(tree, 1).ParentId);
        Assert.Equal(At(tree, 1).Id, At(tree, 2).ParentId);
        Assert.Equal(At(tree, 0).Id, At(tree, 3).ParentId);
        Assert.Equal(Root, At(tree, 4).ParentId);
        Assert.All(tree.Nodes.Where(n => n.Id != Root), n => Assert.Equal(UniversalNodeKind.Section, n.Kind));
        Assert.All(tree.Nodes.Where(n => n.Id != Root), n => Assert.Equal(NodeLevel.L3_Symbol, n.Level));
        Assert.Equal("2", At(tree, 1).Tags["level"]);
        Assert.Equal("First body", At(tree, 1).SourceSnippet);
    }

    [Fact]
    public void Should_AttachAnElementToTheSectionOpenAtItsPlace()
    {
        var tree = Map(
            Control("button", "Before"),   // 0
            Heading(1, "Top"),             // 1
            Link("Docs", "/docs"),         // 2
            Heading(2, "Sub"),             // 3
            Control("textbox", "Query"));  // 4

        Assert.Equal(Root, At(tree, 0).ParentId);
        Assert.Equal(At(tree, 1).Id, At(tree, 2).ParentId);
        Assert.Equal(At(tree, 3).Id, At(tree, 4).ParentId);
    }

    [Fact]
    public void Should_PutTheContentOfARegionUnderIt_AndGoBackToTheSectionAfterIt()
    {
        var tree = Map(
            Heading(1, "Top"),                         // 0
            Region("form", "Search"),                  // 1
            Control("textbox", "Query", container: 1), // 2
            Control("button", "Go", container: 1),     // 3
            Link("After", "/after"));                  // 4

        var form = At(tree, 1);
        Assert.Equal(UniversalNodeKind.Section, form.Kind);
        Assert.Equal("form", form.Tags["role"]);
        Assert.Equal(At(tree, 0).Id, form.ParentId);
        Assert.Equal(form.Id, At(tree, 2).ParentId);
        Assert.Equal(form.Id, At(tree, 3).ParentId);
        Assert.Equal(At(tree, 0).Id, At(tree, 4).ParentId);
        Assert.Equal([At(tree, 2).Id, At(tree, 3).Id], form.ChildrenIds);
    }

    [Fact]
    public void Should_CloseAHeadingWithTheRegionThatHoldsIt()
    {
        var tree = Map(
            Region("navigation", "Menu"),          // 0
            Heading(2, "Menu", container: 0),      // 1
            Link("Home", "/", container: 0),       // 2
            Link("Outside", "/out"),               // 3
            Heading(1, "Main"),                    // 4
            Region("complementary"),              // 5
            Link("Inside", "/in", container: 5));  // 6

        Assert.Equal(At(tree, 0).Id, At(tree, 1).ParentId);
        Assert.Equal(At(tree, 1).Id, At(tree, 2).ParentId);
        Assert.Equal(Root, At(tree, 3).ParentId);
        Assert.Equal(At(tree, 4).Id, At(tree, 5).ParentId);
        Assert.Equal(At(tree, 5).Id, At(tree, 6).ParentId);
    }

    [Fact]
    public void Should_NestARegionInTheRegionThatHoldsIt()
    {
        var tree = Map(
            Region("main"),                             // 0
            Region("form", "Login", container: 0),      // 1
            Control("textbox", "User", container: 1),   // 2
            Control("button", "Help", container: 0));   // 3

        Assert.Equal(Root, At(tree, 0).ParentId);
        Assert.Equal(At(tree, 0).Id, At(tree, 1).ParentId);
        Assert.Equal(At(tree, 1).Id, At(tree, 2).ParentId);
        Assert.Equal(At(tree, 0).Id, At(tree, 3).ParentId);
    }

    [Fact]
    public void Should_IgnoreAContainerThatIsNotAnEarlierRegion()
    {
        var tree = Map(
            Heading(1, "Top"),                        // 0
            Control("button", "A", container: 0),     // 1: a heading is no region
            Control("button", "B", container: 7),     // 2: out of range
            Control("button", "C", container: 2));    // 3: a control is no region

        Assert.All(Enumerable.Range(1, 3), i => Assert.Equal(At(tree, 0).Id, At(tree, i).ParentId));
    }

    [Fact]
    public void Should_DescribeALink()
    {
        var tree = Map(Link("Docs", "https://example.test/docs"));

        var link = At(tree, 0);
        Assert.Equal(UniversalNodeKind.Link, link.Kind);
        Assert.Equal(NodeLevel.L4_Statement, link.Level);
        Assert.Equal("Docs", link.Name);
        Assert.Equal("https://example.test/docs", link.Signature);
        Assert.True(link.Tags.ContainsKey("target:https://example.test/docs"));
        Assert.Equal("link", link.Tags["role"]);
    }

    [Fact]
    public void Should_LinkAnInPageAnchorToTheElementThatCarriesIt()
    {
        var tree = Map(
            Link("Jump", "#install"),                  // 0
            Link("Nowhere", "#missing"),               // 1
            Heading(1, "Install", anchor: "install")); // 2

        var edge = Assert.Single(tree.Edges, e => e.Kind == EdgeKind.Documents);
        Assert.Equal(At(tree, 0).Id, edge.FromId);
        Assert.Equal(At(tree, 2).Id, edge.ToId);
        Assert.Equal(At(tree, 2).Fqn, At(tree, 0).Tags["resolved:" + At(tree, 2).Id]);
        Assert.DoesNotContain(At(tree, 1).Tags.Keys, k => k.StartsWith("resolved:", StringComparison.Ordinal));
        Assert.Equal("install", At(tree, 2).Tags["anchor"]);
    }

    [Fact]
    public void Should_DescribeAControl()
    {
        var tree = Map(Control("textbox", "Query"), Control("button", "Hidden", visible: false));

        var box = At(tree, 0);
        Assert.Equal(UniversalNodeKind.Control, box.Kind);
        Assert.Equal(NodeLevel.L4_Statement, box.Level);
        Assert.Equal("textbox", box.Tags["role"]);
        Assert.False(box.Tags.ContainsKey("hidden"));
        Assert.True(At(tree, 1).Tags.ContainsKey("hidden"));
    }

    [Fact]
    public void Should_HoldOneContainsEdgePerElement_AndIndexEveryNodeByFqn()
    {
        var tree = Map(Heading(1, "Top"), Control("button", "Go"), Link("Docs", "/docs"));

        var contains = tree.Edges.Where(e => e.Kind == EdgeKind.Contains).ToList();
        Assert.Equal(3, contains.Count);
        Assert.Contains(contains, e => e.FromId == Root && e.ToId == At(tree, 0).Id);
        Assert.Contains(contains, e => e.FromId == At(tree, 0).Id && e.ToId == At(tree, 1).Id);
        Assert.Equal(tree.Nodes.Count, tree.Index.Count);
        Assert.All(tree.Nodes, n => Assert.Same(n, tree.Index[n.Fqn]));
        Assert.Equal(contains.Count, contains.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Should_OrderFqnsAsTheDocument()
    {
        var elements = new List<PageElement> { Heading(1, "Top") };
        for (var i = 0; i < 12; i++) elements.Add(Control("button", "B" + i));
        elements.Add(Heading(1, "Zed"));
        var tree = Map([.. elements]);

        var byFqn = tree.Nodes.OrderBy(n => n.Fqn, StringComparer.Ordinal).Select(n => n.Id).ToList();

        Assert.Equal(tree.Nodes.Select(n => n.Id), byFqn);
        Assert.Equal(Root, byFqn[0]);
    }

    [Fact]
    public async Task Should_YieldATreeAStoreQueriesByKindSubtreeAndRole()
    {
        var tree = Map(
            Heading(1, "Top"),                         // 0
            Control("button", "One"),                  // 1
            Heading(1, "Other"),                       // 2
            Region("form", "Search"),                  // 3
            Control("textbox", "Query", container: 3), // 4
            Control("button", "Two", container: 3));   // 5
        var store = new InMemoryRaggableStore(tree, new FakeFileSystemService());
        var ct = TestContext.Current.CancellationToken;

        var all = await store.QueryAsync(new NodeQuery(), ct);
        var controls = await store.QueryAsync(new NodeQuery { Kinds = [UniversalNodeKind.Control] }, ct);
        var underOther = await store.QueryAsync(new NodeQuery { RootFqn = At(tree, 2).Id }, ct);
        var buttons = await store.QueryAsync(
            new NodeQuery { Tags = ImmutableDictionary<string, string>.Empty.Add("role", "button") }, ct);
        var children = await store.GetChildrenAsync(At(tree, 3).Id, ct);

        Assert.Equal(tree.Nodes.Select(n => n.Id), all.Select(n => n.Id));
        Assert.Equal(["One", "Query", "Two"], controls.Select(n => n.Name));
        Assert.Equal(["Other", "Search", "Query", "Two"], underOther.Select(n => n.Name));
        Assert.Equal(["One", "Two"], buttons.Select(n => n.Name));
        Assert.Equal(["Query", "Two"], children.Select(n => n.Name));
    }

    [Fact]
    public void Should_ComposeTheEmbeddingTextFromTheNameAndTheText()
    {
        var tree = Map(Heading(1, "Install"));

        Assert.Equal("Install\nInstall body", At(tree, 0).EmbeddingText);
        Assert.Equal(64, At(tree, 0).Sha256.Length);
    }
}
