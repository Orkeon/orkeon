using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IDirectoryLister"/>: the tree a picker walks is declared by the test,
/// so browsing never depends on the machine's real directories.
/// </summary>
public sealed class FakeDirectoryLister : IDirectoryLister
{
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _files = new(StringComparer.Ordinal);

    /// <summary>Declares a directory, and registers it under its parent.</summary>
    public FakeDirectoryLister WithDirectory(string path)
    {
        if (!_children.ContainsKey(path))
            _children[path] = [];

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            WithDirectory(parent);
            if (!_children[parent].Contains(path, StringComparer.Ordinal))
                _children[parent].Add(path);
        }

        return this;
    }

    /// <summary>Declares a file, and the directory holding it.</summary>
    public FakeDirectoryLister WithFile(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
            return this;

        WithDirectory(parent);

        if (!_files.TryGetValue(parent, out var files))
            _files[parent] = files = [];

        if (!files.Contains(path, StringComparer.Ordinal))
            files.Add(path);

        return this;
    }

    public bool Exists(string path) => _children.ContainsKey(path);

    public IReadOnlyList<string> ListDirectories(string path) =>
        _children.TryGetValue(path, out var children) ? children : [];

    /// <summary>Matches the pattern's extension only — enough for the <c>*.ext</c> globs the UIs use.</summary>
    public IReadOnlyList<string> ListFiles(string path, string searchPattern)
    {
        if (!_files.TryGetValue(path, out var files))
            return [];

        var extension = Path.GetExtension(searchPattern);
        return extension.Length == 0
            ? files
            : [.. files.Where(file => Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase))];
    }
}
