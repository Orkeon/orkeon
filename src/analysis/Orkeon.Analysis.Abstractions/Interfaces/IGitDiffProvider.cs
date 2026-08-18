namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Supplies git diffs so incremental re-indexing can scope itself to changed files.
/// </summary>
public interface IGitDiffProvider
{
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string rootPath, string fromCommit, string toCommit, CancellationToken ct);

    /// <summary>
    /// Files changed in the WORKING TREE (staged + unstaged + untracked) relative to
    /// HEAD — what a coding agent's shell edits look like before any commit. Used by
    /// the lazy-freshness pass (PLAN B3) to catch edits the file_write hook cannot see.
    /// A default empty implementation keeps third-party providers source-compatible.
    /// </summary>
    Task<IReadOnlyList<string>> GetWorkingTreeChangesAsync(string rootPath, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
