using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IDirectoryProbe"/>: the set of "existing" directories is
/// declared by the test, so mount validation never depends on the machine's disk.
/// </summary>
public sealed class FakeDirectoryProbe : IDirectoryProbe
{
    private readonly HashSet<string> _existing = new(StringComparer.Ordinal);

    public FakeDirectoryProbe(params string[] existingDirectories)
    {
        foreach (var directory in existingDirectories)
            _existing.Add(directory);
    }

    /// <summary>Paths passed to <see cref="Create"/>, in call order.</summary>
    public List<string> Created { get; } = new();

    /// <summary>When set, <see cref="Create"/> throws what it returns instead of creating — the shape of a disk that refuses.</summary>
    public Func<string, Exception>? CreateFault { get; init; }

    public bool Exists(string path) => _existing.Contains(path);

    public void Create(string path)
    {
        if (CreateFault is { } fault)
            throw fault(path);

        Created.Add(path);
        _existing.Add(path);
    }
}
