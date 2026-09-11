using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core.Serialization;

namespace Orkeon.Analysis.Tests;

public class RaggableTreeSerializerTests
{
    [Fact]
    public async Task Roundtrip_preserves_nodes_edges_and_statements()
    {
        var tree = BuildSampleTree();
        var serializer = new RaggableTreeSerializer();

        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(tree, "idx-42", stream, CancellationToken.None);
        stream.Position = 0;

        var snapshot = await serializer.DeserializeAsync(stream, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal("idx-42", snapshot!.IndexId);
        Assert.Equal(tree.Nodes.Count, snapshot.Tree.Nodes.Count);
        Assert.Equal(tree.Edges.Count, snapshot.Tree.Edges.Count);

        var rehydrated = snapshot.Tree.Nodes.First(n => n.Id == "sym::foo");
        Assert.Equal("foo", rehydrated.Name);
        Assert.Equal(UniversalNodeKind.Method, rehydrated.Kind);
        Assert.Equal(NodeLevel.L3_Symbol, rehydrated.Level);
        Assert.Equal(["callerA"], rehydrated.CalledByIds);
        Assert.Equal(2, rehydrated.Tags.Count);
        Assert.Equal("nestjs", rehydrated.Tags["http-endpoint"]);
        Assert.Single(rehydrated.Statements);
        Assert.Equal(StatementKind.If, rehydrated.Statements[0].Kind);
        Assert.Single(rehydrated.Statements[0].Children);
    }

    [Fact]
    public async Task Embedding_roundtrips_as_base64_without_loss()
    {
        var embedding = new float[] { 0.1f, -0.2f, 3.5f, 42.42f, float.MinValue, float.MaxValue };
        var node = BuildMinimalNode("sym::vec");
        node.Embedding = embedding;
        var tree = new RaggableTree([node], [], new Dictionary<string, RaggableNode>());

        var serializer = new RaggableTreeSerializer();
        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(tree, "idx", stream, CancellationToken.None);

        stream.Position = 0;
        var snapshot = await serializer.DeserializeAsync(stream, CancellationToken.None);
        Assert.NotNull(snapshot);

        var rehydrated = snapshot!.Tree.Nodes.Single();
        Assert.NotNull(rehydrated.Embedding);
        Assert.Equal(embedding, rehydrated.Embedding!.Value.ToArray());
    }

    [Fact]
    public async Task Null_fields_are_omitted_from_json_payload()
    {
        var node = BuildMinimalNode("sym::plain");
        var tree = new RaggableTree([node], [], new Dictionary<string, RaggableNode>());

        var serializer = new RaggableTreeSerializer();
        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(tree, "idx", stream, CancellationToken.None);

        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("\"semanticSummary\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"embedding\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"parentId\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"overriddenKind\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_mismatch_is_rejected()
    {
        var fake = """{"version":"0.9","indexId":"x","createdAt":"2026-01-01T00:00:00Z","nodeCount":0,"edgeCount":0,"nodes":[],"edges":[]}""";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fake));

        var serializer = new RaggableTreeSerializer();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => serializer.DeserializeAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Enum_values_are_serialized_as_strings()
    {
        var node = BuildMinimalNode("sym::enum");
        var tree = new RaggableTree([node], [], new Dictionary<string, RaggableNode>());

        var serializer = new RaggableTreeSerializer();
        await using var stream = new MemoryStream();
        await serializer.SerializeAsync(tree, "idx", stream, CancellationToken.None);

        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"kind\":\"Method\"", json, StringComparison.Ordinal);
        Assert.Contains("\"level\":\"L3_Symbol\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gzip_reduces_payload_size_significantly()
    {
        var tree = BuildLargeSyntheticTree(nodeCount: 500);

        var serializer = new RaggableTreeSerializer();
        await using var plain = new MemoryStream();
        await serializer.SerializeAsync(tree, "idx", plain, CancellationToken.None);

        await using var compressed = new MemoryStream();
        await using (var gz = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            await serializer.SerializeAsync(tree, "idx", gz, CancellationToken.None);
        }

        var plainSize = plain.Length;
        var gzSize = compressed.Length;
        Assert.True(gzSize < plainSize * 0.4,
            $"expected >60% reduction — plain={plainSize}, gz={gzSize}");

        compressed.Position = 0;
        await using var decompress = new GZipStream(compressed, CompressionMode.Decompress);
        var snapshot = await serializer.DeserializeAsync(decompress, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(tree.Nodes.Count, snapshot!.Tree.Nodes.Count);
    }

    [Fact]
    public async Task Large_tree_serialization_does_not_regress_catastrophically()
    {
        // Regression tripwire, not a benchmark: 5000 nodes serialize in well under a
        // second on any machine, so only an accidental O(n^2) (or worse) regression can
        // reach this bound. The generous limit keeps the test deterministic on loaded
        // CI runners, where a tight wall-clock assertion flaked under contention.
        var tree = BuildLargeSyntheticTree(nodeCount: 5000);
        var serializer = new RaggableTreeSerializer();

        await using var stream = new MemoryStream();
        var sw = Stopwatch.StartNew();
        await serializer.SerializeAsync(tree, "idx", stream, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 15.0,
            $"serialization took {sw.Elapsed.TotalMilliseconds:F0}ms (tripwire limit 15000ms)");
    }

    [Fact]
    public async Task Envelope_captures_counts_and_created_at()
    {
        var tree = BuildSampleTree();
        var serializer = new RaggableTreeSerializer();

        await using var stream = new MemoryStream();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await serializer.SerializeAsync(tree, "idx-meta", stream, CancellationToken.None);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var json = JsonElement.Parse(stream.ToArray());
        Assert.Equal("2.0", json.GetProperty("version").GetString());
        Assert.Equal("idx-meta", json.GetProperty("indexId").GetString());
        Assert.Equal(tree.Nodes.Count, json.GetProperty("nodeCount").GetInt32());
        Assert.Equal(tree.Edges.Count, json.GetProperty("edgeCount").GetInt32());
        var createdAt = json.GetProperty("createdAt").GetDateTimeOffset();
        Assert.InRange(createdAt, before, after);
    }

    // ----- Helpers -----

    private static RaggableTree BuildSampleTree()
    {
        var pkg = BuildMinimalNode("pkg::root", UniversalNodeKind.Package, NodeLevel.L1_Package, name: "root");
        var mod = BuildMinimalNode("mod::a", UniversalNodeKind.Module, NodeLevel.L2_Module, name: "a");
        mod.ParentId = pkg.Id;
        pkg.ChildrenIdsMutable.Add(mod.Id);

        var foo = BuildMinimalNode("sym::foo");
        foo.ParentId = mod.Id;
        foo.Fqn = "root::a::foo";
        foo.TagsMutable["http-endpoint"] = "nestjs";
        foo.TagsMutable["service"] = "core";
        foo.CalledByIdsMutable.Add("callerA");
        foo.DecoratorsMutable.Add("@Get");
        foo.ModifiersMutable.Add("public");
        foo.Embedding = new[] { 0.1f, 0.2f, 0.3f };

        var child = new StatementNode
        {
            Id = "stmt::foo::return",
            ParentSymbolId = "sym::foo",
            Kind = StatementKind.Return,
            StartLine = 12,
            Expression = "return 42",
            Depth = 1,
        };
        var outer = new StatementNode
        {
            Id = "stmt::foo::if",
            ParentSymbolId = "sym::foo",
            Kind = StatementKind.If,
            StartLine = 10,
            EndLine = 12,
            Expression = "if (cond)",
            Condition = "cond",
            Depth = 0,
        };
        outer.AddReference(new SymbolReference("cond", "sym::cond", ReferenceKind.Read));
        outer.AddChild(child);
        foo.StatementsMutable.Add(outer);

        mod.ChildrenIdsMutable.Add(foo.Id);

        var edge = new RaggableEdge(
            "e1", "callerA", foo.Id, EdgeKind.Calls,
            new SourceLocation("/tmp/caller.ts", 5, 6, "sha"),
            "call");

        var nodes = new[] { pkg, mod, foo };
        var index = nodes.Where(n => !string.IsNullOrEmpty(n.Fqn))
                         .ToDictionary(n => n.Fqn, n => n, StringComparer.Ordinal);
        return new RaggableTree(nodes, [edge], index);
    }

    private static RaggableTree BuildLargeSyntheticTree(int nodeCount)
    {
        var nodes = new List<RaggableNode>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var n = BuildMinimalNode($"sym::n{i}");
            n.Fqn = $"pkg::n{i}";
            n.Signature = $"function n{i}()";
            n.Embedding = new[] { i / 1000f, (i + 1) / 1000f, (i + 2) / 1000f };
            nodes.Add(n);
        }
        var edges = new List<RaggableEdge>();
        for (var i = 0; i < nodeCount - 1; i++)
        {
            edges.Add(new RaggableEdge($"e{i}", nodes[i].Id, nodes[i + 1].Id, EdgeKind.Calls));
        }
        var index = nodes.ToDictionary(n => n.Fqn, n => n, StringComparer.Ordinal);
        return new RaggableTree(nodes, edges, index);
    }

    private static RaggableNode BuildMinimalNode(
        string id,
        UniversalNodeKind kind = UniversalNodeKind.Method,
        NodeLevel level = NodeLevel.L3_Symbol,
        string? name = null)
    {
        return new RaggableNode
        {
            Id = id,
            Kind = kind,
            Name = name ?? id.Split("::").Last(),
            VirtualFilePath = "/tmp/" + id + ".ts",
            Range = new NodeRange(0, 10, 1, 3, 2),
            Level = level,
            Language = "typescript",
            SourceSnippet = "snippet",
            Sha256 = "abc",
        };
    }
}
