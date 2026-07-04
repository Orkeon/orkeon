using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;

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
}
