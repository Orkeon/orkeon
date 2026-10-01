using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="IDiskEntryProbe"/> over two sets of names, so a drop needs no disk.</summary>
public sealed class FakeDiskEntryProbe : IDiskEntryProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    public FakeDiskEntryProbe WithFiles(params string[] files)
    {
        Files.UnionWith(files);
        return this;
    }

    public FakeDiskEntryProbe WithDirectories(params string[] directories)
    {
        Directories.UnionWith(directories);
        return this;
    }

    public DiskEntryKind KindOf(string path)
    {
        if (Directories.Contains(path))
            return DiskEntryKind.Directory;

        return Files.Contains(path) ? DiskEntryKind.File : DiskEntryKind.None;
    }
}
