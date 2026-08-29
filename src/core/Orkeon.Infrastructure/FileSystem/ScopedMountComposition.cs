using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.FileSystem;

/// <summary>
/// Builds the mount set one execution flow enters through <see cref="IFileSystemScope"/>.
/// </summary>
public static class ScopedMountComposition
{
    /// <summary>
    /// The registry an execution should enter: the boot mounts, with this execution's own ones
    /// substituted at the virtual paths they claim.
    /// <para>
    /// Entering a scope REPLACES the mount set — <c>FileSystemService.ActiveRegistry</c> is
    /// <c>scope?.Current ?? boot</c>, never a union. So the composition here decides everything
    /// the execution can reach, and it has to satisfy two requirements at once.
    /// </para>
    /// <para>
    /// Carrying the boot mounts forward is what keeps the execution able to run at all. The
    /// Internal ones (<c>/llm-logs</c>, <c>/sandbox</c>) are a confidentiality matter: the
    /// exchange logger and both sandboxes read through this same ambient scope and fail by
    /// degrading rather than by throwing, and dropping them also drops the overlap check that
    /// stops an Internal mount gaining a second, agent-reachable address. The agent-facing ones
    /// matter just as concretely: <c>orkeon-host</c> mounts each hosted crew's directory under
    /// <c>/crews</c> and then loads the crew BY that virtual path, from inside the scope. An
    /// execution entered on its own mounts alone cannot find its own definition.
    /// </para>
    /// <para>
    /// Substitution, rather than addition, is the point of the whole mechanism: two hosted crews
    /// may each be granted <c>/output</c> over two different physical folders, which one flat
    /// boot registry — where a virtual path is globally unique — cannot express. A boot mount at
    /// a path this execution claims is therefore shadowed, not a collision.
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

        var scoped = agentFacing as IReadOnlyList<FileSystemMount> ?? [.. agentFacing];
        var claimed = scoped.Select(m => m.VirtualPath).ToHashSet(StringComparer.Ordinal);

        // Ordinal, matching FileSystemRegistry's own duplicate check: a path this composition
        // considered distinct but the registry considers equal would throw out of a DI factory.
        return new FileSystemRegistry(
            [.. scoped, .. boot.GetMounts().Where(m => !claimed.Contains(m.VirtualPath))]);
    }
}
