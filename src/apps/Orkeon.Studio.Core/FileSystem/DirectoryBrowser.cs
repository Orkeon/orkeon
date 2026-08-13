using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// Directory and file browsing for the pickers of the Studio front-ends. An interface so a
/// picker is exercised over a declared tree instead of the machine's real one.
/// </summary>
public interface IDirectoryLister
{
    /// <summary>True when the directory exists.</summary>
    bool Exists(string path);

    /// <summary>Immediate sub-directories, sorted; an unreadable directory lists as empty.</summary>
    IReadOnlyList<string> ListDirectories(string path);

    /// <summary>
    /// Files of the directory matching <paramref name="searchPattern"/>, sorted; an
    /// unreadable directory lists as empty.
    /// </summary>
    IReadOnlyList<string> ListFiles(string path, string searchPattern);
}

/// <summary>Real-disk <see cref="IDirectoryLister"/>.</summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; its pickers browse physical " +
    "directories that are about to become VFS mount roots or settings files, before any " +
    "VFS exists.")]
public sealed class PhysicalDirectoryLister : IDirectoryLister
{
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalDirectoryLister Instance { get; } = new();

    /// <inheritdoc />
    public bool Exists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public IReadOnlyList<string> ListDirectories(string path) => List(() => Directory.GetDirectories(path));

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles(string path, string searchPattern) =>
        List(() => Directory.GetFiles(path, searchPattern));

    private static string[] List(Func<string[]> enumerate)
    {
        try
        {
            var entries = enumerate();
            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            return entries;
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}

/// <summary>
/// The state of a folder or file picker: where we are, what is under it, and what a
/// selection would return. Mount roots and settings files are picked, never typed blind
/// (spec §4.5).
/// <para>
/// With no <c>fileSearchPattern</c> the browser lists directories only and a confirmation
/// returns <see cref="CurrentPath"/> — the folder is chosen by standing in it. Given a
/// pattern, matching files are listed after the directories and can be returned instead.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the picker starts from the process' " +
    "physical working directory when no path is given, before any VFS mount exists.")]
public sealed class DirectoryBrowser
{
    /// <summary>The entry that walks one level up.</summary>
    public const string ParentEntry = "..";

    private readonly IDirectoryLister _lister;
    private readonly string? _fileSearchPattern;

    /// <summary>Creates a browser rooted at <paramref name="startPath"/> (the current directory when blank).</summary>
    /// <param name="startPath">Where to start; ignored when it names nothing that exists.</param>
    /// <param name="lister">Directory access (defaults to the real disk).</param>
    /// <param name="fileSearchPattern">
    /// Glob of the files to offer, e.g. <c>*.json</c>. Null lists directories only.
    /// </param>
    public DirectoryBrowser(
        string? startPath = null,
        IDirectoryLister? lister = null,
        string? fileSearchPattern = null)
    {
        _lister = lister ?? PhysicalDirectoryLister.Instance;
        _fileSearchPattern = string.IsNullOrWhiteSpace(fileSearchPattern) ? null : fileSearchPattern.Trim();
        CurrentPath = ResolveStart(startPath);
    }

    /// <summary>The directory currently being listed — also the value a folder confirmation returns.</summary>
    public string CurrentPath { get; private set; }

    /// <summary>True when the browser also offers files.</summary>
    public bool ListsFiles => _fileSearchPattern is not null;

    /// <summary>
    /// What the list shows: <c>..</c> first (unless we are at a root), then the immediate
    /// sub-directory names, then the matching file names when a pattern was given.
    /// </summary>
    public IReadOnlyList<string> Entries
    {
        get
        {
            var entries = new List<string>();
            if (Path.GetDirectoryName(CurrentPath) is { Length: > 0 })
                entries.Add(ParentEntry);

            entries.AddRange(_lister.ListDirectories(CurrentPath).Select(NameOf));

            if (_fileSearchPattern is not null)
                entries.AddRange(_lister.ListFiles(CurrentPath, _fileSearchPattern).Select(NameOf));

            return entries;
        }
    }

    /// <summary>How many entries of <see cref="Entries"/> are directories (<c>..</c> included).</summary>
    public int DirectoryEntryCount =>
        (Path.GetDirectoryName(CurrentPath) is { Length: > 0 } ? 1 : 0)
        + _lister.ListDirectories(CurrentPath).Count;

    /// <summary>True when the entry at <paramref name="index"/> is one of the listed files.</summary>
    public bool IsFileEntry(int index) =>
        _fileSearchPattern is not null && index >= DirectoryEntryCount && index < Entries.Count;

    /// <summary>
    /// The full path of the file at <paramref name="index"/>, or <see langword="null"/> when
    /// that entry is a directory.
    /// </summary>
    public string? FilePathAt(int index)
    {
        if (!IsFileEntry(index))
            return null;

        var entries = Entries;
        return index < entries.Count ? Path.Combine(CurrentPath, entries[index]) : null;
    }

    /// <summary>Enters the entry at <paramref name="index"/>; a file or an out-of-range index is ignored.</summary>
    public void Enter(int index)
    {
        var entries = Entries;
        if (index < 0 || index >= entries.Count || IsFileEntry(index))
            return;

        Enter(entries[index]);
    }

    /// <summary>Enters a sub-directory by name, or walks up on <see cref="ParentEntry"/>.</summary>
    public void Enter(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return;

        if (string.Equals(entry, ParentEntry, StringComparison.Ordinal))
        {
            if (Path.GetDirectoryName(CurrentPath) is { Length: > 0 } parent)
                CurrentPath = parent;
            return;
        }

        var candidate = Path.Combine(CurrentPath, entry);
        if (_lister.Exists(candidate))
            CurrentPath = candidate;
    }

    /// <summary>Jumps to an absolute path when it exists; returns false otherwise.</summary>
    public bool TryNavigateTo(string? path)
    {
        var target = Trimmed(path);
        if (target is null || !_lister.Exists(target))
            return false;

        CurrentPath = Path.GetFullPath(target);
        return true;
    }

    private static string NameOf(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private string ResolveStart(string? startPath)
    {
        var candidate = Trimmed(startPath);
        if (candidate is not null && _lister.Exists(candidate))
            return Path.GetFullPath(candidate);

        return Directory.GetCurrentDirectory();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
