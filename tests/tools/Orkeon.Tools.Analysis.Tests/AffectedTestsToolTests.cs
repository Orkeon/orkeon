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
}
