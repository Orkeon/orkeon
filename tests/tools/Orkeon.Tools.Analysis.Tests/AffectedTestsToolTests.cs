using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Fixtures.DevRepo;
using Orkeon.Tools.Analysis.Tests.Doubles;

namespace Orkeon.Tools.Analysis.Tests;

public class AffectedTestsToolTests
{
    [Fact]
    [Trait("BR", "RT-34")]
    public async Task ShouldUseGitDiff_WhenBaseGivenInsteadOfChangedFiles()
    {
        // Neither file is in the (empty) index: the query reports them as unresolved, which shows
        // the tool fed it the committed range and the working-tree changes (untracked included).
        var git = new FakeGitDiffProvider
        {
            ChangedFiles = ["Unknown/Committed.cs"],
            WorkingTreeChanges = ["Other/Edited.cs"],
        };
        using var tool = new AffectedTestsTool(TestGraph.Store([], []), git);

        var resp = await tool.ExecuteTypedForTest(new AffectedTestsRequest { Base = "main" }, CancellationToken.None);

        Assert.Equal(1, git.ChangedFilesCallCount);
        Assert.Equal("main", git.LastFromCommit);
        Assert.Equal("HEAD", git.LastToCommit);
        Assert.Equal(1, git.WorkingTreeCallCount);
        Assert.True(resp.All);
        Assert.Equal(["Other/Edited.cs", "Unknown/Committed.cs"], resp.Unresolved.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-34")]
    public async Task ShouldUseWorkingTreeChanges_WhenNeitherBaseNorChangedFilesGiven()
    {
        var git = new FakeGitDiffProvider
        {
            ChangedFiles = ["Unknown/Committed.cs"],
            WorkingTreeChanges = ["Other/Edited.cs"],
        };
        using var tool = new AffectedTestsTool(TestGraph.Store([], []), git);

        var resp = await tool.ExecuteTypedForTest(new AffectedTestsRequest(), CancellationToken.None);

        Assert.Equal("HEAD", git.LastFromCommit);
        Assert.Equal(1, git.WorkingTreeCallCount);
        Assert.True(resp.All);
        Assert.Equal(["Other/Edited.cs", "Unknown/Committed.cs"], resp.Unresolved.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "R18")]
    public async Task ShouldListTsTest_WhenAffectedTestsToolRunsOnDevRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "affected-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            IFileSystemService fs = new DiskBackedFileSystemService(dir, "/src");
            new DevRepoFixtureBuilder()
                .WithProject("P")
                .WithTestProject("P.Tests", "P")
                .WithTypeScriptFile("P/lib.ts", "export function helper() { return 1; }\n")
                .WithTypeScriptFile("P.Tests/lib.spec.ts", "import { helper } from '../P/lib';\ntest('x', () => { helper(); });\n")
                .Build(fs, "/src");
            var built = await new RaggableTreeBuilder(new TypeScriptAdapter(), fs).BuildAsync(
                "/src", new IndexCodebaseRequest { RootPath = "/src" }, CancellationToken.None);
            var module = Assert.Single(built.Tree.Nodes, n => n.Level == NodeLevel.L2_Module && n.VirtualFilePath == "/src/P.Tests/lib.spec.ts");
            using var tool = new AffectedTestsTool(new InMemoryRaggableStore(built.Tree, fs), new FakeGitDiffProvider());

            var resp = await tool.ExecuteTypedForTest(
                new AffectedTestsRequest { ChangedFiles = ["P/lib.ts"] }, CancellationToken.None);

            Assert.Equal([module.Fqn], resp.TestClasses);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
