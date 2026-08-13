using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Config.Tests.Doubles;

/// <summary>
/// In-memory <see cref="IExecutableProbe"/>: whether an <c>orkeon</c> binary exists, and
/// where, is declared by the test rather than read off the machine running the suite.
/// </summary>
public sealed class FakeExecutableProbe : IExecutableProbe
{
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);

    /// <summary>Directory the fake Studio executable lives in.</summary>
    public string BaseDirectory { get; set; } = Path.Combine("/", "opt", "orkeon");

    /// <summary>Directories of the fake PATH.</summary>
    public IReadOnlyList<string> SearchPathDirectories { get; set; } = [];

    /// <summary>Declares a file as existing.</summary>
    public FakeExecutableProbe WithFile(string path)
    {
        _files.Add(path);
        return this;
    }

    public bool FileExists(string path) => _files.Contains(path);
}
