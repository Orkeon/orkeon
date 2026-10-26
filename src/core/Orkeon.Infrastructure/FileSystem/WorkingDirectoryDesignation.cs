using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.FileSystem;

/// <summary>
/// How a runner designates the working directory of the registry it builds: the directory a
/// relative path given to a file tool resolves against, and the one <c>shell_command</c> runs in
/// when it names none.
/// <para>
/// The registry takes <see cref="RunnerVirtualRoots.Workspace"/> by itself when it is mounted.
/// A run that mounts no workspace (<c>orkeon run</c> on a crew writing to <c>/output</c>, a hosted
/// crew, a Studio launch) used to get one by accident: <c>shell_command</c> picked the first mount
/// that granted the rights asked, in the registry's order (longest virtual path first), which put a
/// read in <c>/session-history</c> when that mount was present. The runner now names it instead:
/// the configured <see cref="ConfigurationKeys.FileSystemWorkingDirectory"/>, else the workspace,
/// else <see cref="RunnerVirtualRoots.Output"/> when an agent-facing mount claims it, else none.
/// </para>
/// </summary>
internal static class WorkingDirectoryDesignation
{
    /// <summary>Designates the working directory of a registry a runner has just built.</summary>
    /// <param name="registry">The registry.</param>
    /// <param name="configured">The value of <see cref="ConfigurationKeys.FileSystemWorkingDirectory"/>, if any.</param>
    /// <exception cref="InvalidOperationException">The configured directory is in no agent-facing mount.</exception>
    public static void Apply(FileSystemRegistry registry, string? configured)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                registry.SetWorkingDirectory(configured);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"'{ConfigurationKeys.FileSystemWorkingDirectory}': {ex.Message}", ex);
            }

            return;
        }

        if (registry.WorkingDirectory is not null)
            return;

        if (registry.GetAvailableMounts().Any(m => string.Equals(m.VirtualPath, RunnerVirtualRoots.Output, StringComparison.Ordinal)))
            registry.SetWorkingDirectory(RunnerVirtualRoots.Output);
    }

    /// <summary>
    /// Carries the boot registry's working directory into a per-execution registry when the
    /// execution still reaches it, else designates one for the execution's own mounts.
    /// </summary>
    /// <param name="boot">The process-wide registry.</param>
    /// <param name="execution">The registry composed for one execution.</param>
    public static void CarryForward(FileSystemRegistry boot, FileSystemRegistry execution)
    {
        ArgumentNullException.ThrowIfNull(boot);
        ArgumentNullException.ThrowIfNull(execution);

        if (boot.WorkingDirectory is { } inherited)
        {
            try
            {
                execution.SetWorkingDirectory(inherited);
                return;
            }
            catch (ArgumentException)
            {
                // The execution shadowed or dropped that mount: fall through to its own default.
            }
        }

        Apply(execution, configured: null);
    }
}
