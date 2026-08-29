using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.FileSystem;

/// <summary>
/// Builds the mount set one execution flow enters through <see cref="IFileSystemScope"/>.
/// </summary>
public static class ScopedMountComposition
{
    /// <summary>
    /// The registry an execution should enter: its own agent-facing mounts, plus the boot
    /// registry's Internal ones carried forward.
    /// <para>
    /// Entering a scope REPLACES the mount set — <c>FileSystemService.ActiveRegistry</c> is
    /// <c>scope?.Current ?? boot</c>, never a union. An execution entered on its own mounts
    /// alone therefore loses <c>/llm-logs</c> and <c>/sandbox</c> for its whole duration: the
    /// LLM exchange logger and both sandboxes read through the SAME ambient scope, and they
    /// fail by degrading rather than by throwing. It also loses the overlap check that stops
    /// an Internal mount gaining a second, agent-reachable address through one of the
    /// execution's own mounts — which makes this a confidentiality rule, not a convenience.
    /// </para>
    /// </summary>
    /// <param name="boot">The process-wide registry the host built at startup.</param>
    /// <param name="agentFacing">The mounts this execution's agents address.</param>
    /// <returns>A registry the caller owns and disposes.</returns>
    public static FileSystemRegistry ForExecution(
        FileSystemRegistry boot,
        IEnumerable<FileSystemMount> agentFacing)
    {
        ArgumentNullException.ThrowIfNull(boot);
        ArgumentNullException.ThrowIfNull(agentFacing);

        return new FileSystemRegistry([.. agentFacing, .. boot.GetInternalMounts()]);
    }
}
