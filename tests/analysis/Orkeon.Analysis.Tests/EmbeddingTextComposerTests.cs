using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

public class EmbeddingTextComposerTests
{
    private static RaggableNode MakeNode(string name, string fqn) => new()
    {
        Id = fqn,
        Kind = UniversalNodeKind.Method,
        Name = name,
        VirtualFilePath = "/r/a.ts",
        Range = new NodeRange(0, 1, 1, 1, 1),
        Level = NodeLevel.L3_Symbol,
        Language = "typescript",
        SourceSnippet = "",
        Sha256 = "",
        Fqn = fqn,
    };

    [Fact]
    public void Produces_ordered_sections()
    {
        var node = MakeNode("send", "pkg::file::Class::send");
        node.Signature = "send<T>(p: T): Promise<T>";
        node.DocComment = "/** Main entry point. */";
        node.SemanticSummary = "Sends a message.";

        var sut = new EmbeddingTextComposer();
        var text = sut.Compose(node, new Dictionary<string, RaggableNode> { [node.Id] = node });

        var lines = text.Split('\n');
        Assert.StartsWith("Method pkg::file::Class::send", lines[0]);
        Assert.Contains("Promise<T>", text);
        Assert.Contains("Sends a message.", text);
    }

    [Fact]
    public void Skips_missing_sections()
    {
        var node = MakeNode("send", "pkg::Class::send");
        node.Signature = "send(): void";
        var sut = new EmbeddingTextComposer();
        var text = sut.Compose(node, new Dictionary<string, RaggableNode> { [node.Id] = node });
        Assert.DoesNotContain("\n\n", text);
    }

    [Fact]
    public void Truncates_to_max_length()
    {
        var node = MakeNode("x", "pkg::x");
        node.Signature = new string('a', 5000);
        var sut = new EmbeddingTextComposer(maxLength: 2000);
        var text = sut.Compose(node, new Dictionary<string, RaggableNode> { [node.Id] = node });
        Assert.Equal(2000, text.Length);
    }

    [Fact]
    public void ApplyToAll_fills_embedding_text()
    {
        var node = MakeNode("send", "pkg::send");
        var sut = new EmbeddingTextComposer();
        sut.ApplyToAll([node], new Dictionary<string, RaggableNode> { [node.Id] = node });
        Assert.NotEmpty(node.EmbeddingText);
    }

    private static RaggableNode MakeClassNode(string name, string fqn) => new()
    {
        Id = fqn,
        Kind = UniversalNodeKind.Class,
        Name = name,
        VirtualFilePath = "/r/a.ts",
        Range = new NodeRange(0, 1, 1, 1, 1),
        Level = NodeLevel.L3_Symbol,
        Language = "typescript",
        SourceSnippet = "",
        Sha256 = "",
        Fqn = fqn,
    };

    [Fact]
    public void Mentions_parent_in_context_section()
    {
        var parent = MakeClassNode("Class", "pkg::Class");
        var child = MakeNode("send", "pkg::Class::send");
        child.ParentId = parent.Id;
        parent.ChildrenIdsMutable.Add(child.Id);

        var sut = new EmbeddingTextComposer();
        var index = new Dictionary<string, RaggableNode> { [parent.Id] = parent, [child.Id] = child };
        var text = sut.Compose(child, index);
        Assert.Contains("member of", text);
    }
}
