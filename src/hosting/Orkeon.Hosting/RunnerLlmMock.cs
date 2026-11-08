using Orkeon.Compliance.Vfs;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting;

/// <summary>
/// The responses file of <c>--llm-mock</c>, one rule for every host that offers it: <c>orkeon run</c>
/// for a YAML crew or a procedural script. The file's folder is mounted read-only at
/// <see cref="RunnerVirtualRoots.LlmMock"/> (a mount is always a folder, never one file, as for
/// <see cref="RunnerLlmLog"/>), and every LLM profile of the run — the default and the named ones —
/// becomes <c>Provider: mock</c> reading that file through the VFS.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves the user-supplied --llm-mock file before the VFS mount that hosts it is provisioned.")]
internal static class RunnerLlmMock
{
    /// <summary>The full physical path of the responses file, or null when <c>--llm-mock</c> is not given.</summary>
    /// <param name="path"><c>--llm-mock</c>, or null.</param>
    internal static string? ResolvePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    /// <summary>
    /// Refuses a responses file that does not exist, writing the refusal to <paramref name="error"/>.
    /// </summary>
    /// <param name="physicalPath">The resolved file, or null when the option is off.</param>
    /// <param name="error">Where the refusal is written.</param>
    /// <returns>True when the run may go on.</returns>
    internal static bool EnsureReadable(string? physicalPath, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (physicalPath is null || File.Exists(physicalPath))
            return true;

        error.WriteLine($"ERROR: --llm-mock names a file that does not exist: {physicalPath}");
        return false;
    }

    /// <summary>
    /// Refuses a responses file whose folder holds the crew's or the script's folder (or is it): the
    /// folder is an internal mount, a boundary in physical space, so everything under it would be
    /// reserved by the runtime and the crew could no longer be read.
    /// </summary>
    /// <param name="physicalPath">The resolved file, or null when the option is off.</param>
    /// <param name="inputDirectory">The folder of the crew definition or of the script.</param>
    /// <param name="error">Where the refusal is written.</param>
    /// <returns>True when the run may go on.</returns>
    internal static bool EnsureSeparateFrom(string? physicalPath, string? inputDirectory, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (physicalPath is null || inputDirectory is null)
            return true;

        var folder = Path.GetDirectoryName(physicalPath)!;
        if (!PhysicalPathContainment.IsUnder(Path.GetFullPath(inputDirectory), folder))
            return true;

        error.WriteLine(
            $"ERROR: --llm-mock: the folder of the responses file ({folder}) holds the folder of the crew or script "
            + $"({Path.GetFullPath(inputDirectory)}). The folder is mounted read-only at {RunnerVirtualRoots.LlmMock} and "
            + "reserved by the runtime, so the crew could not be read. Move the responses file to a folder "
            + "that holds neither the crew nor the script.");
        return false;
    }

    /// <summary>The internal read-only mount of the file's folder, or nothing when the option is off.</summary>
    internal static IReadOnlyList<string> CreateMounts(string? physicalPath) =>
        physicalPath is null
            ? []
            : [$"{FileSystemMount.Quote(Path.GetDirectoryName(physicalPath)!)}:{RunnerVirtualRoots.LlmMock}:ro"];

    /// <summary>The folder the run reads as an input, readable without the external-mounts opt-in.</summary>
    internal static IReadOnlyList<string> InputDirectories(string? physicalPath) =>
        physicalPath is null ? [] : [Path.GetDirectoryName(physicalPath)!];

    /// <summary>The virtual path of the responses file under the mount, or null when the option is off.</summary>
    internal static string? VirtualPath(string? physicalPath) =>
        physicalPath is null ? null : $"{RunnerVirtualRoots.LlmMock}/{Path.GetFileName(physicalPath)}";
}
