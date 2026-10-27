using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis;

namespace Orkeon.Analysis.Tests;

public class AdminToolsTests
{
    [Fact]
    public async Task IndexCodebase_populates_store()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
            ("src/b.ts", TestFixtures.Utils),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
            RaggableTreeBuilder Factory() => new(new TypeScriptAdapter(), fs);
            using var tool = new IndexCodebaseTool(store, Factory);
            var resp = await tool.ExecuteTypedForTest(
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);
            Assert.True(resp.NodeCount > 0);
            Assert.NotEmpty(resp.IndexId);
            Assert.Equal(2, resp.FileCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task IncrementalReindex_refreshes_only_changed_files()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("package.json", TestFixtures.PackageJson),
            ("src/a.ts", TestFixtures.SimpleClass),
            ("src/b.ts", TestFixtures.Utils),
        ]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
            RaggableTreeBuilder Factory() => new(new TypeScriptAdapter(), fs);
            using var indexTool = new IndexCodebaseTool(store, Factory);
            var initial = await indexTool.ExecuteTypedForTest(new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot }, CancellationToken.None);
            Assert.True(initial.NodeCount > 0);

            await File.WriteAllTextAsync(Path.Combine(dir, "src/a.ts"),
                TestFixtures.SimpleClass + "\nexport const EXTRA = 1;\n", TestContext.Current.CancellationToken);

            using var reindexTool = new IncrementalReindexTool(store, Factory);
            var resp = await reindexTool.ExecuteTypedForTest(
                new IncrementalReindexRequest
                {
                    RootPath = TestFixtures.TestVirtualRoot,
                    ChangedFiles = [$"{TestFixtures.TestVirtualRoot}/src/a.ts"],
                },
                CancellationToken.None);
            Assert.True(resp.NodeCount > 0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task IncrementalReindex_with_empty_list_returns_unchanged()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([]);
        try
        {
            var fs = TestFixtures.CreateFs(dir);
            var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
            RaggableTreeBuilder Factory() => new(new TypeScriptAdapter(), fs);
            using var tool = new IncrementalReindexTool(store, Factory);
            var resp = await tool.ExecuteTypedForTest(
                new IncrementalReindexRequest { RootPath = TestFixtures.TestVirtualRoot, ChangedFiles = [] },
                CancellationToken.None);
            Assert.Equal("unchanged", resp.IndexId);
            Assert.Equal(0, resp.NodeCount);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Issue #215: one refused file never fails index_codebase; the caller learns what was skipped.
    [Fact]
    public async Task IndexCodebase_completes_and_reports_a_file_the_vfs_refuses()
    {
        var dir = await TestFixtures.WriteDirectoryAsync([
            ("src/run.sh", "#!/usr/bin/env bash\necho hi\n"),
            ("src/a.ts", TestFixtures.SimpleClass),
        ]);
        try
        {
            var denied = $"{TestFixtures.TestVirtualRoot}/src/run.sh";
            var fs = new FakeCountingFileSystemService(TestFixtures.CreateFs(dir)) { DeniedReadPaths = { denied } };
            var store = new InMemoryRaggableStore([], [], new FakeFileSystemService());
            RaggableTreeBuilder Factory() => new(new TypeScriptAdapter(), fs);
            using var tool = new IndexCodebaseTool(store, Factory);

            var resp = await tool.ExecuteTypedForTest(
                new IndexCodebaseRequest { RootPath = TestFixtures.TestVirtualRoot },
                CancellationToken.None);

            Assert.Equal(1, resp.FileCount);
            Assert.True(resp.NodeCount > 0);
            Assert.Equal(1, resp.FilterStats.ExcludedByPolicy);
            Assert.Equal([denied], resp.FilterStats.ExcludedByPolicyPaths.ToArray());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
