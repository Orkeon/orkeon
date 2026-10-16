using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.DependencyInjection;

/// <summary>
/// The <c>--mount &lt;physical:virtual:rights&gt;</c> specs of the REPL, made absolute: how the
/// coding-agent REPL is given a <c>/workspace</c> (and e.g. <c>/output</c>) to read and edit,
/// mirroring <c>Scripting.Cli</c>'s <c>-m</c> flag.
/// </summary>
/// <remarks>
/// Where a spec lands is no longer this class's business (GAP-49, decision 3): the REPL places its
/// <c>--mount</c> arguments as the runners place theirs, by virtual root against the declared entries
/// (<c>RunnerHost.ConfigureMounts</c>), after the runners' guards have refused what cannot be honoured.
/// It used to append each spec after the declared entries, so a <c>--mount</c> on a declared root
/// produced "Duplicate virtual paths" at the console's start. The leading physical segment is resolved
/// to an absolute path; the rest of the spec (<c>:virtual:rights[;subpath:rights;...]</c>) is
/// preserved verbatim for <c>FileSystemMount.Parse</c>.
/// </remarks>
internal static class CliWorkspaceMountBootstrapper
{
    /// <summary>The <c>--mount</c> specs, each physical segment resolved to an absolute path, in order.</summary>
    /// <param name="mountSpecs">The <c>--mount</c> specs as typed.</param>
    public static IReadOnlyList<string> ResolvePhysicalSegments(IReadOnlyList<string> mountSpecs)
    {
        ArgumentNullException.ThrowIfNull(mountSpecs);
        return [.. mountSpecs.Select(ResolvePhysicalSegment)];
    }

    /// <summary>
    /// Resolves a spec's physical segment to an absolute path, leaving everything else alone. A
    /// spec the grammar cannot read is returned verbatim so <c>FileSystemMount.Parse</c> can
    /// surface its own precise format error.
    /// <para>
    /// Where the segment ends is the domain type's business. Splitting on the first <c>':'</c> —
    /// what this method used to do — makes <c>C:\src:/workspace:ro</c> resolve the physical path
    /// <c>"C"</c> against the working directory and emit a corrupt mount string.
    /// </para>
    /// </summary>
    /// <param name="spec">A <c>--mount</c> spec as typed.</param>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves a user-supplied --mount physical path to absolute for the VFS mount parser, before any VFS mount exists.")]
    public static string ResolvePhysicalSegment(string spec)
    {
        if (FileSystemMount.TryGetBasePath(spec) is not { } physical)
            return spec;

        return FileSystemMount.WithBasePath(spec, Path.GetFullPath(physical));
    }
}
