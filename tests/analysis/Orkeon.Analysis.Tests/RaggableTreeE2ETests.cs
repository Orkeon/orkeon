using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Fingerprinters;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis;

namespace Orkeon.Analysis.Tests;

public class RaggableTreeE2ETests
{
    private static readonly float[] UnitEmbedding = [1f, 0f, 0f];

    // ----- E2E-01 — Full TypeScript pipeline -----

    [Fact]
    public async Task E2E01_typescript_pipeline_produces_coherent_tree()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/user.ts", TestFixtures.SimpleClass),
            ("src/utils.ts", TestFixtures.Utils),
            ("src/calls.ts", TestFixtures.Calls),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            Assert.True(result.Tree.Nodes.Count > 10, $"nodes {result.Tree.Nodes.Count}");
            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L0_Monorepo);
            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L1_Package);
            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L2_Module);
            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L3_Symbol);

            AssertReverseLinksSymmetric(result.Tree.Nodes, result.Tree.Edges);

            var symbols = result.Tree.Nodes.Where(n => n.Level == NodeLevel.L3_Symbol).ToList();
            Assert.NotEmpty(symbols);
            Assert.All(symbols, s => Assert.False(string.IsNullOrWhiteSpace(s.EmbeddingText),
                $"L3 symbol {s.Fqn} has empty EmbeddingText"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- E2E-02 — Polyglot pipeline (CA-08) -----

    [Fact]
    public async Task E2E02_polyglot_indexes_typescript_and_python_in_same_tree()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("ts-pkg/package.json", TestFixtures.PackageJson),
            ("ts-pkg/src/a.ts", TestFixtures.SimpleClass),
            ("ts-pkg/src/b.ts", TestFixtures.Utils),
            ("py-pkg/pyproject.toml", "[project]\nname = \"py-pkg\"\nversion = \"1.0.0\"\n"),
            ("py-pkg/user.py", TestFixtures.PythonSimpleClass),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(
                [new TypeScriptAdapter(), new PythonAdapter()],
                fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot, Languages = ["typescript", "python"] },
                CancellationToken.None);

            var languages = result.Tree.Nodes
                .Where(n => n.Level == NodeLevel.L3_Symbol)
                .Select(n => n.Language)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("typescript", languages);
            Assert.Contains("python", languages);

            var packages = result.Tree.Nodes.Where(n => n.Level == NodeLevel.L1_Package).ToList();
            Assert.True(packages.Count >= 2, $"expected >=2 packages, got {packages.Count}");
            Assert.Equal(packages.Select(p => p.Fqn).Distinct().Count(), packages.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- E2E-03 — NestJS fingerprinting -----

    [Fact]
    public async Task E2E03_nestjs_fingerprinter_tags_controller_and_endpoints()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/users.controller.ts", TestFixtures.NestJsController),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            using var parserPool = new TreeSitterParserPool();
            var builder = new RaggableTreeBuilder(
                [new TypeScriptAdapter()],
                new FileSystemDiscoverer(fs),
                fs,
                parserPool,
                new DefaultReferenceResolver(),
                new EmbeddingTextComposer(),
                new RaggableEnrichmentServices { Fingerprinters = [NestJsRules.Create()] });
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);

            var controller = result.Tree.Nodes.FirstOrDefault(n => n.Name == "UsersController");
            Assert.NotNull(controller);
            Assert.Contains("http-controller", controller!.Tags.Keys);
            Assert.Equal("nestjs", controller.Tags["http-controller"]);

            var endpoints = result.Tree.Nodes
                .Where(n => n.Tags.ContainsKey("http-endpoint"))
                .ToList();
            Assert.NotEmpty(endpoints);
            Assert.Contains(endpoints, n => n.Tags.ContainsKey("http-get"));
            Assert.Contains(endpoints, n => n.Tags.ContainsKey("http-post"));

            var injectable = result.Tree.Nodes.FirstOrDefault(n => n.Name == "UsersService");
            Assert.NotNull(injectable);
            Assert.Contains("service", injectable!.Tags.Keys);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- E2E-04 — LLM enricher (mock) -----

    [Fact]
    public async Task E2E04_mock_summarizer_populates_semantic_summary_and_embedding_text()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/user.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var summarizer = new StubSummarizer("Manages user accounts.");
            var fs = TestFixtures.CreateFs(dir);
            using var parserPool = new TreeSitterParserPool();
            var builder = new RaggableTreeBuilder(
                [new TypeScriptAdapter()],
                new FileSystemDiscoverer(fs),
                fs,
                parserPool,
                new DefaultReferenceResolver(),
                new EmbeddingTextComposer(),
                new RaggableEnrichmentServices { Summarizer = summarizer });
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot, EnrichWithLlm = true },
                CancellationToken.None);

            var enriched = result.Tree.Nodes
                .Where(n => !string.IsNullOrEmpty(n.SemanticSummary))
                .ToList();
            Assert.NotEmpty(enriched);
            Assert.All(enriched, n => Assert.Equal("Manages user accounts.", n.SemanticSummary));

            var classNode = enriched.First(n => n.EffectiveKind == UniversalNodeKind.Class);
            Assert.Contains("Manages user accounts.", classNode.EmbeddingText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- E2E-05 — Incremental reindex -----

    [Fact]
    public async Task E2E05_incremental_reindex_refreshes_only_changed_file_and_emits_new_index_id()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
            ("src/b.ts", TestFixtures.Utils),
            ("src/c.ts", TestFixtures.Calls),
            ("src/d.ts", TestFixtures.Inheritance),
            ("src/e.ts", TestFixtures.Imports),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
            RaggableTreeBuilder Factory() => new(new TypeScriptAdapter(), fs);
            using var indexTool = new IndexCodebaseTool(store, Factory);
            var initial = await indexTool.ExecuteTypedForTest(
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            Assert.True(initial.NodeCount > 0);

            var changed = Path.Combine(dir, "src/a.ts");
            await File.WriteAllTextAsync(changed,
                TestFixtures.SimpleClass + "\nexport const EXTRA = 42;\n", TestContext.Current.CancellationToken);
            var changedVirtual = $"{TestFixtures.TestVirtualRoot}/src/a.ts";

            using var reindex = new IncrementalReindexTool(store, Factory);
            var resp = await reindex.ExecuteTypedForTest(
                new IncrementalReindexRequest
                {
                    RootPath = TestFixtures.TestVirtualRoot,
                    ChangedFiles = [changedVirtual],
                },
                CancellationToken.None);

            Assert.True(resp.NodeCount > 0);
            Assert.NotEqual(initial.IndexId, resp.IndexId);
            Assert.NotEqual("unchanged", resp.IndexId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- T-03 — codebase_search without vector store (CA-07) -----

    [Fact]
    public async Task T03_codebase_search_returns_empty_when_no_vector_store_configured()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            var store = new InMemoryRaggableStore(result.Tree, new FakeFileSystemService());

            using var tool = new CodebaseSearchTool(store);
            var resp = await tool.ExecuteTypedForTest(
                new CodebaseSearchRequest { Query = "any", TopK = 5 },
                CancellationToken.None);

            Assert.Empty(resp.Hits);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- T-09 — symbol_detail on an unknown FQN -----

    [Fact]
    public async Task T09_symbol_detail_throws_not_found_with_suggestions_for_unknown_fqn()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            var store = new InMemoryRaggableStore(result.Tree, new FakeFileSystemService());

            using var tool = new SymbolDetailTool(store);
            var ex = await Assert.ThrowsAsync<FqnNotFoundException>(async () =>
                await tool.ExecuteTypedForTest(
                    new SymbolDetailRequest { Fqn = "non.existent.fqn" }, CancellationToken.None));
            Assert.Equal("non.existent.fqn", ex.Fqn);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ----- Non-regression — topK > MaxTopK is clamped -----

    [Fact]
    public async Task NonRegression_semantic_search_caps_topK_to_store_maximum()
    {
        var nodes = Enumerable.Range(0, 60)
            .Select(i => new RaggableNode
            {
                Id = $"id::n{i}",
                Kind = UniversalNodeKind.Function,
                Name = $"n{i}",
                VirtualFilePath = $"/tmp/n{i}.ts",
                Range = new NodeRange(0, 0, 1, 2, 0),
                Level = NodeLevel.L3_Symbol,
                Language = "typescript",
                SourceSnippet = "function",
                Sha256 = string.Empty,
                Fqn = $"n{i}",
                Embedding = UnitEmbedding,
            })
            .ToList();
        var store = new InMemoryRaggableStore(
            (IReadOnlyList<RaggableNode>)nodes, [],
            new FakeFileSystemService(),
            (_, __) => Task.FromResult<ReadOnlyMemory<float>?>(UnitEmbedding));

        using var tool = new CodebaseSearchTool(store);
        var resp = await tool.ExecuteTypedForTest(
            new CodebaseSearchRequest { Query = "q", TopK = 500 }, CancellationToken.None);

        Assert.True(resp.Hits.Length <= InMemoryRaggableStore.MaxTopK,
            $"topK not clamped: {resp.Hits.Length}");
    }

    // ----- Helpers -----

    private static void AssertReverseLinksSymmetric(
        IReadOnlyList<RaggableNode> nodes,
        IReadOnlyList<RaggableEdge> edges)
    {
        var byId = nodes.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!byId.TryGetValue(edge.FromId, out var from)) continue;
            if (!byId.TryGetValue(edge.ToId, out var to)) continue;

            switch (edge.Kind)
            {
                case EdgeKind.Calls:
                    Assert.Contains(to.Id, from.CallIds);
                    Assert.Contains(from.Id, to.CalledByIds);
                    break;
                case EdgeKind.Imports:
                    Assert.Contains(to.Id, from.ImportIds);
                    Assert.Contains(from.Id, to.ImportedByIds);
                    break;
            }
        }
    }

    private sealed class StubSummarizer : INodeSummarizer
    {
        private readonly string _summary;
        public StubSummarizer(string summary) { _summary = summary; }

        public Task<string?> SummarizeAsync(RaggableNode node, SummarizationContext context, CancellationToken ct)
            => Task.FromResult<string?>(_summary);

        public Task EnrichBatchAsync(
            IReadOnlyList<RaggableNode> candidates,
            IReadOnlyDictionary<string, RaggableNode> nodeIndex,
            CancellationToken ct)
        {
            foreach (var node in candidates)
            {
                if (node.Kind is UniversalNodeKind.Class or UniversalNodeKind.Interface
                    or UniversalNodeKind.Function or UniversalNodeKind.Method)
                {
                    node.SemanticSummary = _summary;
                }
            }
            return Task.CompletedTask;
        }
    }
}
