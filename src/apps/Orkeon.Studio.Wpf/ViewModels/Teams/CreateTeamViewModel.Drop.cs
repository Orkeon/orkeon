using System.Diagnostics.CodeAnalysis;
using System.IO;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// Asks the shell to declare <see cref="Folder"/> in the settings and bind it behind
/// <see cref="TargetVirtualPath"/> (STUDIO-47): the disk pick's own path, without the picker —
/// the folder was dropped on the need.
/// </summary>
public sealed class DeclareFolderRequestedEventArgs(string targetVirtualPath, MountRights rights, string folder) : EventArgs
{
    /// <summary>The mount point to bind.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string TargetVirtualPath { get; } = targetVirtualPath;

    /// <summary>The rights the team takes on the folder.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public MountRights Rights { get; } = rights;

    /// <summary>The real folder, as it was dropped.</summary>
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string Folder { get; } = folder;
}

/// <summary>
/// Files and folders dropped on the step-1 need (STUDIO-47). The view hands the dropped paths
/// and the caret over; everything else happens here, testable without WPF: each path that
/// exists is inserted at the caret, quoted, and each dropped folder — or a dropped file's own
/// folder — is kept as a candidate for the Folders step. When the forge proposes the folders,
/// a candidate answers the row the request gave it (its name), else the default input
/// (<c>/workspace</c>, « what the team reads »), else it joins the list as an input of its
/// own; every answered row is bound to the real folder through the shell, like a disk pick.
/// </summary>
public sealed partial class CreateTeamViewModel
{
    /// <summary>The name a dropped folder takes when its own cannot be a root.</summary>
    private const string DroppedFallbackRoot = "/docs";

    private readonly IDiskEntryProbe _diskEntries;

    /// <summary>
    /// The folders dropped on this creation's need, each with the path as it was inserted —
    /// a candidate whose every path has since been taken out of the need is not proposed.
    /// </summary>
    private readonly List<(string Folder, string Inserted)> _droppedFolders = [];

    /// <summary>
    /// Raised when a dropped folder answers a row of the Folders step: the shell declares the
    /// folder in the settings and binds it behind the row, as it does a disk pick.
    /// </summary>
    public event EventHandler<DeclareFolderRequestedEventArgs>? DeclareFolderRequested;

    /// <summary>
    /// Drops <paramref name="paths"/> on the need at <paramref name="caretIndex"/>: every path
    /// that exists is inserted there, quoted and separated by a space; a path that does not is
    /// ignored. A folder, or a file's folder, becomes a candidate of the Folders step.
    /// </summary>
    /// <returns>Where the caret goes: after the insertion, or where it was when nothing was inserted.</returns>
    public int DropPaths(IReadOnlyList<string> paths, int caretIndex)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var need = _need;
        var caret = Math.Clamp(caretIndex, 0, need.Length);
        if (!IsStep1)
            return caret;

        var inserted = new List<string>();
        foreach (var raw in paths)
        {
            var path = raw?.Trim() ?? "";
            if (path.Length == 0 || inserted.Contains(path, StringComparer.Ordinal))
                continue;

            var kind = _diskEntries.KindOf(path);
            if (kind == DiskEntryKind.None)
                continue;

            // A folder is a candidate itself; a file proposes the folder it sits in.
            var folder = kind == DiskEntryKind.Directory ? path : Path.GetDirectoryName(path);

            inserted.Add(path);
            if (folder is { Length: > 0 })
                _droppedFolders.Add((folder, Quoted(path)));
        }

        if (inserted.Count == 0)
            return caret;

        var before = caret > 0 && !char.IsWhiteSpace(need[caret - 1]) ? " " : "";
        var after = caret == need.Length || !char.IsWhiteSpace(need[caret]) ? " " : "";
        var text = before + string.Join(" ", inserted.Select(Quoted)) + after;
        Need = need[..caret] + text + need[caret..];
        return caret + text.Length;
    }

    private static string Quoted(string path) => "\"" + path + "\"";

    /// <summary>A new creation starts: the folders dropped on the previous one go with it.</summary>
    private void ForgetDroppedFolders() => _droppedFolders.Clear();

    /// <summary>
    /// The dropped folders answer the rows the forge just proposed — the row of their name,
    /// else the default input still unanswered, else a row of their own — and each answer is
    /// bound through the shell.
    /// </summary>
    private void MergeDroppedFolders()
    {
        var candidates = _droppedFolders
            .Where(dropped => _need.Contains(dropped.Inserted, StringComparison.Ordinal))
            .Select(dropped => dropped.Folder)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var folder in candidates)
        {
            if (FolderRows.Any(row => row.Directory is { } directory
                && string.Equals(MountDefinition.NormalizeFolder(directory), MountDefinition.NormalizeFolder(folder), StringComparison.Ordinal)))
            {
                continue;
            }

            var name = RootNameOf(folder);
            var row = FolderRows.FirstOrDefault(r => r.Directory is null && SameRoot(r.VirtualPath, name))
                ?? FolderRows.FirstOrDefault(r => r.IsInput && r.IsUnanswered
                    && string.Equals(r.VirtualPath, TeamMountPaths.ReadRoot, StringComparison.Ordinal));
            if (row is null)
            {
                row = new WizardFolderRow(
                    new ForgeFolder(UniqueRoot(name), ForgeFolder.InputRole),
                    _strings,
                    PickRowFolder,
                    OnFolderRowPathChanged);
                FolderRows.Add(row);
            }

            row.AnswerWithDirectory(folder);
            if (TryNormalizeRootName(row.VirtualPath, out var root))
                DeclareFolderRequested?.Invoke(this, new DeclareFolderRequestedEventArgs(root, row.Rights, folder));
        }
    }

    private static bool SameRoot(string virtualPath, string root) =>
        TryNormalizeRootName(virtualPath, out var normalized) && string.Equals(normalized, root, StringComparison.Ordinal);

    /// <summary>
    /// The root a dropped folder is offered under: its own name, lowercased, every character
    /// the engine refuses turned into a dash (<c>My PDFs</c> → <c>/my-pdfs</c>).
    /// </summary>
    private static string RootNameOf(string folder)
    {
        var trimmed = MountDefinition.NormalizeFolder(folder);
        var name = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
        var cleaned = new string([.. name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')]).Trim('-');
        return TryNormalizeRootName(cleaned, out var root) ? root : DroppedFallbackRoot;
    }

    /// <summary><paramref name="root"/>, or <c>root-2</c>, <c>root-3</c>… when a row already holds it.</summary>
    private string UniqueRoot(string root)
    {
        var candidate = root;
        for (var n = 2; FolderRows.Any(row => SameRoot(row.VirtualPath, candidate)); n++)
            candidate = root + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return candidate;
    }
}
