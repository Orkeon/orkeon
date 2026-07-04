using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis;

namespace Orkeon.Analysis.Tests;

public class ToolsBatch1Tests
{
    private static readonly float[] UnitEmbedding = [1f, 0f, 0f];

    private static async Task<(InMemoryRaggableStore Store, string Dir, BuildResult Result)> BuildAsync(
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
    public async Task CodebaseMap_returns_package_entries()
    {
        var (store, dir, _) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var tool = new CodebaseMapTool(store);
            var resp = await tool.ExecuteTypedForTest(new CodebaseMapRequest { Level = NodeLevel.L1_Package }, CancellationToken.None);
            Assert.True(resp.Totals.PackageCount >= 1);
            Assert.NotEmpty(resp.Entries);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CodebaseMap_includes_metrics_when_requested()
    {
        var (store, dir, _) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.ComplexMethodTs),
        ]);
        try
        {
            var tool = new CodebaseMapTool(store);
            var resp = await tool.ExecuteTypedForTest(
                new CodebaseMapRequest { Level = NodeLevel.L3_Symbol, IncludeMetrics = true },
                CancellationToken.None);
            Assert.Contains(resp.Entries, e => e.Cyclomatic is > 0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CodebaseMap_scopes_descendants_by_root_fqn_across_levels()
    {
        // Regression: Fqn.StartsWith-based scoping never matched L2/L3 under an L1
        // package because L1 Fqn ends with "::name" while L2/L3 embed the disk path.
        // Scoping now uses VirtualFilePath prefix which works across all levels.
        var (store, dir, result) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
            ("src/b.ts", TestFixtures.Utils),
        ]);
        try
        {
            var pkg = result.Tree.Nodes.First(n => n.Level == NodeLevel.L1_Package);
            var tool = new CodebaseMapTool(store);

            var moduleResp = await tool.ExecuteTypedForTest(
                new CodebaseMapRequest { Level = NodeLevel.L2_Module, RootFqn = pkg.Fqn },
                CancellationToken.None);
            Assert.NotEmpty(moduleResp.Entries);

            var symbolResp = await tool.ExecuteTypedForTest(
                new CodebaseMapRequest { Level = NodeLevel.L3_Symbol, RootFqn = pkg.Fqn },
                CancellationToken.None);
            Assert.NotEmpty(symbolResp.Entries);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CodebaseMap_scopes_descendants_with_root_alias()
    {
        // Scoping must keep working when FQN are rewritten via RootAlias: the
        // VirtualFilePath-based filter is independent of the FQN prefix.
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot, RootAlias = "fixture" },
                CancellationToken.None);
            var store = new InMemoryRaggableStore(result.Tree, new FakeFileSystemService());

            var pkg = result.Tree.Nodes.First(n => n.Level == NodeLevel.L1_Package);
            Assert.StartsWith("fixture", pkg.Fqn, StringComparison.Ordinal);

            using var tool = new CodebaseMapTool(store);
            var moduleResp = await tool.ExecuteTypedForTest(
                new CodebaseMapRequest { Level = NodeLevel.L2_Module, RootFqn = pkg.Fqn },
                CancellationToken.None);
            Assert.NotEmpty(moduleResp.Entries);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task PackageSummary_returns_counts()
    {
        var (store, dir, result) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
            ("src/b.ts", TestFixtures.Utils),
        ]);
        try
        {
            var pkg = result.Tree.Nodes.First(n => n.Level == NodeLevel.L1_Package);
            var tool = new PackageSummaryTool(store);
            var resp = await tool.ExecuteTypedForTest(new PackageSummaryRequest { Fqn = pkg.Fqn }, CancellationToken.None);
            Assert.True(resp.FileCount >= 2);
            Assert.True(resp.SymbolCount >= 1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task PackageSummary_throws_fqn_not_found_with_suggestions()
    {
        var (store, dir, _) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var tool = new PackageSummaryTool(store);
            var ex = await Assert.ThrowsAsync<FqnNotFoundException>(async () =>
                await tool.ExecuteTypedForTest(new PackageSummaryRequest { Fqn = "bogus::pkg" }, CancellationToken.None));
            Assert.Equal("never-existed", ex.Reason);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task SymbolDetail_expands_members_and_caps_by_max_children()
    {
        var (store, dir, result) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var cls = result.Tree.Nodes.First(n => n.EffectiveKind == UniversalNodeKind.Class);
            var tool = new SymbolDetailTool(store);
            var resp = await tool.ExecuteTypedForTest(
                new SymbolDetailRequest
                {
                    Fqn = cls.Fqn,
                    Expand = ExpandModes.Members | ExpandModes.Callers | ExpandModes.Callees,
                    IncludeBodyMetrics = false,
                    MaxChildren = 1,
                },
                CancellationToken.None);
            Assert.Equal(cls.Fqn, resp.Fqn);
            Assert.True(resp.Members.Length <= 1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task CodebaseSearch_returns_semantic_hits()
    {
        Task<ReadOnlyMemory<float>?> Embed(string _, CancellationToken __)
            => Task.FromResult<ReadOnlyMemory<float>?>(UnitEmbedding);

        var nodes = new List<RaggableNode>
        {
            NewSymbol("alpha", [1f, 0f, 0f]),
            NewSymbol("beta", [0f, 1f, 0f]),
        };
        var store = new InMemoryRaggableStore(nodes, [], new FakeFileSystemService(), Embed);
        using var tool = new CodebaseSearchTool(store);
        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "alpha", TopK = 5 },
            CancellationToken.None);
        Assert.NotEmpty(resp.Hits);
        Assert.Equal("alpha", resp.Hits[0].Fqn);
    }

    [Fact]
    public async Task SymbolSource_returns_slice_and_sha()
    {
        var (store, dir, result) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var method = result.Tree.Nodes.First(n => n.EffectiveKind == UniversalNodeKind.Method);
            var tool = new SymbolSourceTool(store);
            var resp = await tool.ExecuteTypedForTest(
                new SymbolSourceRequest { Fqn = method.Fqn, Mode = SourceMode.SignatureOnly },
                CancellationToken.None);
            Assert.NotEmpty(resp.Source);
            Assert.Equal(64, resp.Sha256.Length);
            Assert.Equal(method.Fqn, resp.Fqn);
            Assert.Equal(resp.Sha256[..8], resp.ShortSha);
            Assert.Contains($"// FQN  : {method.Fqn}", resp.MarkdownReadyBlock);
            Assert.Contains($"// SHA  : sha256:{resp.ShortSha}", resp.MarkdownReadyBlock);
            Assert.Contains(resp.Source, resp.MarkdownReadyBlock);
            Assert.StartsWith("```" + resp.Language, resp.MarkdownReadyBlock);
            Assert.EndsWith("```", resp.MarkdownReadyBlock);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task SymbolSource_missing_fqn_throws_with_suggestions()
    {
        var (store, dir, _) = await BuildAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var tool = new SymbolSourceTool(store);
            var ex = await Assert.ThrowsAsync<FqnNotFoundException>(async () =>
                await tool.ExecuteTypedForTest(new SymbolSourceRequest { Fqn = "not::here", Mode = SourceMode.SignatureOnly }, CancellationToken.None));
            Assert.Equal("not::here", ex.Fqn);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task IndexCodebase_missing_rootpath_returns_soft_error()
    {
        var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
        using var tool = new IndexCodebaseTool(store, () => new RaggableTreeBuilder(new TypeScriptAdapter(), TestFixtures.CreateFs("/")));
        var resp = await tool.ExecuteTypedForTest(new IndexCodebaseRequest { RootPath = "" }, CancellationToken.None);
        var singleError = Assert.Single(resp.Errors);
        Assert.Equal("root_path is required. Example: index_codebase(root_path=\"/src\")", singleError);
        Assert.Equal(0, resp.NodeCount);
        Assert.Equal(0, resp.EdgeCount);
        Assert.Equal(0, resp.StatementCount);
        Assert.Equal(0, resp.FileCount);
        Assert.Equal(TimeSpan.Zero, resp.Elapsed);
    }

    private static RaggableNode NewSymbol(string name, float[] embedding)
    {
        return new RaggableNode
        {
            Id = "id::" + name,
            Kind = UniversalNodeKind.Function,
            Name = name,
            VirtualFilePath = "/tmp/" + name + ".ts",
            Range = new NodeRange(0, 0, 1, 2, 0),
            Level = NodeLevel.L3_Symbol,
            Language = "typescript",
            SourceSnippet = "function " + name + "() {}",
            Sha256 = "",
            Fqn = name,
            Embedding = embedding,
        };
    }

    // --- P2-RT-FIX-07: EmbeddingStats tests ---

    private static async Task<BuildResult> BuildWithEmbedderAsync(
        (string Path, string Content)[] files,
        IEmbeddingProvider? embedder)
    {
        var dir = await TestFixtures.WriteDirectoryAsync(files);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var adapter = new TypeScriptAdapter();
            using var pool = new Orkeon.Analysis.TreeSitter.TreeSitterParserPool();
            var discoverer = new Orkeon.Analysis.Core.FileSystemDiscoverer(fs);
            var resolver = new Orkeon.Analysis.Core.DefaultReferenceResolver();
            var composer = new Orkeon.Analysis.Core.EmbeddingTextComposer();
            var builder = new RaggableTreeBuilder(
                [adapter], discoverer, fs, pool, resolver, composer,
                new RaggableEnrichmentServices { Embedder = embedder });
            return await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task IndexCodebase_returns_embedding_stats()
    {
        var stubEmbedder = new StubEmbeddingProvider(dimensions: 3, vector: [1f, 0f, 0f]);
        var result = await BuildWithEmbedderAsync(
        [
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ], stubEmbedder);

        Assert.NotNull(result.EmbeddingStats);
        Assert.True(result.EmbeddingStats!.NodesEmbedded >= 1);
        Assert.Equal(3, result.EmbeddingStats.Dimensions);
        Assert.Equal("Stub", result.EmbeddingStats.Provider);
    }

    [Fact]
    public async Task IndexCodebase_without_embedder_returns_null_stats()
    {
        var result = await BuildWithEmbedderAsync(
        [
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ], embedder: null);

        Assert.Null(result.EmbeddingStats);
    }

    /// <summary>Minimal in-test stub for <see cref="IEmbeddingProvider"/>.</summary>
    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        private readonly float[] _vector;
        public int Dimensions { get; }

        public StubEmbeddingProvider(int dimensions, float[] vector)
        {
            Dimensions = dimensions;
            _vector = vector;
        }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct)
        {
            IReadOnlyList<ReadOnlyMemory<float>> result =
                texts.Select(_ => new ReadOnlyMemory<float>(_vector)).ToList();
            return System.Threading.Tasks.Task.FromResult(result);
        }
    }
}

internal static class ToolTestExtensions
{
    public static async Task<TRes> ExecuteTypedForTest<TReq, TRes>(
        this Orkeon.Tools.Abstractions.Base.ToolBase<TReq, TRes> tool,
        TReq request,
        CancellationToken ct)
        where TReq : class, new()
        where TRes : class
    {
        var method = typeof(Orkeon.Tools.Abstractions.Base.ToolBase<TReq, TRes>)
            .GetMethod("ExecuteTypedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var task = (Task<TRes>)method!.Invoke(tool, [request, ct])!;
        return await task.ConfigureAwait(false);
    }
}
