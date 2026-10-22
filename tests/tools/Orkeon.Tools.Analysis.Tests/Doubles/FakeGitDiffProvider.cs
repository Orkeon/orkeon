using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.Analysis.Tests.Doubles;

/// <summary>Hand-written fake of <see cref="IGitDiffProvider"/> with call tracking.</summary>
public sealed class FakeGitDiffProvider : IGitDiffProvider
{
    public List<string> WorkingTreeChanges = [];
    public List<string> WorkingTreeRoots = [];
    public int WorkingTreeCallCount;

    public Task<IReadOnlyList<string>> GetChangedFilesAsync(string rootPath, string fromCommit, string toCommit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> GetWorkingTreeChangesAsync(string rootPath, CancellationToken ct)
    {
        WorkingTreeCallCount++;
        WorkingTreeRoots.Add(rootPath);
        return Task.FromResult<IReadOnlyList<string>>(WorkingTreeChanges);
    }
}
