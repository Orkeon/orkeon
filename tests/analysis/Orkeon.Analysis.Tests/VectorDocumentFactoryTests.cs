using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Vectors;

namespace Orkeon.Analysis.Tests;

public class VectorDocumentFactoryTests
{
    [Fact]
    public void FromTree_skips_nodes_without_embedding()
    {
        var withEmb = BuildNode("sym::a");
        withEmb.Embedding = new[] { 0.1f, 0.2f };
        var withoutEmb = BuildNode("sym::b");

        var tree = new RaggableTree([withEmb, withoutEmb], [], new Dictionary<string, RaggableNode>());

        var docs = VectorDocumentFactory.FromTree(tree);

        Assert.Single(docs);
        Assert.Equal("sym::a", docs[0].Id);
    }

    [Fact]
    public void FromTree_populates_metadata_with_kind_language_fqn_and_tags()
    {
        var node = BuildNode("sym::a");
        node.Fqn = "pkg::a";
        node.EmbeddingText = "summary + snippet";
        node.Embedding = new[] { 0.3f };
        node.TagsMutable["http-endpoint"] = "nestjs";
        node.TagsMutable["service"] = "core";
        node.DecoratorsMutable.Add("@Get");

        var tree = new RaggableTree([node], [], new Dictionary<string, RaggableNode> { [node.Fqn] = node });

        var doc = Assert.Single(VectorDocumentFactory.FromTree(tree));

        Assert.Equal("sym::a", doc.Id);
        Assert.Equal("summary + snippet", doc.Text);
        Assert.Equal(UniversalNodeKind.Method.ToString(), doc.Metadata.Kind);
        Assert.Equal("typescript", doc.Metadata.Language);
        Assert.Equal("pkg::a", doc.Metadata.Fqn);
        Assert.Contains("http-endpoint", doc.Metadata.Tags);
        Assert.Contains("@Get", doc.Metadata.Decorators);
    }

    [Fact]
    public void FromTree_counts_in_and_out_degree_from_edges()
    {
        var a = BuildNode("sym::a"); a.Embedding = new[] { 0.1f };
        var b = BuildNode("sym::b"); b.Embedding = new[] { 0.2f };
        var c = BuildNode("sym::c"); c.Embedding = new[] { 0.3f };

        var edges = new[]
        {
            new RaggableEdge("e1", "sym::a", "sym::b", EdgeKind.Calls),
            new RaggableEdge("e2", "sym::a", "sym::c", EdgeKind.Calls),
            new RaggableEdge("e3", "sym::c", "sym::b", EdgeKind.Calls),
        };
        var tree = new RaggableTree([a, b, c], edges, new Dictionary<string, RaggableNode>());

        var docs = VectorDocumentFactory.FromTree(tree);
        var byId = docs.ToDictionary(d => d.Id);

        Assert.Equal(2, byId["sym::a"].Metadata.OutDegree);
        Assert.Equal(0, byId["sym::a"].Metadata.InDegree);
        Assert.Equal(2, byId["sym::b"].Metadata.InDegree);
        Assert.Equal(0, byId["sym::b"].Metadata.OutDegree);
        Assert.Equal(1, byId["sym::c"].Metadata.InDegree);
        Assert.Equal(1, byId["sym::c"].Metadata.OutDegree);
    }

    [Fact]
    public void FromTree_falls_back_to_source_snippet_when_embedding_text_empty()
    {
        var node = BuildNode("sym::a");
        node.Embedding = new[] { 0.1f };
        // EmbeddingText left empty on purpose

        var tree = new RaggableTree([node], [], new Dictionary<string, RaggableNode>());
        var doc = Assert.Single(VectorDocumentFactory.FromTree(tree));

        Assert.Equal("snippet-source", doc.Text);
    }

    [Fact]
    public void FromTree_sets_HasParent_and_ChildCount()
    {
        var parent = BuildNode("sym::parent"); parent.Embedding = new[] { 0.1f };
        parent.ChildrenIdsMutable.Add("sym::a");
        parent.ChildrenIdsMutable.Add("sym::b");

        var child = BuildNode("sym::a"); child.Embedding = new[] { 0.2f };
        child.ParentId = parent.Id;

        var tree = new RaggableTree([parent, child], [], new Dictionary<string, RaggableNode>());
        var docs = VectorDocumentFactory.FromTree(tree).ToDictionary(d => d.Id);

        Assert.False(docs["sym::parent"].Metadata.HasParent);
        Assert.Equal(2, docs["sym::parent"].Metadata.ChildCount);
        Assert.True(docs["sym::a"].Metadata.HasParent);
    }

    private static RaggableNode BuildNode(string id)
    {
        return new RaggableNode
        {
            Id = id,
            Kind = UniversalNodeKind.Method,
            Name = id,
            VirtualFilePath = "/tmp/" + id + ".ts",
            Range = new NodeRange(0, 1, 1, 1, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "snippet-source",
            Sha256 = "sha",
        };
    }
}
