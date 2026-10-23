using System.Security.Cryptography;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core.Editing;

/// <summary>
/// Applies operations over several files as one unit: everything is edited and parse-checked in memory, the original
/// bytes go to a journal, then the files are written; a failed write or a red build restores them from memory.
/// </summary>
internal sealed class EditTransaction
{
    private readonly IFileSystemService _fs;
    private readonly StructuralEditEngine _engine;
    private readonly IBuildValidator? _buildValidator;
    private readonly List<(string Path, IEditOperation Operation)> _operations = [];

    public EditTransaction(IFileSystemService fs, TreeSitterParserPool pool, IBuildValidator? buildValidator = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(pool);
        _fs = fs;
        _engine = new StructuralEditEngine(fs, pool);
        _buildValidator = buildValidator;
    }

    public void Add(string virtualPath, IEditOperation operation) => _operations.Add((virtualPath, operation));

    public async Task<EditTransactionResult> CommitAsync(EditValidation validation, CancellationToken cancellationToken = default)
    {
        var paths = _operations.Select(o => o.Path).Distinct(StringComparer.Ordinal).ToList();
        foreach (var path in paths)
        {
            var check = _fs.ResolveAndValidate(path, FileAccessRights.Write);
            if (!check.IsAllowed) throw new InvalidOperationException(check.DenialReason ?? "Invalid or unsafe file path");
        }

        var batchId = Guid.NewGuid().ToString("N");
        EditLocks.Acquire(paths, batchId);
        try
        {
            return await RunAsync(paths, validation, batchId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            EditLocks.Release(paths, batchId);
        }
    }

    private async Task<EditTransactionResult> RunAsync(
        List<string> paths, EditValidation validation, string batchId, CancellationToken cancellationToken)
    {
        await ReplayLeftoverJournalsAsync(cancellationToken).ConfigureAwait(false);

        var originals = new List<byte[]>(paths.Count);
        var edited = new List<byte[]>(paths.Count);
        var rangesByPath = new Dictionary<string, IReadOnlyList<NodeRange>>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var original = await _fs.TryReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException("File not found.", path);
            var operations = _operations.Where(o => o.Path == path).Select(o => o.Operation).ToList();
            var (bytes, ranges) = _engine.ApplyInMemory(path, original, operations);
            originals.Add(original);
            edited.Add(bytes);
            rangesByPath[path] = ranges;
        }

        var journalPath = EditJournal.PathOf(batchId);
        await _fs.WriteAllBytesAsync(journalPath, EditJournal.Serialize(paths, originals, edited), cancellationToken).ConfigureAwait(false);

        var attempted = 0;
        BuildValidation? build = null;
        try
        {
            for (; attempted < paths.Count; attempted++)
                await _fs.WriteAllBytesAsync(paths[attempted], edited[attempted], cancellationToken).ConfigureAwait(false);
            if (validation.BuildPaths is not null) build = await BuildAsync(validation.BuildPaths, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            // The file whose write failed may be truncated: it is restored too.
            var failures = await RestoreAsync(paths, originals, Math.Min(attempted + 1, paths.Count)).ConfigureAwait(false);
            if (failures.Count > 0) throw new AggregateException([failure, .. failures]);
            await _fs.DeleteAsync(journalPath, recursive: false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (build?.Outcome == BuildOutcome.Red)
        {
            var failures = await RestoreAsync(paths, originals, paths.Count).ConfigureAwait(false);
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        await _fs.DeleteAsync(journalPath, recursive: false, CancellationToken.None).ConfigureAwait(false);
        var next = new Dictionary<string, int>(StringComparer.Ordinal);
        return new EditTransactionResult
        {
            Operations = _operations.Select(o =>
            {
                var index = next.GetValueOrDefault(o.Path);
                next[o.Path] = index + 1;
                return new EditedRange(o.Path, rangesByPath[o.Path][index]);
            }).ToList(),
            Build = build,
        };
    }

    private Task<BuildValidation> BuildAsync(IReadOnlyList<string> physicalPaths, CancellationToken cancellationToken)
        => _buildValidator is null
            ? Task.FromResult(new BuildValidation { Outcome = BuildOutcome.Skipped })
            : _buildValidator.ValidateAsync(physicalPaths, cancellationToken);

    private async Task<List<Exception>> RestoreAsync(List<string> paths, List<byte[]> originals, int count)
    {
        var failures = new List<Exception>();
        for (var i = 0; i < count; i++)
        {
            try
            {
                await _fs.WriteAllBytesAsync(paths[i], originals[i], CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // every restore is attempted; failures are collected and surfaced together
            catch (Exception restoreFailure)
#pragma warning restore CA1031
            {
                failures.Add(restoreFailure);
            }
        }

        return failures;
    }

    // A file is the batch's own while it holds its edited bytes, its original bytes, or the empty file a failed write leaves.
    private static bool IsOurs(byte[] current, byte[] original, byte[] editedHash)
        => current.Length == 0 || current.AsSpan().SequenceEqual(original) || SHA256.HashData(current).AsSpan().SequenceEqual(editedHash);

    private async Task ReplayLeftoverJournalsAsync(CancellationToken cancellationToken)
    {
        if (!await _fs.ExistsAsync(EditJournal.Folder, cancellationToken).ConfigureAwait(false)) return;
        var options = new VirtualEnumerationOptions(Recursive: false, SearchPattern: "*" + EditJournal.Extension);
        await foreach (var entry in _fs.EnumerateFilesAsync(EditJournal.Folder, options, cancellationToken).ConfigureAwait(false))
        {
            if (EditLocks.IsActive(Path.GetFileNameWithoutExtension(entry.VirtualPath))) continue;
            var journal = await _fs.TryReadAllBytesAsync(entry.VirtualPath, cancellationToken).ConfigureAwait(false);
            if (journal is null) continue;
            foreach (var (path, bytes, editedHash) in EditJournal.Deserialize(journal))
            {
                var current = await _fs.TryReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                if (current is not null && IsOurs(current, bytes, editedHash))
                    await _fs.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            }

            await _fs.DeleteAsync(entry.VirtualPath, recursive: false, cancellationToken).ConfigureAwait(false);
        }
    }
}
