using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

public class InMemoryRaggableStoreTests
{
    private static readonly float[] UnitEmbedding = [1f, 0f, 0f];

    private static async Task<(InMemoryRaggableStore Store, string Dir, BuildResult Result)> BuildStoreAsync(
        (string Path, string Content)[] files,
        Func<string, CancellationToken, Task<ReadOnlyMemory<float>?>>? embedder = null)
    {
        var dir = await TestFixtures.WriteDirectoryAsync(files);
        var fs = TestFixtures.CreateFs(dir);
        var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
        var result = await builder.BuildAsync(TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
        return (new InMemoryRaggableStore(result.Tree, new FakeFileSystemService(), embedder), dir, result);
    }

    [Fact]
    public async Task GetAsync_resolves_by_fqn()
    {
        var (store, dir, result) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var any = result.Tree.Nodes.First(n => n.Level == NodeLevel.L3_Symbol);
            var fetched = await store.GetAsync(any.Fqn, CancellationToken.None);
            Assert.NotNull(fetched);
            Assert.Equal(any.Fqn, fetched!.Fqn);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task QueryAsync_filters_by_level_and_kinds()
    {
        var (store, dir, _) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var query = new NodeQuery
            {
                Level = NodeLevel.L3_Symbol,
                Kinds = [UniversalNodeKind.Class],
            };
            var classes = await store.QueryAsync(query, CancellationToken.None);
            Assert.NotEmpty(classes);
            Assert.All(classes, n => Assert.Equal(UniversalNodeKind.Class, n.EffectiveKind));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task GetChildrenAsync_returns_module_symbols()
    {
        var (store, dir, result) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var module = result.Tree.Nodes.First(n => n.Level == NodeLevel.L2_Module);
            var children = await store.GetChildrenAsync(module.Id, CancellationToken.None);
            Assert.NotEmpty(children);
            Assert.All(children, c => Assert.Equal(module.Id, c.ParentId));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExpandAsync_walks_edges_within_budget()
    {
        var (store, dir, result) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.Calls),
        ]);
        try
        {
            var seed = result.Tree.Nodes.FirstOrDefault(n => n.Name == "run" && n.EffectiveKind == UniversalNodeKind.Method);
            if (seed is null) return;
            var query = new ExpandQuery
            {
                Seeds = [seed.Fqn],
                EdgeKinds = [EdgeKind.Calls],
                MaxDepth = 2,
                MaxNodes = 50,
            };
            var subgraph = await store.ExpandAsync(query, CancellationToken.None);
            Assert.Contains(subgraph.Nodes, n => n.Id == seed.Id);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExpandAsync_applies_max_nodes_budget()
    {
        var nodes = new List<RaggableNode>();
        var edges = new List<RaggableEdge>();
        for (var i = 0; i < 20; i++)
        {
            nodes.Add(Synthetic($"n{i}"));
        }
        for (var i = 0; i < 19; i++)
        {
            edges.Add(new RaggableEdge($"e{i}", $"n{i}", $"n{i + 1}", EdgeKind.Calls));
        }

        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        var query = new ExpandQuery
        {
            Seeds = ["n0"],
            EdgeKinds = [EdgeKind.Calls],
            MaxDepth = 30,
            MaxNodes = 5,
        };
        var subgraph = await store.ExpandAsync(query, CancellationToken.None);
        Assert.True(subgraph.Truncated);
        Assert.True(subgraph.Nodes.Length <= 5);
    }

    [Fact]
    public async Task ShortestPathAsync_returns_direct_chain()
    {
        var nodes = new List<RaggableNode>
        {
            Synthetic("a"),
            Synthetic("b"),
            Synthetic("c"),
        };
        var edges = new List<RaggableEdge>
        {
            new("e1", "a", "b", EdgeKind.Calls),
            new("e2", "b", "c", EdgeKind.Calls),
        };
        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        var path = await store.ShortestPathAsync(
            new ShortestPathQuery { From = "a", To = "c" },
            CancellationToken.None);
        Assert.NotNull(path);
        Assert.Equal(2, path!.Length);
        Assert.Equal("a", path.Fqns[0]);
        Assert.Equal("c", path.Fqns[^1]);
    }

    [Fact]
    public async Task FindAllPathsAsync_enumerates_paths()
    {
        var nodes = new List<RaggableNode>
        {
            Synthetic("a"),
            Synthetic("b"),
            Synthetic("c"),
            Synthetic("d"),
        };
        var edges = new List<RaggableEdge>
        {
            new("e1", "a", "b", EdgeKind.Calls),
            new("e2", "a", "c", EdgeKind.Calls),
            new("e3", "b", "d", EdgeKind.Calls),
            new("e4", "c", "d", EdgeKind.Calls),
        };
        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        var paths = await store.FindAllPathsAsync(
            new PathQuery { From = "a", To = "d", EdgeKinds = [EdgeKind.Calls], MaxDepth = 3 },
            CancellationToken.None);
        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public async Task FindCyclesAsync_detects_simple_cycle()
    {
        var nodes = new List<RaggableNode>
        {
            Synthetic("a", level: NodeLevel.L3_Symbol),
            Synthetic("b", level: NodeLevel.L3_Symbol),
        };
        var edges = new List<RaggableEdge>
        {
            new("e1", "a", "b", EdgeKind.Calls),
            new("e2", "b", "a", EdgeKind.Calls),
        };
        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        var cycles = await store.FindCyclesAsync(
            new CycleQuery { Scope = NodeLevel.L3_Symbol, EdgeKinds = [EdgeKind.Calls] },
            CancellationToken.None);
        Assert.NotEmpty(cycles);
        Assert.Contains(cycles, c => c.Fqns.Length >= 2);
    }

    [Fact]
    public async Task TopCentralityAsync_ranks_by_indegree()
    {
        var nodes = new List<RaggableNode>
        {
            Synthetic("hub", level: NodeLevel.L3_Symbol),
            Synthetic("a", level: NodeLevel.L3_Symbol),
            Synthetic("b", level: NodeLevel.L3_Symbol),
            Synthetic("c", level: NodeLevel.L3_Symbol),
        };
        var edges = new List<RaggableEdge>
        {
            new("e1", "a", "hub", EdgeKind.Calls),
            new("e2", "b", "hub", EdgeKind.Calls),
            new("e3", "c", "hub", EdgeKind.Calls),
        };
        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        var top = await store.TopCentralityAsync(
            new CentralityQuery { Scope = NodeLevel.L3_Symbol, Metric = CentralityMetric.InDegreeCalls, TopN = 5 },
            CancellationToken.None);
        Assert.NotEmpty(top);
        Assert.Equal("hub", top[0].Fqn);
    }

    [Fact]
    public async Task TopComplexityAsync_ranks_by_cyclomatic()
    {
        var (store, dir, _) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.ComplexMethodTs),
        ]);
        try
        {
            var top = await store.TopComplexityAsync(
                new ComplexityQuery { Metric = ComplexityMetric.Cyclomatic, TopN = 5 },
                CancellationToken.None);
            Assert.NotEmpty(top);
            Assert.True(top[0].Value >= 1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task SemanticSearchAsync_without_embedder_degrades_to_lexical_not_empty()
    {
        // The pre-hybrid pin asserted Empty here — a configured-out embedder made every
        // search look like an EMPTY CODEBASE, silently. Hybrid (the default) now degrades
        // to the BM25 half instead, and the hits say so via MatchOrigin.
        var (store, dir, _) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var hits = (await store.SemanticSearchAsync(
                new SemanticQuery { Text = "user service", TopK = 5 },
                CancellationToken.None)).Hits;
            Assert.NotEmpty(hits);
            Assert.All(hits, h => Assert.Equal("bm25", h.MatchOrigin));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task SemanticSearchAsync_explicit_Vector_mode_without_embedder_keeps_the_historical_empty()
    {
        // Mode=Vector IS the pre-hybrid contract, preserved for pinning and comparisons.
        var (store, dir, _) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var hits = (await store.SemanticSearchAsync(
                new SemanticQuery { Text = "user service", TopK = 5, Mode = SearchMode.Vector },
                CancellationToken.None)).Hits;
            Assert.Empty(hits);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task SemanticSearchAsync_caps_topk_to_budget()
    {
        var nodes = new List<RaggableNode>();
        for (var i = 0; i < 10; i++)
        {
            var n = Synthetic($"n{i}", level: NodeLevel.L3_Symbol);
            n.Embedding = new[] { 1f, 0f, 0f };
            nodes.Add(n);
        }

        Task<ReadOnlyMemory<float>?> Embed(string _, CancellationToken __)
            => Task.FromResult<ReadOnlyMemory<float>?>(UnitEmbedding);

        var store = new InMemoryRaggableStore(nodes, [], new FakeFileSystemService(), Embed);
        var hits = (await store.SemanticSearchAsync(
            new SemanticQuery { Text = "x", TopK = 500 },
            CancellationToken.None)).Hits;
        Assert.True(hits.Count <= InMemoryRaggableStore.MaxTopK);
    }

    [Fact]
    public async Task GetSourceAsync_returns_signature_only()
    {
        var (store, dir, result) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var symbol = result.Tree.Nodes.First(n => n.Level == NodeLevel.L3_Symbol);
            var slice = await store.GetSourceAsync(symbol.Fqn, SourceMode.SignatureOnly, CancellationToken.None);
            Assert.NotNull(slice);
            Assert.Equal(symbol.Signature, slice!.Source);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task GetSourceAsync_returns_null_for_missing_fqn()
    {
        var (store, dir, _) = await BuildStoreAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var slice = await store.GetSourceAsync("missing::symbol", SourceMode.SignatureOnly, CancellationToken.None);
            Assert.Null(slice);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task GetEdgesAsync_filters_by_direction()
    {
        var nodes = new List<RaggableNode>
        {
            Synthetic("a"),
            Synthetic("b"),
        };
        var edges = new List<RaggableEdge>
        {
            new("e1", "a", "b", EdgeKind.Calls),
        };
        var store = new InMemoryRaggableStore(nodes, edges, new FakeFileSystemService());
        var outgoing = await store.GetEdgesAsync("a", EdgeKind.Calls, Direction.Forward, CancellationToken.None);
        var incoming = await store.GetEdgesAsync("a", EdgeKind.Calls, Direction.Backward, CancellationToken.None);
        Assert.Single(outgoing);
        Assert.Empty(incoming);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldLeaveOutEveryNodeOfAFileOutsideTheScope_WhenAScopedToolCallReadsTheStore()
    {
        // Arrange: /src/a is in the scope, /src/b is not; the visible symbol calls the hidden one.
        var monorepo = Located("/src", "/src", NodeLevel.L0_Monorepo);
        var package = Located("/src::b", "/src/b", NodeLevel.L1_Package);
        var visible = Located("/src/a/x.ts::run", "/src/a/x.ts");
        var hidden = Located("/src/b/y.ts::run", "/src/b/y.ts");
        var store = new InMemoryRaggableStore(
            [monorepo, package, visible, hidden],
            [new RaggableEdge("e1", visible.Id, hidden.Id, EdgeKind.Calls)],
            new FakeFileSystemService().AddMount("/src").AddFile("/src/b/y.ts", "secret"));
        var ct = TestContext.Current.CancellationToken;
        var scope = Orkeon.Domain.FileSystem.AgentFileSystemScope.Resolve(
            "reader",
            [new Orkeon.Domain.FileSystem.FileSystemScopeEntry("/src/a", null)],
            [new Orkeon.Domain.FileSystem.MountInfo("/src", Orkeon.Domain.FileSystem.FileAccessRights.ReadWrite, [])]);
        await using var run = Orkeon.Domain.Execution.AgentRunScope.Begin("id", "reader", null, scope);
        using var call = Orkeon.Domain.Execution.AgentRunScope.EnterToolCall();

        // Act + Assert
        Assert.Null(await store.GetAsync(hidden.Fqn, ct));
        Assert.Null(await store.GetSourceAsync(hidden.Fqn, SourceMode.FullSpan, ct));
        Assert.Equal([visible.Fqn], (await store.GetManyAsync([visible.Fqn, hidden.Fqn], ct)).Select(n => n.Fqn));
        Assert.Equal([visible.Fqn], (await store.FindByLocalNameAsync("run", ct)).Select(n => n.Fqn));
        Assert.Equal(
            [monorepo.Fqn, visible.Fqn],
            (await store.QueryAsync(new NodeQuery { Take = 50 }, ct)).Select(n => n.Fqn).Order(StringComparer.Ordinal));
        Assert.Equal(2, await store.GetNodeCountAsync(ct));
        Assert.Empty(await store.GetEdgesAsync(visible.Fqn, EdgeKind.None, Direction.Both, ct));
        Assert.Empty((await store.ListEdgesAsync(new EdgeListQuery(), ct)).Edges);
        var expanded = await store.ExpandAsync(new ExpandQuery { Seeds = [visible.Fqn], EdgeKinds = [EdgeKind.Calls] }, ct);
        Assert.Equal([visible.Fqn], expanded.Nodes.Select(n => n.Fqn));
        Assert.Empty(expanded.Edges);
    }

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldAnswerEveryNode_WhenNoToolCallScopeIsInForce()
    {
        var hidden = Located("/src/b/y.ts::run", "/src/b/y.ts");
        var store = new InMemoryRaggableStore([hidden], [], new FakeFileSystemService());
        var scope = Orkeon.Domain.FileSystem.AgentFileSystemScope.Resolve(
            "reader",
            [new Orkeon.Domain.FileSystem.FileSystemScopeEntry("/src/a", null)],
            [new Orkeon.Domain.FileSystem.MountInfo("/src", Orkeon.Domain.FileSystem.FileAccessRights.ReadWrite, [])]);
        // The run carries a scope, but the framework's own reads around a tool call are not bound by it.
        await using var run = Orkeon.Domain.Execution.AgentRunScope.Begin("id", "reader", null, scope);

        Assert.NotNull(await store.GetAsync(hidden.Fqn, TestContext.Current.CancellationToken));
    }

    private static RaggableNode Located(string fqn, string path, NodeLevel level = NodeLevel.L3_Symbol) => new()
    {
        Id = fqn + "#id",
        Kind = level == NodeLevel.L3_Symbol ? UniversalNodeKind.Function : UniversalNodeKind.Package,
        Name = fqn,
        VirtualFilePath = path,
        Range = new NodeRange(0, 0, 1, 1, 0),
        Level = level,
        Language = "typescript",
        SourceSnippet = "snapshot of " + fqn,
        Sha256 = string.Empty,
        Fqn = fqn,
    };

    private static RaggableNode Synthetic(string fqn, NodeLevel level = NodeLevel.L3_Symbol)
    {
        return new RaggableNode
        {
            Id = fqn,
            Kind = UniversalNodeKind.Function,
            Name = fqn,
            VirtualFilePath = $"/tmp/{fqn}.ts",
            Range = new NodeRange(0, 0, 0, 0, 0),
            Level = level,
            Language = "typescript",
            SourceSnippet = string.Empty,
            Sha256 = string.Empty,
            Fqn = fqn,
        };
    }
}
