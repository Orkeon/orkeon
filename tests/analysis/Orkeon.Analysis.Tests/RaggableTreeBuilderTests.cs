using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class RaggableTreeBuilderTests
{
    [Fact]
    public async Task Builds_tree_with_monorepo_package_module_and_symbol_levels()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/user.ts", TestFixtures.SimpleClass),
            ("src/utils.ts", TestFixtures.Utils),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L0_Monorepo);
            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L1_Package);
            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L2_Module);
            Assert.Contains(result.Tree.Nodes, n => n.Level == NodeLevel.L3_Symbol);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task IndexId_is_deterministic_for_same_sources()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/a.ts", TestFixtures.SimpleClass),
            ("src/b.ts", TestFixtures.Utils),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var r1 = await builder.BuildAsync(TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            var r2 = await builder.BuildAsync(TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            Assert.Equal(r1.IndexId, r2.IndexId);
            Assert.Equal(64, r1.IndexId.Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Embedding_text_is_populated_for_symbols()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(TestFixtures.TestVirtualRoot, new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            var symbol = result.Tree.Nodes.First(n => n.Level == NodeLevel.L3_Symbol);
            Assert.False(string.IsNullOrWhiteSpace(symbol.EmbeddingText));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RootAlias_rewrites_fqn_prefix_for_every_level()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/user.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot, RootAlias = "fixture" },
                CancellationToken.None);

            var monorepo = result.Tree.Nodes.Single(n => n.Level == NodeLevel.L0_Monorepo);
            Assert.Equal("fixture", monorepo.Fqn);

            foreach (var node in result.Tree.Nodes)
            {
                Assert.DoesNotContain(dir, node.Fqn, StringComparison.Ordinal);
                if (node.Level != NodeLevel.L0_Monorepo)
                {
                    Assert.StartsWith("fixture", node.Fqn, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Virtual_paths_never_leak_physical_disk_paths_into_output()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/user.ts", TestFixtures.SimpleClass),
            ("src/utils.ts", TestFixtures.Utils),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            foreach (var node in result.Tree.Nodes)
            {
                Assert.DoesNotContain(dir, node.VirtualFilePath, StringComparison.Ordinal);
                Assert.DoesNotContain("/workspace/", node.VirtualFilePath, StringComparison.Ordinal);
                Assert.DoesNotContain(@"C:\", node.VirtualFilePath, StringComparison.Ordinal);
                Assert.StartsWith(TestFixtures.TestVirtualRoot, node.VirtualFilePath, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RootAlias_empty_preserves_virtual_root_prefix()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/user.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var builder = new RaggableTreeBuilder(new TypeScriptAdapter(), fs);
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot, RootAlias = "" },
                CancellationToken.None);

            var monorepo = result.Tree.Nodes.Single(n => n.Level == NodeLevel.L0_Monorepo);
            Assert.Equal(TestFixtures.TestVirtualRoot, monorepo.Fqn);
            Assert.Equal(TestFixtures.TestVirtualRoot, monorepo.VirtualFilePath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task C_file_resolves_include_and_calls_into_a_header_parsed_as_cpp()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/main.c", "#include \"util.h\"\nint run(void) { return twice(2); }\n"),
            ("src/util.h", "int twice(int a) { return a * 2; }\n"),
        ]);
        try
        {
            var builder = new RaggableTreeBuilder([new CAdapter(), new CppAdapter()], TestFixtures.CreateFs(dir));
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var nodes = result.Tree.Nodes.ToList();
            Assert.Equal("cpp", nodes.Single(n => n.Name == "twice").Language);
            Assert.True(AdapterHarness.HasEdge(nodes, result.Tree.Edges, EdgeKind.Imports,
                $"{TestFixtures.TestVirtualRoot}/src/main.c", $"{TestFixtures.TestVirtualRoot}/src/util.h"));
            Assert.True(AdapterHarness.HasEdge(nodes, result.Tree.Edges, EdgeKind.Calls, "run", "twice"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeScript_file_resolves_import_of_a_javascript_file()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/app.ts", "import { helper } from './lib';\nexport function main(): number { return helper(); }\n"),
            ("src/lib.js", "export function helper() { return 1; }\n"),
        ]);
        try
        {
            var builder = new RaggableTreeBuilder([new TypeScriptAdapter(), new JavaScriptAdapter()], TestFixtures.CreateFs(dir));
            var result = await builder.BuildAsync(
                TestFixtures.TestVirtualRoot,
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            var nodes = result.Tree.Nodes.ToList();
            Assert.True(AdapterHarness.HasEdge(nodes, result.Tree.Edges, EdgeKind.Imports,
                $"{TestFixtures.TestVirtualRoot}/src/app.ts", $"{TestFixtures.TestVirtualRoot}/src/lib.js"));
            Assert.True(AdapterHarness.HasEdge(nodes, result.Tree.Edges, EdgeKind.Calls, "main", "helper"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Two packages whose files import each other, plus Markdown linking both: enough files to interleave.
    private static IEnumerable<(string Path, string Content)> ParallelFixture()
    {
        yield return ("packages/a/package.json", """{ "name": "a" }""");
        yield return ("packages/b/package.json", """{ "name": "b" }""");
        for (var i = 0; i < 12; i++)
        {
            yield return ($"packages/a/src/a{i}.ts",
                $"import {{ b{i} }} from '../../b/src/b{i}';\nexport class A{i} {{ run(): number {{ return b{i}() + {i}; }} }}\n");
            yield return ($"packages/b/src/b{i}.ts",
                $"export function b{i}(): number {{ return {i}; }}\nexport interface Shape{i} {{ size: number; }}\n");
        }
        for (var i = 0; i < 4; i++)
        {
            yield return ($"docs/guide{i}.md",
                $"# Guide {i}\n\nUse `A{i}` with `b{i}`, see [next](guide{(i + 1) % 4}.md).\n\n## Details\n\nMore on `Shape{i}`.\n");
        }
    }

    private static async Task<BuildResult> BuildAsync(string dir, int maxParallelism, CancellationToken ct, IProgress<IndexBuildProgress>? progress = null)
    {
        var fs = TestFixtures.CreateFs(dir);
        using var pool = new TreeSitterParserPool();
        var builder = new RaggableTreeBuilder(
            [new TypeScriptAdapter()], new FileSystemDiscoverer(fs), fs, pool,
            new DefaultReferenceResolver(), new EmbeddingTextComposer(),
            new RaggableEnrichmentServices { BuildProgress = progress });
        return await builder.BuildAsync(
            TestFixtures.TestVirtualRoot,
            new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot, MaxParallelism = maxParallelism },
            ct);
    }

    [Fact]
    public async Task Parallel_build_yields_the_same_index_as_a_sequential_build()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([.. ParallelFixture()]);
        try
        {
            var sequential = await BuildAsync(dir, 1, CancellationToken.None);
            var parallel = await BuildAsync(dir, 8, CancellationToken.None);
            var perProcessor = await BuildAsync(dir, 0, CancellationToken.None);

            Assert.Contains(sequential.Tree.Edges, e => e.Kind == EdgeKind.Imports);
            Assert.Contains(sequential.Tree.Edges, e => e.Kind == EdgeKind.Documents);
            foreach (var other in new[] { parallel, perProcessor })
            {
                Assert.Equal(sequential.IndexId, other.IndexId);
                Assert.Equal(sequential.FileCount, other.FileCount);
                Assert.Equal(sequential.Tree.Nodes.Select(n => (n.Id, n.ParentId)), other.Tree.Nodes.Select(n => (n.Id, n.ParentId)));
                Assert.Equal(sequential.Tree.Edges.Select(e => e.Id), other.Tree.Edges.Select(e => e.Id));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Parallel_build_reports_each_parsed_file_once()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([.. ParallelFixture()]);
        try
        {
            var progress = new FakeIndexBuildProgress();
            var result = await BuildAsync(dir, 8, CancellationToken.None, progress);

            var parse = progress.Reports.Where(r => r.Phase == IndexBuildPhase.Parse).ToList();
            Assert.All(parse, r => Assert.Equal(result.FileCount, r.Total));
            Assert.Equal(Enumerable.Range(1, result.FileCount), parse.Select(r => r.Current).Order());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Parallel_build_stops_when_cancelled()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([.. ParallelFixture()]);
        try
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BuildAsync(dir, 8, cts.Token));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("BR", "R-39")]
    public async Task ShouldReportMonotonicParseProgress_WhenWorkersRunInParallel()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([.. ParallelFixture()]);
        try
        {
            using var progress = new GatedIndexBuildProgress();
            await BuildAsync(dir, 4, CancellationToken.None, progress);

            var parse = progress.Reports.Where(r => r.Phase == IndexBuildPhase.Parse).Select(r => r.Current).ToList();
            Assert.True(parse.Count >= 2);
            Assert.Equal(parse.Order(), parse);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Negative_max_parallelism_is_refused()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([("src/user.ts", TestFixtures.SimpleClass)]);
        try
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => BuildAsync(dir, -1, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
