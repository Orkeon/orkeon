using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Run.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IExecutableProbe"/>: the install layout and the PATH are declared by
/// the test, so binary resolution never depends on what happens to be installed on the
/// machine running the suite.
/// </summary>
public sealed class FakeExecutableProbe : IExecutableProbe
{
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);
    private readonly List<string> _pathDirectories = new();

    /// <summary>Directory the fake Studio executable lives in.</summary>
    public string BaseDirectory { get; set; } = Path.Combine("/", "opt", "orkeon");

    /// <inheritdoc />
    public IReadOnlyList<string> SearchPathDirectories => _pathDirectories;

    /// <summary>Paths passed to <see cref="FileExists"/>, in call order.</summary>
    public List<string> ProbedPaths { get; } = new();

    /// <summary>Declares a file as existing.</summary>
    public FakeExecutableProbe WithFile(string path)
    {
        _files.Add(path);
        return this;
    }

    /// <summary>Appends directories to the fake PATH.</summary>
    public FakeExecutableProbe WithPathDirectories(params string[] directories)
    {
        _pathDirectories.AddRange(directories);
        return this;
    }

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        ProbedPaths.Add(path);
        return _files.Contains(path);
    }
}
