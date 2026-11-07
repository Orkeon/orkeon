using Microsoft.Extensions.Configuration;
using Orkeon.Application.Runs;
using Orkeon.Compliance.Vfs;
using Orkeon.Constants.FileSystem;

namespace Orkeon.Hosting;

/// <summary>
/// The folder of the run history: when <c>Orkeon:RunHistory</c> is enabled, <c>./.orkeon/runs</c> is mounted at
/// <see cref="RunnerVirtualRoots.Runs"/> as an internal mount, next to the checkpoint state (<see cref="RunnerState"/>),
/// so the history sinks reach it through the VFS and no agent can address it. Planned where every runner host plans its
/// mounts, so <c>orkeon run</c>, <c>orkeon runs</c> and <c>orkeon-host</c> all get it.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: creates the runs folder before the VFS mount that hosts it is provisioned.")]
internal static class RunnerRunHistory
{
    /// <summary>The folder the history is kept in, under the working directory.</summary>
    internal const string Directory = ".orkeon/runs";

    /// <summary>The section of the declared layers, or null.</summary>
    internal static string SectionPath => RunHistoryOptions.SectionName;

    /// <summary>
    /// Whether the history is on: the setting when the operator wrote one, else <paramref name="byDefault"/> (the
    /// default of the host: <c>orkeon-host</c> records, the library does not).
    /// </summary>
    internal static bool IsEnabled(IConfigurationSection? section, bool byDefault) =>
        section?["Enabled"] is { } written
            ? string.Equals(written, "true", StringComparison.OrdinalIgnoreCase)
            : byDefault;

    /// <summary>
    /// The physical runs folder the history needs, created, or null when it needs none: the history is off, its path is
    /// not under <see cref="RunnerVirtualRoots.Runs"/> (the operator mounts that place), or the operator mounts
    /// <see cref="RunnerVirtualRoots.Runs"/> themselves.
    /// </summary>
    /// <param name="section">The <c>Orkeon:RunHistory</c> section of the declared layers, or null.</param>
    /// <param name="byDefault">Whether the history is on when the section does not say.</param>
    /// <param name="cwd">The working directory; the process's when null.</param>
    /// <param name="mountRoots">The virtual roots already mounted by the operator.</param>
    internal static string? Plan(IConfigurationSection? section, bool byDefault, string? cwd, IEnumerable<string> mountRoots)
    {
        ArgumentNullException.ThrowIfNull(mountRoots);
        if (!IsEnabled(section, byDefault))
            return null;

        var path = section?["Path"];
        if (!string.IsNullOrWhiteSpace(path)
            && !string.Equals(path, RunnerVirtualRoots.Runs, StringComparison.Ordinal)
            && !path.StartsWith(RunnerVirtualRoots.Runs + "/", StringComparison.Ordinal))
        {
            return null;
        }

        if (mountRoots.Contains(RunnerVirtualRoots.Runs, StringComparer.Ordinal))
            return null;

        var physical = System.IO.Path.GetFullPath(Directory, cwd ?? System.IO.Directory.GetCurrentDirectory());
        System.IO.Directory.CreateDirectory(physical);
        return physical;
    }
}
