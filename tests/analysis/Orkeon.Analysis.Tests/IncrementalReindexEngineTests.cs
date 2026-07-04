using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

public class IncrementalReindexEngineTests
{
    [Fact]
    public async Task Modified_file_reindexes_only_that_file()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/a.ts", "export class A { run(): number { return 1; } }"),
            ("src/b.ts", "export class B { run(): number { return 2; } }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            var bNode = tree.Nodes.First(n => n.Name == "B" && n.Level == NodeLevel.L3_Symbol);
            var bOriginalId = bNode.Id;

            await File.WriteAllTextAsync(
                Path.Combine(dir, "src", "a.ts"),
                "export class A { run(): number { return 99; } }",
                TestContext.Current.CancellationToken);

            var fs = TestFixtures.CreateFs(dir);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), fs);
            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot,
                [$"{TestFixtures.TestVirtualRoot}/src/a.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.Equal(1, result.ChangedFileCount);
            Assert.Equal(1, result.ReusedFileCount);
            Assert.Contains(result.Tree.Nodes, n => n.Id == bOriginalId);
            var aSymbol = result.Tree.Nodes.First(n => n.Name == "A" && n.Level == NodeLevel.L3_Symbol);
            Assert.Contains("99", aSymbol.SourceSnippet);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Added_file_produces_new_module_and_symbols()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export class A { run(): void {} }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);

            var newFile = Path.Combine(dir, "c.ts");
            await File.WriteAllTextAsync(newFile, "export class C { run(): void {} }", TestContext.Current.CancellationToken);
            var newFileVirtual = $"{TestFixtures.TestVirtualRoot}/c.ts";

            var fs = TestFixtures.CreateFs(dir);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), fs);
            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [newFileVirtual],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.Contains(result.Tree.Nodes, n => n.VirtualFilePath == newFileVirtual && n.Level == NodeLevel.L2_Module);
            Assert.Contains(result.Tree.Nodes, n => n.Name == "C" && n.Level == NodeLevel.L3_Symbol);
            Assert.Equal(1, result.ChangedFileCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Deleted_file_drops_module_and_descendants()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export class A { run(): void {} }"),
            ("b.ts", "export class B { run(): void {} }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);

            var bPath = Path.Combine(dir, "b.ts");
            File.Delete(bPath);
            var bVirtual = $"{TestFixtures.TestVirtualRoot}/b.ts";

            var fs = TestFixtures.CreateFs(dir);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), fs);
            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [bVirtual],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.DoesNotContain(result.Tree.Nodes, n => n.VirtualFilePath == bVirtual);
            Assert.True(result.RemovedNodeCount >= 2);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task IndexId_differs_after_file_modification()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export class A { run(): number { return 1; } }"),
        ]);
        try
        {
            var (tree, indexId) = await BuildInitialAsync(dir);

            var aPath = Path.Combine(dir, "a.ts");
            await File.WriteAllTextAsync(aPath, "export class A { run(): number { return 2; } }", TestContext.Current.CancellationToken);
            var aVirtual = $"{TestFixtures.TestVirtualRoot}/a.ts";

            var fs = TestFixtures.CreateFs(dir);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), fs);
            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [aVirtual],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.NotEqual(indexId, result.IndexId);
            Assert.Equal(64, result.IndexId.Length);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Dangling_edges_are_cleaned_when_symbol_is_removed()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("b.ts", "export class B { compute(): number { return 10; } }"),
            ("a.ts", "import { B } from './b';\nexport class A { run(): number { const x = new B(); return x.compute(); } }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            Assert.Contains(tree.Edges, e => e.Kind == EdgeKind.Imports);

            var bPath = Path.Combine(dir, "b.ts");
            await File.WriteAllTextAsync(bPath,
                "export class B { renamed(): number { return 20; } }", TestContext.Current.CancellationToken);
            var bVirtual = $"{TestFixtures.TestVirtualRoot}/b.ts";

            var fs = TestFixtures.CreateFs(dir);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), fs);
            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [bVirtual],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var survivingCompute = result.Tree.Nodes.FirstOrDefault(n => n.Name == "compute");
            Assert.Null(survivingCompute);
            Assert.Contains(result.Tree.Nodes, n => n.Name == "renamed");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Vector_store_receives_delete_and_index_calls()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export class A { run(): void {} }"),
            ("b.ts", "export class B { run(): void {} }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            foreach (var node in tree.Nodes)
            {
                if (node.Level == NodeLevel.L3_Symbol)
                {
                    node.Embedding = new ReadOnlyMemory<float>([0.1f, 0.2f]);
                }
            }

            var store = new RecordingVectorStore();
            var fs = TestFixtures.CreateFs(dir);
            using var parserPool = new Analysis.TreeSitter.TreeSitterParserPool();
            var engine = new IncrementalReindexEngine(
                [new TypeScriptAdapter()],
                new FileSystemDiscoverer(fs),
                fs,
                parserPool,
                new DefaultReferenceResolver(),
                new EmbeddingTextComposer(),
                new RaggableEnrichmentServices
                {
                    Embedder = new StubEmbedder(),
                    VectorStore = store,
                });

            var aPath = Path.Combine(dir, "a.ts");
            await File.WriteAllTextAsync(aPath, "export class A { run(): number { return 7; } }", TestContext.Current.CancellationToken);
            var aVirtual = $"{TestFixtures.TestVirtualRoot}/a.ts";

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [aVirtual],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.NotEmpty(store.DeletedIds);
            Assert.NotEmpty(store.IndexedDocuments);
            Assert.All(store.IndexedDocuments, d => Assert.StartsWith(aVirtual, d.Metadata.VirtualFilePath));
            Assert.True(result.AddedNodeCount > 0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Inverse_links_are_recomputed_on_reused_nodes()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("b.ts", "export class B { compute(): number { return 10; } }"),
            ("a.ts", "import { B } from './b';\nexport class A { run(): number { const x = new B(); return x.compute(); } }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            var aModuleBefore = tree.Nodes.First(n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath.EndsWith("a.ts", StringComparison.Ordinal));
            Assert.NotEmpty(aModuleBefore.ImportIds);

            var bPath = Path.Combine(dir, "b.ts");
            await File.WriteAllTextAsync(bPath, "export class B { compute(): number { return 42; } }", TestContext.Current.CancellationToken);
            var bVirtual = $"{TestFixtures.TestVirtualRoot}/b.ts";

            var fs = TestFixtures.CreateFs(dir);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), fs);
            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [bVirtual],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var aModuleAfter = result.Tree.Nodes.First(n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath.EndsWith("a.ts", StringComparison.Ordinal));
            Assert.NotEmpty(aModuleAfter.ImportIds);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static async Task<(RaggableTree Tree, string IndexId)> BuildInitialAsync(string dir)
    {
        var fs = TestFixtures.CreateFs(dir);
        var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
        var result = await builder.BuildAsync(TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
        return (result.Tree, result.IndexId);
    }

    private sealed class StubEmbedder : IEmbeddingProvider
    {
        public int Dimensions => 2;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct)
        {
            var vectors = texts.Select(_ => new ReadOnlyMemory<float>([0.1f, 0.2f])).ToList();
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
        }
    }

    private sealed class RecordingVectorStore : IVectorStoreProvider
    {
        public ConcurrentBag<string> DeletedIds { get; } = [];
        public ConcurrentBag<VectorDocument> IndexedDocuments { get; } = [];

        public Task IndexAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct)
        {
            foreach (var d in documents) IndexedDocuments.Add(d);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            string queryText, ReadOnlyMemory<float> queryEmbedding, int topK,
            IReadOnlyDictionary<string, object>? filters, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<VectorSearchHit>>([]);

        public Task DeleteAsync(IEnumerable<string> ids, CancellationToken ct)
        {
            foreach (var id in ids) DeletedIds.Add(id);
            return Task.CompletedTask;
        }
    }
}
