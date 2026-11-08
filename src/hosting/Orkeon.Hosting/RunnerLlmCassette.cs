using Orkeon.Compliance.Vfs;
using Orkeon.Constants.Cli;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting;

/// <summary>
/// The cassette of <c>--llm-record</c> and <c>--llm-replay</c>, one rule for every host that offers
/// them. The file's folder is mounted at <see cref="RunnerVirtualRoots.LlmCassette"/> (a mount is always
/// a folder, never one file, as for <see cref="RunnerLlmLog"/>): read-write to record, read-only to
/// replay. The two switches and <c>--llm-mock</c> are exclusive two by two.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves the user-supplied cassette and creates its folder before the VFS mount that hosts it is provisioned.")]
internal static class RunnerLlmCassette
{
    /// <summary>The cassette a run records to or replays from.</summary>
    /// <param name="PhysicalPath">The full physical path of the file.</param>
    /// <param name="Records">True for <c>--llm-record</c>, false for <c>--llm-replay</c>.</param>
    internal sealed record Choice(string PhysicalPath, bool Records);

    /// <summary>The cassette the switches name, or null when neither is given.</summary>
    internal static Choice? Resolve(string? record, string? replay)
    {
        if (!string.IsNullOrWhiteSpace(record))
            return new Choice(Path.GetFullPath(record), Records: true);
        return string.IsNullOrWhiteSpace(replay) ? null : new Choice(Path.GetFullPath(replay), Records: false);
    }

    /// <summary>
    /// Refuses two of <c>--llm-record</c>, <c>--llm-replay</c> and <c>--llm-mock</c> given together,
    /// writing the refusal to <paramref name="error"/>.
    /// </summary>
    /// <returns>True when the run may go on.</returns>
    internal static bool EnsureExclusive(string? record, string? replay, string? mock, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var given = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(record))
            given.Add("--" + RunOptionNames.LlmRecord);
        if (!string.IsNullOrWhiteSpace(replay))
            given.Add("--" + RunOptionNames.LlmReplay);
        if (!string.IsNullOrWhiteSpace(mock))
            given.Add("--" + RunOptionNames.LlmMock);
        if (given.Count < 2)
            return true;

        error.WriteLine($"ERROR: {string.Join(" and ", given)} are exclusive: a run records its calls, replays a cassette or answers from a mock file, not two of them.");
        return false;
    }

    /// <summary>Refuses a replay of a cassette that does not exist.</summary>
    /// <returns>True when the run may go on.</returns>
    internal static bool EnsureReadable(Choice? choice, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (choice is null || choice.Records || File.Exists(choice.PhysicalPath))
            return true;

        error.WriteLine($"ERROR: --{RunOptionNames.LlmReplay} names a cassette that does not exist: {choice.PhysicalPath}");
        return false;
    }

    /// <summary>
    /// Refuses a cassette whose folder holds the crew's or the script's folder (or is it): the folder is an
    /// internal mount, reserved by the runtime, so the crew could no longer be read.
    /// </summary>
    /// <returns>True when the run may go on.</returns>
    internal static bool EnsureSeparateFrom(Choice? choice, string? inputDirectory, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (choice is null || inputDirectory is null)
            return true;

        var folder = Path.GetDirectoryName(choice.PhysicalPath)!;
        if (!PhysicalPathContainment.IsUnder(Path.GetFullPath(inputDirectory), folder))
            return true;

        error.WriteLine(
            $"ERROR: --{Switch(choice)}: the folder of the cassette ({folder}) holds the folder of the crew or script "
            + $"({Path.GetFullPath(inputDirectory)}). The folder is mounted at {RunnerVirtualRoots.LlmCassette} and "
            + "reserved by the runtime, so the crew could not be read. Move the cassette to a folder "
            + "that holds neither the crew nor the script.");
        return false;
    }

    /// <summary>
    /// Refuses a cassette to record outside the working directory without <c>--allow-external-mounts</c>:
    /// recording writes there.
    /// </summary>
    /// <returns>True when the run may go on.</returns>
    internal static bool EnsureWriteAllowed(Choice? choice, bool allowExternalMounts, TextWriter error, string? cwd = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        cwd ??= Directory.GetCurrentDirectory();
        if (choice is not { Records: true } || allowExternalMounts || PhysicalPathContainment.IsUnder(choice.PhysicalPath, cwd))
            return true;

        error.WriteLine($"ERROR: --allow-external-mounts is required when --{RunOptionNames.LlmRecord} points outside the current working directory (or set ORKEON_ALLOW_EXTERNAL_MOUNTS=1).");
        error.WriteLine($"       cassette    : {choice.PhysicalPath}");
        error.WriteLine($"       cwd         : {cwd}");
        return false;
    }

    /// <summary>
    /// The internal mount of the cassette's folder, or nothing when neither switch is given. Recording
    /// creates the folder: a mount base path must exist before the registry is built.
    /// </summary>
    internal static IReadOnlyList<string> CreateMounts(Choice? choice)
    {
        if (choice is null)
            return [];

        var folder = Path.GetDirectoryName(choice.PhysicalPath)!;
        if (choice.Records)
            Directory.CreateDirectory(folder);
        return [$"{FileSystemMount.Quote(folder)}:{RunnerVirtualRoots.LlmCassette}:{(choice.Records ? "rw" : "ro")}"];
    }

    /// <summary>The folder a replay reads as an input, readable without the external-mounts opt-in.</summary>
    internal static IReadOnlyList<string> InputDirectories(Choice? choice) =>
        choice is { Records: false } ? [Path.GetDirectoryName(choice.PhysicalPath)!] : [];

    /// <summary>The virtual path of the cassette under the mount, or null when neither switch is given.</summary>
    internal static string? VirtualPath(Choice? choice) =>
        choice is null ? null : $"{RunnerVirtualRoots.LlmCassette}/{Path.GetFileName(choice.PhysicalPath)}";

    private static string Switch(Choice choice) => choice.Records ? RunOptionNames.LlmRecord : RunOptionNames.LlmReplay;
}
