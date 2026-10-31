using System.Collections.Concurrent;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Tests.Doubles;

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
    public async Task Modified_c_file_keeps_its_include_and_calls_into_a_cpp_header()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/main.c", "#include \"util.h\"\nint run(void) { return twice(2); }\n"),
            ("src/util.h", "int twice(int a) { return a * 2; }\n"),
        ]);
        try
        {
            ILanguageAdapter[] adapters = [new CAdapter(), new CppAdapter()];
            var request = new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot };
            var initial = await new RaggableTreeBuilder(adapters, TestFixtures.CreateFs(dir))
                .BuildAsync(TestFixtures.TestVirtualRoot, request, CancellationToken.None);

            await File.WriteAllTextAsync(
                Path.Combine(dir, "src", "main.c"),
                "#include \"util.h\"\nint run(void) { return twice(3); }\n",
                TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(adapters, TestFixtures.CreateFs(dir));
            var result = await engine.ReindexAsync(
                initial.Tree, TestFixtures.TestVirtualRoot,
                [$"{TestFixtures.TestVirtualRoot}/src/main.c"],
                request,
                CancellationToken.None);

            var nodes = result.Tree.Nodes.ToList();
            Assert.Equal(1, result.ChangedFileCount);
            Assert.True(AdapterHarness.HasEdge(nodes, result.Tree.Edges, EdgeKind.Imports,
                $"{TestFixtures.TestVirtualRoot}/src/main.c", $"{TestFixtures.TestVirtualRoot}/src/util.h"));
            Assert.True(AdapterHarness.HasEdge(nodes, result.Tree.Edges, EdgeKind.Calls, "run", "twice"));
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

    [Fact]
    [Trait("BR", "RT-29")]
    public async Task ShouldKeepMarkdownNodes_WhenIncrementalReindexRunsOnAnotherFile()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("docs/guide.md", "# Guide\ntext"),
            ("src/a.ts", "export class A { run(): number { return 1; } }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            var guide = $"{TestFixtures.TestVirtualRoot}/docs/guide.md";
            Assert.Contains(tree.Nodes, n => n.VirtualFilePath == guide && n.Kind == UniversalNodeKind.Section);

            await File.WriteAllTextAsync(
                Path.Combine(dir, "src", "a.ts"),
                "export class A { run(): number { return 2; } }",
                TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));
            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/src/a.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.Contains(result.Tree.Nodes, n => n.VirtualFilePath == guide && n.Kind == UniversalNodeKind.Section && n.Name == "Guide");

            await File.WriteAllTextAsync(
                Path.Combine(dir, "docs", "guide.md"),
                "# Guide\ntext\n## More\ntext",
                TestContext.Current.CancellationToken);
            var second = await new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir)).ReindexAsync(
                result.Tree, TestFixtures.TestVirtualRoot, [guide],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var names = second.Tree.Nodes
                .Where(n => n.VirtualFilePath == guide && n.Kind == UniversalNodeKind.Section)
                .Select(n => n.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(["Guide", "More"], names);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "RT-30")]
    public async Task ShouldReadAndParseOnlyChangedFile_WhenOneMarkdownFileChanges()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("docs/one.md", "# One\ntext"),
            ("docs/two.md", "# Two\ntext"),
            ("docs/three.md", "# Three\ntext"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "docs", "two.md"), "# Two\nchanged", TestContext.Current.CancellationToken);
            var counting = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir));
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), counting);
            var two = $"{TestFixtures.TestVirtualRoot}/docs/two.md";

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [two],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var mdReads = counting.Reads.Where(p => p.EndsWith(".md", StringComparison.Ordinal)).ToArray();
            Assert.Equal([two], mdReads);
            Assert.Equal(1, result.FilesParsed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "RT-30")]
    public async Task ShouldKeepIncomingEdges_WhenTargetFileReindexed()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("b.ts", "export class B { compute(): number { return 10; } }"),
            ("a.ts", "import { B } from './b';\nexport class A { run(): number { const x = new B(); return x.compute(); } }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "b.ts"),
                "export class B { compute(): number { return 11; } extra(): number { return 1; } }", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/b.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var aModule = result.Tree.Nodes.First(n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath.EndsWith("a.ts", StringComparison.Ordinal));
            var bModule = result.Tree.Nodes.First(n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath.EndsWith("b.ts", StringComparison.Ordinal));
            Assert.Contains(result.Tree.Edges, e => e.Kind == EdgeKind.Imports && e.FromId == aModule.Id && e.ToId == bModule.Id);
            Assert.Equal(result.Tree.Edges.Count, result.TotalEdgeCount);
            Assert.Equal(1, result.FilesParsed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "RT-30")]
    public async Task ShouldDropOldNodes_WhenSymbolRemovedFromFile()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("docs/guide.md", "# Guide\ntext\n## Obsolete\nold text"),
            ("docs/other.md", "# Other\ntext"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            Assert.Contains(tree.Nodes, n => n.Name == "Obsolete");
            await File.WriteAllTextAsync(Path.Combine(dir, "docs", "guide.md"), "# Guide\ntext", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/docs/guide.md"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.DoesNotContain(result.Tree.Nodes, n => n.Name == "Obsolete");
            Assert.True(result.RemovedNodeCount >= 1);
            Assert.Equal(result.Tree.Nodes.Count, result.TotalNodeCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "RT-30")]
    public async Task ShouldPatchLexicalIndex_WhenFileReindexed()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("docs/guide.md", "# Zebra\ntext"),
            ("docs/other.md", "# Other\ntext"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            var fs = TestFixtures.CreateFs(dir);
            var store = new InMemoryRaggableStore(tree, fs);
            var guide = $"{TestFixtures.TestVirtualRoot}/docs/guide.md";
            await File.WriteAllTextAsync(Path.Combine(dir, "docs", "guide.md"), "# Quokka\ntext", TestContext.Current.CancellationToken);
            using var service = new IndexFreshnessService(
                store, () => new IncrementalReindexEngine(new TypeScriptAdapter(), fs), fs);

            var result = await service.RefreshPathsAsync([guide], TestFixtures.TestVirtualRoot, CancellationToken.None);

            Assert.Equal(1, result.FilesParsed);
            var quokka = result.Tree.Nodes.First(n => n.VirtualFilePath == guide && n.Name == "Quokka");
            var fresh = await store.SemanticSearchAsync(
                new SemanticQuery { Text = "Quokka", Mode = SearchMode.Lexical }, CancellationToken.None);
            var stale = await store.SemanticSearchAsync(
                new SemanticQuery { Text = "Zebra", Mode = SearchMode.Lexical }, CancellationToken.None);
            Assert.Contains(fresh, h => h.Fqn == quokka.Fqn);
            Assert.Empty(stale);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "RT-30")]
    public async Task ShouldIgnoreChangedPaths_WhenExcludedByDiscoveryRules()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/keep.ts", "export class Keep { run(): number { return 1; } }"),
            ("dist/x.js", "export class Dist { run() { return 1; } }"),
            ("node_modules/pkg/i.js", "export class Pkg { run() { return 1; } }"),
            ("src/types.d.ts", "export declare class Typings { run(): number; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "keep.ts"),
                "export class Keep { run(): number { return 2; } }", TestContext.Current.CancellationToken);
            var counting = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir));
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), counting);
            var root = TestFixtures.TestVirtualRoot;
            string[] excluded = [$"{root}/dist/x.js", $"{root}/node_modules/pkg/i.js", $"{root}/src/types.d.ts"];

            var result = await engine.ReindexAsync(
                tree, root, [$"{root}/src/keep.ts", .. excluded],
                new IndexCodebaseRequest { RootPath = root },
                CancellationToken.None);

            Assert.Empty(counting.Reads.Intersect(excluded));
            Assert.DoesNotContain(result.Tree.Nodes, n => excluded.Contains(n.VirtualFilePath));
            Assert.Contains(result.Tree.Nodes, n => n.Name == "Keep" && n.Level == NodeLevel.L3_Symbol && n.SourceSnippet.Contains('2'));
            Assert.Equal(1, result.FilesParsed);
            Assert.Equal(1, result.ChangedFileCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldKeepUnresolvedCalls_WhenAnotherFileReindexed()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/a.ts", "export function a(s: string): string { return s.trim(); }"),
            ("src/b.ts", "export function b(): number { return 1; }"),
        ]);
        try
        {
            var root = TestFixtures.TestVirtualRoot;
            var request = new IndexCodebaseRequest { RootPath = root };
            var built = await new RaggableTreeBuilder(new TypeScriptAdapter(), SortedFs(dir)).BuildAsync(root, request, CancellationToken.None);
            Assert.Contains(built.Tree.Edges, e => e.Kind == EdgeKind.Calls && e.ToId.StartsWith("ext::unresolved::", StringComparison.Ordinal));
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "b.ts"),
                "export function b(): number { return 2; }", TestContext.Current.CancellationToken);

            var result = await new IncrementalReindexEngine(new TypeScriptAdapter(), SortedFs(dir)).ReindexAsync(
                built.Tree, root, [$"{root}/src/b.ts"], request, CancellationToken.None);

            Assert.Equal(await FullBuildEdgesAsync(dir), EdgesOf(result.Tree));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShouldReplaceTheUnresolvedCall_WhenAnAddedFileDefinesItsName()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("src/caller.ts", CallerSource)]);
        try
        {
            var root = TestFixtures.TestVirtualRoot;
            var request = new IndexCodebaseRequest { RootPath = root };
            var built = await new RaggableTreeBuilder(new TypeScriptAdapter(), SortedFs(dir)).BuildAsync(root, request, CancellationToken.None);
            Assert.Contains(built.Tree.Edges, e => e.Kind == EdgeKind.Calls && e.ToId == "ext::unresolved::foo");
            await File.WriteAllTextAsync(Path.Combine(dir, "src", "foo.ts"),
                "export function foo(): number { return 1; }", TestContext.Current.CancellationToken);

            var result = await new IncrementalReindexEngine(new TypeScriptAdapter(), SortedFs(dir)).ReindexAsync(
                built.Tree, root, [$"{root}/src/foo.ts"], request, CancellationToken.None);

            Assert.DoesNotContain(result.Tree.Edges, e => e.ToId == "ext::unresolved::foo");
            Assert.Equal(await FullBuildEdgesAsync(dir), EdgesOf(result.Tree));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static async Task<HashSet<string>> FullBuildEdgesAsync(string dir)
    {
        var full = await new RaggableTreeBuilder(new TypeScriptAdapter(), SortedFs(dir)).BuildAsync(
            TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
        return EdgesOf(full.Tree);
    }

    // Every edge by kind and ends, an end that is no node (an `ext::` id) by its id.
    private static HashSet<string> EdgesOf(RaggableTree tree)
    {
        var byId = tree.Nodes.ToDictionary(n => n.Id, n => n.Fqn);
        return tree.Edges.Select(e => $"{e.Kind} {byId.GetValueOrDefault(e.FromId, e.FromId)} -> {byId.GetValueOrDefault(e.ToId, e.ToId)}").ToHashSet();
    }

    private const string CallerSource = "export function caller(): number { return foo(); }";

    private static async Task<HashSet<string>> FullBuildCallsAsync(string dir)
    {
        var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), SortedFs(dir));
        var full = await builder.BuildAsync(
            TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
        return CallsOf(full.Tree);
    }

    // Listing order must not depend on the disk: files are discovered in ordinal path order.
    private static FakeCountingFileSystemService SortedFs(string dir) =>
        new(TestFixtures.CreateFs(dir)) { SortListing = true };

    private static HashSet<string> CallsOf(RaggableTree tree)
    {
        var byId = tree.Nodes.ToDictionary(n => n.Id, n => n.Fqn);
        return tree.Edges.Where(e => e.Kind == EdgeKind.Calls)
            .Select(e => $"{byId[e.FromId]} -> {byId[e.ToId]}").ToHashSet();
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldKeepSingleCallsEdge_WhenNameIsAddedInAFileRankedBeforeTheCurrentTarget()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", CallerSource),
            ("b.ts", "export function other(): number { return 1; }"),
            ("d.ts", "export function foo(): number { return 4; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "b.ts"),
                "export function other(): number { return 1; }\nexport function foo(): number { return 2; }", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), SortedFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/b.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var calls = CallsOf(result.Tree).Where(c => c.EndsWith("foo", StringComparison.Ordinal)).ToList();
            var call = Assert.Single(calls);
            Assert.Contains("b.ts", call, StringComparison.Ordinal);
            Assert.Equal(await FullBuildCallsAsync(dir), CallsOf(result.Tree));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldLinkCall_WhenNameIsAddedInABrandNewFile()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", CallerSource),
            ("d.ts", "export function foo(): number { return 4; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "b.ts"),
                "export function foo(): number { return 2; }", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), SortedFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/b.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var call = Assert.Single(CallsOf(result.Tree), c => c.EndsWith("foo", StringComparison.Ordinal));
            Assert.Contains("b.ts", call, StringComparison.Ordinal);
            Assert.Equal(await FullBuildCallsAsync(dir), CallsOf(result.Tree));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static bool HasEdge(RaggableTree tree, EdgeKind kind, string fromName, string toName)
    {
        var from = tree.Nodes.Where(n => n.Name == fromName).Select(n => n.Id).ToHashSet();
        var to = tree.Nodes.Where(n => n.Name == toName).Select(n => n.Id).ToHashSet();
        return tree.Edges.Any(e => e.Kind == kind && from.Contains(e.FromId) && to.Contains(e.ToId));
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldLinkUnchangedCaller_WhenCalledSymbolIsAdded()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", CallerSource),
            ("b.ts", "export function other(): number { return 1; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            Assert.False(HasEdge(tree, EdgeKind.Calls, "caller", "foo"));
            await File.WriteAllTextAsync(Path.Combine(dir, "b.ts"),
                "export function other(): number { return 1; }\nexport function foo(): number { return 2; }", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/b.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.True(HasEdge(result.Tree, EdgeKind.Calls, "caller", "foo"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldUnlinkUnchangedCaller_WhenCalledSymbolIsRemoved()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", CallerSource),
            ("b.ts", "export function foo(): number { return 2; }"),
            ("c.ts", "export function unrelated(): number { return 3; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            Assert.True(HasEdge(tree, EdgeKind.Calls, "caller", "foo"));
            await File.WriteAllTextAsync(Path.Combine(dir, "b.ts"),
                "export function other(): number { return 1; }", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(dir, "c.ts"),
                "export function unrelated(): number { return 3; }\nexport function foo(): number { return 4; }", TestContext.Current.CancellationToken);
            var oldFoo = tree.Nodes.First(n => n.Name == "foo").Id;
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/b.ts", $"{TestFixtures.TestVirtualRoot}/c.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.DoesNotContain(result.Tree.Edges, e => e.Kind == EdgeKind.Calls && e.ToId == oldFoo);
            var cFoo = result.Tree.Nodes.First(n => n.Name == "foo" && n.VirtualFilePath.EndsWith("c.ts", StringComparison.Ordinal)).Id;
            Assert.Contains(result.Tree.Edges, e => e.Kind == EdgeKind.Calls && e.ToId == cFoo);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldNotReadUnchangedFiles_WhenRelinkingInboundCalls()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", CallerSource),
            ("b.ts", "export function other(): number { return 1; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "b.ts"),
                "export function other(): number { return 1; }\nexport function foo(): number { return 2; }", TestContext.Current.CancellationToken);
            var counting = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir));
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), counting);
            var a = $"{TestFixtures.TestVirtualRoot}/a.ts";

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/b.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.True(HasEdge(result.Tree, EdgeKind.Calls, "caller", "foo"));
            Assert.DoesNotContain(a, counting.Reads);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldLinkTestsEdge_WhenTestCallsASymbolAddedElsewhere()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.test.ts", "export function checksFoo(): number { return foo(); }\ntest('x', () => { foo(); });"),
            ("b.ts", "export function other(): number { return 1; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "b.ts"),
                "export function other(): number { return 1; }\nexport function foo(): number { return 2; }", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/b.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var fooIds = result.Tree.Nodes.Where(n => n.Name == "foo").Select(n => n.Id).ToHashSet();
            Assert.Contains(result.Tree.Edges, e => e.Kind == EdgeKind.Tests && fooIds.Contains(e.ToId));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldNotLinkCallToMarkdownSection_WhenChangedMarkdownHeadingSharesTheCalledName()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", "export function caller(): number { return install(); }"),
            ("docs/other.md", "# Other\ntext"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "docs", "x.md"), "# install\ntext", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/docs/x.md"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var section = result.Tree.Nodes.Single(n => n.Kind == UniversalNodeKind.Section && n.Name == "install");
            var caller = result.Tree.Nodes.Single(n => n.Name == "caller").Id;
            Assert.DoesNotContain(result.Tree.Edges, e => e.Kind == EdgeKind.Calls && e.FromId == caller && e.ToId == section.Id);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldKeepFirstCandidate_WhenAnotherFileDefiningTheSameNameIsEdited()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", CallerSource),
            ("b.ts", "export function foo(): number { return 1; }"),
            ("c.ts", "export function foo(): number { return 2; }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            var caller = tree.Nodes.Single(n => n.Name == "caller").Id;
            var initial = tree.Edges.Where(e => e.Kind == EdgeKind.Calls && e.FromId == caller).ToList();
            var initialTarget = tree.Nodes.Single(n => n.Id == Assert.Single(initial).ToId);
            Assert.Equal("foo", initialTarget.Name);
            var edited = initialTarget.VirtualFilePath;
            var editedName = edited.EndsWith("b.ts", StringComparison.Ordinal) ? "b.ts" : "c.ts";
            var otherName = editedName == "b.ts" ? "c.ts" : "b.ts";
            await File.WriteAllTextAsync(Path.Combine(dir, editedName),
                "export function foo(): number { return 1; }\nexport function extra(): number { return 5; }", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [edited],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var newCaller = result.Tree.Nodes.Single(n => n.Name == "caller").Id;
            var targets = result.Tree.Edges.Where(e => e.Kind == EdgeKind.Calls && e.FromId == newCaller)
                .Select(e => result.Tree.Nodes.Single(n => n.Id == e.ToId).VirtualFilePath).ToList();
            Assert.Equal([edited], targets);
            Assert.DoesNotContain(targets, p => p.EndsWith(otherName, StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string ResolvedTag(RaggableTree tree, string path) =>
        tree.Nodes.Single(n => n.VirtualFilePath == path && n.Kind == UniversalNodeKind.Link).Tags
            .Where(t => t.Key.StartsWith("resolved:", StringComparison.Ordinal))
            .Select(t => t.Value).FirstOrDefault() ?? "";

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldResolveLink_WhenChangedMarkdownIsReindexed()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("docs/a.md", "# A\ntext"),
            ("docs/b.md", "# B\ntext"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "docs", "a.md"), "# A\n[see](./b.md)", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));
            var a = $"{TestFixtures.TestVirtualRoot}/docs/a.md";
            var b = $"{TestFixtures.TestVirtualRoot}/docs/b.md";

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [a],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.StartsWith(b, ResolvedTag(result.Tree, a), StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldResolveUnchangedLink_WhenItsTargetFileIsAdded()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("docs/a.md", "# A\n[see](./b.md)"),
            ("docs/c.md", "# C\ntext"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            var a = $"{TestFixtures.TestVirtualRoot}/docs/a.md";
            var b = $"{TestFixtures.TestVirtualRoot}/docs/b.md";
            Assert.Equal("", ResolvedTag(tree, a));
            await File.WriteAllTextAsync(Path.Combine(dir, "docs", "b.md"), "# B\ntext", TestContext.Current.CancellationToken);
            var counting = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir));
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), counting);

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [b],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.StartsWith(b, ResolvedTag(result.Tree, a), StringComparison.Ordinal);
            Assert.DoesNotContain(a, counting.Reads);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R19")]
    public async Task ShouldDropResolvedTag_WhenItsTargetFileIsRemoved()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("docs/a.md", "# A\n[see](./b.md)"),
            ("docs/b.md", "# B\ntext"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            var a = $"{TestFixtures.TestVirtualRoot}/docs/a.md";
            Assert.StartsWith($"{TestFixtures.TestVirtualRoot}/docs/b.md", ResolvedTag(tree, a), StringComparison.Ordinal);
            File.Delete(Path.Combine(dir, "docs", "b.md"));
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/docs/b.md"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.Equal("", ResolvedTag(result.Tree, a));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    [Trait("BR", "R-04")]
    public async Task ShouldNotReparseFile_WhenIncrementalChangeIsIgnored()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/a.ts", "export class A { run(): number { return 1; } }"),
            ("src/b.ts", "export class B { run(): number { return 2; } }"),
        ]);
        try
        {
            var (tree, _) = await BuildInitialAsync(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, ".gitignore"), "src/b.ts\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "src", "b.ts"), "export class B { run(): number { return 77; } }", TestContext.Current.CancellationToken);
            var engine = new IncrementalReindexEngine(new TypeScriptAdapter(), TestFixtures.CreateFs(dir));

            var result = await engine.ReindexAsync(
                tree, TestFixtures.TestVirtualRoot, [$"{TestFixtures.TestVirtualRoot}/src/b.ts"],
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.Equal(0, result.FilesParsed);
            Assert.DoesNotContain(result.Tree.Nodes, n => n.Name == "B" && n.Level == NodeLevel.L3_Symbol);
            Assert.Contains(result.Tree.Nodes, n => n.Name == "A" && n.Level == NodeLevel.L3_Symbol);
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
