using Orkeon.Studio.Config.Presentation;

namespace Orkeon.Studio.Config.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IDirectoryLister"/>: the tree the folder picker walks is declared
/// by the test, so browsing never depends on the machine's real directories.
/// </summary>
public sealed class FakeDirectoryLister : IDirectoryLister
{
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);

    /// <summary>Declares a directory, and registers it under its parent.</summary>
    public FakeDirectoryLister WithDirectory(string path)
    {
        if (!_children.ContainsKey(path))
            _children[path] = new List<string>();

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            WithDirectory(parent);
            if (!_children[parent].Contains(path, StringComparer.Ordinal))
                _children[parent].Add(path);
        }

        return this;
    }

    public bool Exists(string path) => _children.ContainsKey(path);

    public IReadOnlyList<string> ListDirectories(string path) =>
        _children.TryGetValue(path, out var children) ? children : [];
}
