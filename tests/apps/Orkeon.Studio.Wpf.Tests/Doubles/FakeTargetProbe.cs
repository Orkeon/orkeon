using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="ITargetProbe"/> over declared file and directory names, no disk involved.</summary>
public sealed class FakeTargetProbe : ITargetProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    public FakeTargetProbe WithFile(string path)
    {
        Files.Add(path);
        return this;
    }

    public FakeTargetProbe WithDirectory(string path)
    {
        Directories.Add(path);
        return this;
    }

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public bool FileExists(string path) => Files.Contains(path);

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern)
    {
        var prefix = directory.EndsWith('/') ? directory : directory + "/";
        var suffix = searchPattern.TrimStart('*');

        return [.. Files
            .Where(f => f.StartsWith(prefix, StringComparison.Ordinal)
                && !f[prefix.Length..].Contains('/', StringComparison.Ordinal)
                && f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)];
    }
}
