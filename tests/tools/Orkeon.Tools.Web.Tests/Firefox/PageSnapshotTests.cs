using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class PageSnapshotTests
{
    private const string Outline = """
        {
          "url": "https://example.test/",
          "title": "Example",
          "lead": "Welcome.",
          "text": "Welcome. Guide Install it.",
          "textTruncated": true,
          "truncated": true,
          "elements": [
            { "k": 1, "r": "navigation", "n": "Main", "c": -1, "t": "" },
            { "k": 2, "r": "link", "n": "Docs", "c": 0, "t": "", "h": "https://example.test/docs" },
            { "k": 0, "r": "heading", "n": "Guide", "l": 2, "c": -1, "t": "Install it.", "a": "guide" },
            { "k": 3, "r": "textbox", "n": "Search", "c": 2, "t": "old", "v": 0 }
          ]
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PageSnapshot Parse(int generation = 3, string json = Outline) =>
        PageSnapshot.Parse(json, ["ref-0", "ref-1", "ref-2", null], generation, new FakeFileSystemService());

    [Fact]
    public void Should_ReadThePageTheScriptDescribed()
    {
        var snapshot = Parse();

        Assert.Equal(3, snapshot.Generation);
        Assert.Equal("https://example.test/", snapshot.Url);
        Assert.Equal("Example", snapshot.Title);
        Assert.Equal("Welcome. Guide Install it.", snapshot.Text);
        Assert.True(snapshot.TextTruncated);
        Assert.True(snapshot.Truncated);
        Assert.Equal("p3#page", snapshot.RootId);
        Assert.Equal("Welcome.", snapshot.Node("p3#page").SourceSnippet);
    }

    [Fact]
    public void Should_GiveEachElementANodeNamedAfterTheSnapshot()
    {
        var snapshot = Parse();

        Assert.Equal(UniversalNodeKind.Section, snapshot.Node("p3#0").Kind);
        Assert.Equal(UniversalNodeKind.Link, snapshot.Node("p3#1").Kind);
        Assert.Equal("Guide", snapshot.Node("p3#2").Name);
        Assert.Equal("Install it.", snapshot.Node("p3#2").SourceSnippet);
        Assert.Equal(UniversalNodeKind.Control, snapshot.Node("p3#3").Kind);
        Assert.Contains("hidden", snapshot.Node("p3#3").Tags);
        Assert.Equal("p3#2", snapshot.Node("p3#3").ParentId);
    }

    [Fact]
    public async Task Should_KeepTheGraphOfThePageInAStoreOfItsOwn()
    {
        var snapshot = Parse();

        Assert.Equal(5, await snapshot.Store.GetNodeCountAsync(Ct));
        var links = await snapshot.Store.QueryAsync(new NodeQuery { Kinds = [UniversalNodeKind.Link] }, Ct);
        Assert.Equal("p3#1", Assert.Single(links).Id);
        var underTheHeading = await snapshot.Store.GetChildrenAsync("p3#2", Ct);
        Assert.Equal("p3#3", Assert.Single(underTheHeading).Id);
    }

    [Fact]
    public void Should_GiveTheBrowserReferenceOfAnElement()
    {
        Assert.Equal("ref-1", Parse().SharedId("p3#1"));
    }

    [Theory]
    [InlineData("p3#page", "is the page, not one of its elements")]
    [InlineData("p3#3", "no longer in the page")]
    [InlineData("p2#1", "belongs to an earlier reading of the page")]
    [InlineData("p3#4", "is not a node of the page")]
    [InlineData("p4#0", "is not a node of the page")]
    [InlineData("guide", "is not a node of the page")]
    public void Should_SayWhy_When_AnIdNamesNoElementOfTheSnapshot(string nodeId, string reason)
    {
        var error = Assert.Throws<FirefoxException>(() => Parse().SharedId(nodeId));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("p3#12", 3, 12)]
    [InlineData("p12#0", 12, 0)]
    [InlineData("p3#page", 3, -1)]
    public void Should_ReadANodeId(string nodeId, int generation, int index)
    {
        Assert.True(PageSnapshot.TryParseId(nodeId, out var readGeneration, out var readIndex));
        Assert.Equal(generation, readGeneration);
        Assert.Equal(index, readIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("p3")]
    [InlineData("p3#")]
    [InlineData("p#1")]
    [InlineData("x3#1")]
    [InlineData("pa#1")]
    [InlineData("p3#-1")]
    [InlineData("p3#one")]
    public void Should_RefuseWhatIsNotANodeId(string? nodeId)
    {
        Assert.False(PageSnapshot.TryParseId(nodeId, out _, out _));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Should_Fail_When_TheScriptAnswersSomethingElseThanAnOutline(string json)
    {
        var error = Assert.Throws<FirefoxException>(() => Parse(json: json));

        Assert.StartsWith("The page could not be read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_ReadAnEmptyPage()
    {
        var snapshot = PageSnapshot.Parse("{}", ImmutableArray<string?>.Empty, 1, new FakeFileSystemService());

        Assert.Equal(string.Empty, snapshot.Url);
        Assert.Single(snapshot.Tree.Nodes);
        Assert.False(snapshot.Truncated);
    }
}
