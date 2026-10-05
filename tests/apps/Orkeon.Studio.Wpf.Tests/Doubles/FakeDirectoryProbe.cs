using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="IDirectoryProbe"/> over a set of names, so mount validation needs no disk.</summary>
public sealed class FakeDirectoryProbe : IDirectoryProbe
{
    public FakeDirectoryProbe(params string[] existingDirectories) =>
        Directories = new HashSet<string>(existingDirectories, StringComparer.Ordinal);

    public HashSet<string> Directories { get; }

    public List<string> Created { get; } = [];

    /// <summary>When set, <see cref="Create"/> throws what it returns instead of creating — the shape of a disk that refuses.</summary>
    public Func<string, Exception>? CreateFault { get; init; }

    public bool Exists(string path) => Directories.Contains(path);

    public void Create(string path)
    {
        if (CreateFault is { } fault)
            throw fault(path);

        Created.Add(path);
        Directories.Add(path);
    }
}
