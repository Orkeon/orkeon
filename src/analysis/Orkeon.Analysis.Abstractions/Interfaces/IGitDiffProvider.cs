namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface IGitDiffProvider
{
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string rootPath, string fromCommit, string toCommit, CancellationToken ct);
}
