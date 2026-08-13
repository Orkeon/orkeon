using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Run.Tests.Doubles;

/// <summary>
/// In-memory <see cref="ITargetProbe"/>: the tree is declared by the test, so target
/// detection never depends on the machine's disk. Paths are compared with '/' separators
/// whatever <see cref="Path.Combine"/> produced.
/// </summary>
public sealed class FakeTargetProbe : ITargetProbe
{
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);

    /// <summary>Declares existing directories.</summary>
    public FakeTargetProbe WithDirectories(params string[] paths)
    {
        foreach (var path in paths)
            _directories.Add(Normalize(path));

        return this;
    }

    /// <summary>Declares existing files.</summary>
    public FakeTargetProbe WithFiles(params string[] paths)
    {
        foreach (var path in paths)
            _files.Add(Normalize(path));

        return this;
    }

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public bool FileExists(string path) => _files.Contains(Normalize(path));

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern)
    {
        var root = Normalize(directory).TrimEnd('/') + "/";
        var suffix = searchPattern.TrimStart('*');

        return [.. _files
            .Where(file => file.StartsWith(root, StringComparison.Ordinal))
            .Where(file => !file[root.Length..].Contains('/', StringComparison.Ordinal))
            .Where(file => file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal)];
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
