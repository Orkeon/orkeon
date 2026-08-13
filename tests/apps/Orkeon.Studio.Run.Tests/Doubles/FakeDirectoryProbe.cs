using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Run.Tests.Doubles;

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

    public bool Exists(string path) => _existing.Contains(path);

    public void Create(string path)
    {
        Created.Add(path);
        _existing.Add(path);
    }
}
