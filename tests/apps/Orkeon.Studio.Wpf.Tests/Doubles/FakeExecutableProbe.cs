using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>An <see cref="IExecutableProbe"/> that reports a declared set of binaries.</summary>
public sealed class FakeExecutableProbe : IExecutableProbe
{
    public FakeExecutableProbe(string baseDirectory = "/opt/orkeon", params string[] existingFiles)
    {
        BaseDirectory = baseDirectory;
        Files = new HashSet<string>(existingFiles, StringComparer.Ordinal);
    }

    public string BaseDirectory { get; }

    public HashSet<string> Files { get; }

    public IReadOnlyList<string> SearchPathDirectories { get; init; } = [];

    public bool FileExists(string path) => Files.Contains(path);

    /// <summary>A probe that finds the CLI next to Studio, which is the installed layout.</summary>
    public static FakeExecutableProbe WithOrkeonInstalled(string baseDirectory = "/opt/orkeon") =>
        new(baseDirectory, System.IO.Path.Combine(baseDirectory, "orkeon"),
            System.IO.Path.Combine(baseDirectory, "orkeon.exe"));
}
