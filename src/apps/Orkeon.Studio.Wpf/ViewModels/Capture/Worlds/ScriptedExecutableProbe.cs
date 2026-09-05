using System.Diagnostics.CodeAnalysis;
using System.IO;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// A machine that either has the CLI installed or does not, on purpose.
/// <para>
/// The absent case is not an edge: it is the first-run screen — the banner on Run, the
/// diagnostic that cannot answer, the Tester screen that refuses. None of it is photographable
/// against a real probe on a developer's machine, where the binary is always there.
/// </para>
/// </summary>
/// <param name="binaryDirectory">Directory the binary is reported in; null for a machine without one.</param>
internal sealed class ScriptedExecutableProbe(string? binaryDirectory) : IExecutableProbe
{
    /// <summary>The file names <see cref="OrkeonBinaryLocator"/> looks for.</summary>
    private static readonly string[] BinaryNames = ["orkeon", "orkeon.exe"];

    /// <inheritdoc />
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public string BaseDirectory { get; } = binaryDirectory ?? Path.GetTempPath();

    /// <inheritdoc />
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    public IReadOnlyList<string> SearchPathDirectories { get; } =
        binaryDirectory is { Length: > 0 } ? [binaryDirectory] : [];

    /// <inheritdoc />
    public bool FileExists(string path) =>
        binaryDirectory is { Length: > 0 }
        && BinaryNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
        && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? ""),
            Path.TrimEndingDirectorySeparator(binaryDirectory),
            StringComparison.OrdinalIgnoreCase);
}
