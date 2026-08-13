using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// Directory browsing for the mount editor's folder picker. An interface so the picker is
/// exercised over a fake tree instead of the machine's real one.
/// </summary>
internal interface IDirectoryLister
{
    /// <summary>True when the directory exists.</summary>
    bool Exists(string path);

    /// <summary>Immediate sub-directories, sorted; an unreadable directory lists as empty.</summary>
    IReadOnlyList<string> ListDirectories(string path);
}

/// <summary>Real-disk <see cref="IDirectoryLister"/>.</summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the folder picker browses physical " +
    "directories that are about to become VFS mount roots, before any VFS exists.")]
internal sealed class PhysicalDirectoryLister : IDirectoryLister
{
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalDirectoryLister Instance { get; } = new();

    /// <inheritdoc />
    public bool Exists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public IReadOnlyList<string> ListDirectories(string path)
    {
        try
        {
            var directories = Directory.GetDirectories(path);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            return directories;
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
/// The state of the folder picker: where we are, what is under it, and where a selection
/// would land. Mount physical paths are picked, never typed blind (spec §4.5).
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the picker starts from the process' " +
    "physical working directory when no path is given, before any VFS mount exists.")]
internal sealed class DirectoryPickerModel
{
    /// <summary>The entry that walks one level up.</summary>
    public const string ParentEntry = "..";

    private readonly IDirectoryLister _lister;

    /// <summary>Creates a picker rooted at <paramref name="startPath"/> (the current directory when blank).</summary>
    public DirectoryPickerModel(string? startPath = null, IDirectoryLister? lister = null)
    {
        _lister = lister ?? PhysicalDirectoryLister.Instance;
        CurrentPath = ResolveStart(startPath);
    }

    /// <summary>The directory currently being listed — also the value a confirmation returns.</summary>
    public string CurrentPath { get; private set; }

    /// <summary>
    /// What the list shows: <c>..</c> first (unless we are at a root), then the immediate
    /// sub-directory names.
    /// </summary>
    public IReadOnlyList<string> Entries
    {
        get
        {
            var entries = new List<string>();
            if (Path.GetDirectoryName(CurrentPath) is { Length: > 0 })
                entries.Add(ParentEntry);

            foreach (var directory in _lister.ListDirectories(CurrentPath))
            {
                var name = Path.GetFileName(directory);
                entries.Add(string.IsNullOrEmpty(name) ? directory : name);
            }

            return entries;
        }
    }

    /// <summary>Enters the entry at <paramref name="index"/>; out-of-range indices are ignored.</summary>
    public void Enter(int index)
    {
        var entries = Entries;
        if (index < 0 || index >= entries.Count)
            return;

        Enter(entries[index]);
    }

    /// <summary>Enters an entry by name, or walks up on <see cref="ParentEntry"/>.</summary>
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
        var target = FieldText.ToStringOrNull(path);
        if (target is null || !_lister.Exists(target))
            return false;

        CurrentPath = Path.GetFullPath(target);
        return true;
    }

    private string ResolveStart(string? startPath)
    {
        var candidate = FieldText.ToStringOrNull(startPath);
        if (candidate is not null && _lister.Exists(candidate))
            return Path.GetFullPath(candidate);

        return Directory.GetCurrentDirectory();
    }
}
